using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using VSCopilotSwitch.Core.Providers.OpenAiCompatible;

namespace VSCopilotSwitch.Core.Providers.DeepSeek;

public sealed class DeepSeekModelProvider : IModelProvider
{
    private readonly OpenAiCompatibleModelProvider _inner;
    private readonly ConcurrentDictionary<string, string> _reasoningByToolCallId = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _reasoningCacheOrder = new();

    public DeepSeekModelProvider(HttpClient httpClient, DeepSeekProviderOptions options)
    {
        _inner = new OpenAiCompatibleModelProvider(httpClient, new OpenAiCompatibleProviderOptions
        {
            ProviderName = string.IsNullOrWhiteSpace(options.ProviderName) ? "deepseek" : options.ProviderName,
            PublicProviderName = "DeepSeek",
            BaseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "https://api.deepseek.com" : options.BaseUrl,
            ApiKey = options.ApiKey,
            Timeout = options.Timeout,
            ApiPathPrefix = null,
            Models = options.Models.Select(ToOpenAiCompatibleModel).ToArray()
        });
    }

    public string Name => _inner.Name;

    public Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CancellationToken cancellationToken = default)
        => _inner.ListModelsAsync(cancellationToken);

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
        => ChatWithThinkingAsync(request, cancellationToken);

    public async IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var preparedRequest = PrepareThinkingRequest(request);
        var reasoning = new StringBuilder();
        var toolCalls = new Dictionary<int, ChatToolCall>();
        ChatStreamChunk? finalChunk = null;

        await using var enumerator = _inner.ChatStreamAsync(preparedRequest, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            ChatStreamChunk chunk;
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }

                chunk = enumerator.Current;
            }
            catch (ProviderException ex)
            {
                throw RewriteReasoningException(ex);
            }

            finalChunk = chunk;
            if (!string.IsNullOrEmpty(chunk.Delta?.ReasoningContent))
            {
                reasoning.Append(chunk.Delta.ReasoningContent);
            }

            MergeToolCalls(toolCalls, chunk.Delta?.ToolCalls);
            yield return chunk;
        }

        if (finalChunk?.Done == true)
        {
            CacheReasoningForToolCalls(reasoning.ToString(), toolCalls.Values.ToArray());
        }
    }

    private async Task<ChatResponse> ChatWithThinkingAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        var preparedRequest = PrepareThinkingRequest(request);
        try
        {
            var response = await _inner.ChatAsync(preparedRequest, cancellationToken);
            CacheReasoningForToolCalls(response.ReasoningContent, response.ToolCalls);
            return response;
        }
        catch (ProviderException ex)
        {
            throw RewriteReasoningException(ex);
        }
    }

    private ChatRequest PrepareThinkingRequest(ChatRequest request)
    {
        if (!ShouldUseThinkingPath(request))
        {
            return StripUnsafeReasoningContent(request);
        }

        var messages = request.Messages
            .Select(message =>
            {
                if (!string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    || message.ToolCalls is not { Count: > 0 })
                {
                    return StripReasoningContent(message);
                }

                if (!string.IsNullOrWhiteSpace(message.ReasoningContent))
                {
                    return message;
                }

                var restored = RestoreReasoningContent(message.ToolCalls);
                return string.IsNullOrWhiteSpace(restored)
                    ? message
                    : message with { ReasoningContent = restored };
            })
            .ToArray();

        return request with { Messages = messages };
    }

    private static ChatRequest StripUnsafeReasoningContent(ChatRequest request)
        => request with { Messages = request.Messages.Select(StripReasoningContent).ToArray() };

    private static ChatMessage StripReasoningContent(ChatMessage message)
        => string.IsNullOrWhiteSpace(message.ReasoningContent)
            ? message
            : message with { ReasoningContent = null };

    private bool ShouldUseThinkingPath(ChatRequest request)
        => !IsExplicitFalse(request.Think)
            && (IsThinkingModel(request.UpstreamModel)
            || !string.IsNullOrWhiteSpace(request.ReasoningEffort)
            || IsPresent(request.Thinking)
            || IsThinkingEnabled(request.Think));

    private static bool IsThinkingModel(string model)
        => model.Contains("reasoner", StringComparison.OrdinalIgnoreCase)
            || model.Contains("deepseek-v4", StringComparison.OrdinalIgnoreCase)
            || model.Contains("deepseek-chat-v4", StringComparison.OrdinalIgnoreCase)
            || model.Contains("deepseek-v3.1", StringComparison.OrdinalIgnoreCase);

    private static bool IsPresent(System.Text.Json.JsonElement? value)
        => value is { ValueKind: not System.Text.Json.JsonValueKind.Null and not System.Text.Json.JsonValueKind.Undefined };

    private static bool IsThinkingEnabled(System.Text.Json.JsonElement? value)
        => value switch
        {
            { ValueKind: System.Text.Json.JsonValueKind.False } => false,
            { ValueKind: System.Text.Json.JsonValueKind.Null } => false,
            { ValueKind: System.Text.Json.JsonValueKind.Undefined } => false,
            not null => true,
            _ => false
        };

    private static bool IsExplicitFalse(System.Text.Json.JsonElement? value)
        => value is { ValueKind: System.Text.Json.JsonValueKind.False };

    private string? RestoreReasoningContent(IReadOnlyList<ChatToolCall> toolCalls)
    {
        foreach (var toolCall in toolCalls)
        {
            if (!string.IsNullOrWhiteSpace(toolCall.Id)
                && _reasoningByToolCallId.TryGetValue(toolCall.Id, out var reasoning)
                && !string.IsNullOrWhiteSpace(reasoning))
            {
                return reasoning;
            }
        }

        return null;
    }

    private void CacheReasoningForToolCalls(string? reasoningContent, IReadOnlyList<ChatToolCall>? toolCalls)
    {
        if (string.IsNullOrWhiteSpace(reasoningContent) || toolCalls is null)
        {
            return;
        }

        foreach (var toolCall in toolCalls)
        {
            if (string.IsNullOrWhiteSpace(toolCall.Id))
            {
                continue;
            }

            _reasoningByToolCallId[toolCall.Id] = reasoningContent;
            _reasoningCacheOrder.Enqueue(toolCall.Id);
        }

        TrimReasoningCache();
    }

    private void TrimReasoningCache()
    {
        while (_reasoningCacheOrder.Count > 512 && _reasoningCacheOrder.TryDequeue(out var id))
        {
            _reasoningByToolCallId.TryRemove(id, out _);
        }
    }

    private static void MergeToolCalls(IDictionary<int, ChatToolCall> target, IReadOnlyList<ChatToolCall>? toolCalls)
    {
        if (toolCalls is null)
        {
            return;
        }

        foreach (var toolCall in toolCalls)
        {
            var index = toolCall.Index ?? target.Count;
            target[index] = target.TryGetValue(index, out var existing)
                ? MergeToolCall(existing, toolCall)
                : toolCall;
        }
    }

    private static ChatToolCall MergeToolCall(ChatToolCall existing, ChatToolCall delta)
        => existing with
        {
            Id = string.IsNullOrWhiteSpace(delta.Id) ? existing.Id : delta.Id,
            Type = string.IsNullOrWhiteSpace(delta.Type) ? existing.Type : delta.Type,
            Function = new ChatFunctionCall(
                string.IsNullOrWhiteSpace(delta.Function.Name) ? existing.Function.Name : delta.Function.Name,
                string.Concat(existing.Function.Arguments, delta.Function.Arguments))
        };

    private static ProviderException RewriteReasoningException(ProviderException exception)
    {
        if (exception.Kind != ProviderErrorKind.InvalidRequest
            || !exception.PublicMessage.Contains("reasoning", StringComparison.OrdinalIgnoreCase))
        {
            return exception;
        }

        return new ProviderException(
            ProviderErrorKind.InvalidRequest,
            "DeepSeek thinking 请求被上游拒绝。若错误发生在 Agent 工具回合，请重新发起该任务；VSCopilotSwitch 会在同一进程内缓存上一轮 reasoning_content 并尝试自动补回。原始错误：" + exception.PublicMessage,
            exception);
    }

    private static OpenAiCompatibleModelOptions ToOpenAiCompatibleModel(DeepSeekModelOptions model)
        => new(model.UpstreamModel, model.Name, model.DisplayName, model.Aliases);
}
