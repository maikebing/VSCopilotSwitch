using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VSCopilotSwitch.Core.Providers.Claude;

public sealed partial class ClaudeModelProvider : IModelProvider
{
    private readonly HttpClient _httpClient;
    private readonly ClaudeProviderOptions _options;
    private readonly Uri _baseUri;
    private readonly string _apiKey;
    private readonly string _providerName;
    private readonly string _anthropicVersion;
    private readonly int _maxTokens;

    public ClaudeModelProvider(HttpClient httpClient, ClaudeProviderOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _providerName = string.IsNullOrWhiteSpace(options.ProviderName) ? "claude" : options.ProviderName.Trim();
        _baseUri = NormalizeBaseUrl(options.BaseUrl);
        _apiKey = NormalizeApiKey(options.ApiKey);
        _anthropicVersion = string.IsNullOrWhiteSpace(options.AnthropicVersion) ? "2023-06-01" : options.AnthropicVersion.Trim();
        _maxTokens = options.MaxTokens > 0 ? options.MaxTokens : 4096;

        if (options.Timeout > TimeSpan.Zero)
        {
            _httpClient.Timeout = options.Timeout;
        }
    }

    public string Name => _providerName;

    public async Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        if (_options.Models.Count > 0)
        {
            return _options.Models.Select(ToProviderModel).ToArray();
        }

        using var request = CreateRequest(HttpMethod.Get, "models");
        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync(response, cancellationToken);
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync(
            contentStream,
            ClaudeProviderJsonContext.Default.ClaudeModelListResponse,
            cancellationToken);
        var models = payload?.Data?
            .Select(model => model.Id?.Trim())
            .Where(modelId => !string.IsNullOrWhiteSpace(modelId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(modelId => ToProviderModel(new ClaudeModelOptions(modelId!)))
            .ToArray() ?? Array.Empty<ProviderModel>();

        if (models.Length == 0)
        {
            throw new ProviderException(ProviderErrorKind.Unavailable, "Claude 未返回可用模型列表，请检查 API Key 权限或平台模型配置。");
        }

        return models;
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var upstreamRequest = CreateMessagesRequest(request, stream: false);
        using var httpRequest = CreateJsonRequest(upstreamRequest);
        using var response = await SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync(response, cancellationToken);
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync(
            contentStream,
            ClaudeProviderJsonContext.Default.ClaudeMessageResponse,
            cancellationToken);
        ThrowIfErrorPayload(payload?.Error);

        var content = ExtractText(payload?.Content);
        return new ChatResponse(
            request.Model,
            content,
            NormalizeDoneReason(payload?.StopReason),
            ToChatToolCalls(payload?.Content),
            ToChatUsage(payload?.Usage));
    }

    public async IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var upstreamRequest = CreateMessagesRequest(request, stream: true);
        using var httpRequest = CreateJsonRequest(upstreamRequest);
        using var response = await SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync(response, cancellationToken);
        }

        var doneReason = "stop";
        ChatUsage? doneUsage = null;
        var completed = false;
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(contentStream, Encoding.UTF8);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            var data = NormalizeSseDataLine(line);
            if (string.IsNullOrWhiteSpace(data))
            {
                continue;
            }

            ClaudeStreamEvent? streamEvent;
            try
            {
                streamEvent = JsonSerializer.Deserialize(
                    data,
                    ClaudeProviderJsonContext.Default.ClaudeStreamEvent);
            }
            catch (JsonException ex)
            {
                throw new ProviderException(ProviderErrorKind.UpstreamError, "Claude 返回了无法解析的流式响应。", ex);
            }

            ThrowIfErrorPayload(streamEvent?.Error);
            var delta = streamEvent?.Delta;
            var text = delta?.Text;
            var contentBlock = streamEvent?.ContentBlock;
            if (string.Equals(streamEvent?.Type, "content_block_start", StringComparison.OrdinalIgnoreCase)
                && contentBlock is { } toolUseBlock
                && string.Equals(toolUseBlock.Type, "tool_use", StringComparison.OrdinalIgnoreCase))
            {
                yield return new ChatStreamChunk(
                    request.Model,
                    string.Empty,
                    Done: false,
                    Delta: new ChatDelta(
                        "assistant",
                        null,
                        new[]
                        {
                            new ChatToolCall(
                                toolUseBlock.Id ?? string.Empty,
                                "function",
                                new ChatFunctionCall(
                                    toolUseBlock.Name ?? string.Empty,
                                    toolUseBlock.Input?.GetRawText() ?? "{}"),
                                streamEvent?.Index)
                        }));
                continue;
            }

            if (string.Equals(streamEvent?.Type, "content_block_delta", StringComparison.OrdinalIgnoreCase)
                && string.Equals(delta?.Type, "text_delta", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(text))
            {
                yield return new ChatStreamChunk(
                    request.Model,
                    text,
                    Done: false,
                    Delta: new ChatDelta(Content: text));
                continue;
            }

            if (string.Equals(streamEvent?.Type, "content_block_delta", StringComparison.OrdinalIgnoreCase)
                && string.Equals(delta?.Type, "input_json_delta", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(delta?.PartialJson))
            {
                yield return new ChatStreamChunk(
                    request.Model,
                    string.Empty,
                    Done: false,
                    Delta: new ChatDelta(
                        ToolCalls: new[]
                        {
                            new ChatToolCall(
                                string.Empty,
                                "function",
                                new ChatFunctionCall(string.Empty, delta.PartialJson),
                                streamEvent?.Index)
                        }));
                continue;
            }

            if (string.Equals(streamEvent?.Type, "message_delta", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(delta?.StopReason))
            {
                doneReason = NormalizeDoneReason(delta.StopReason);
                doneUsage = ToChatUsage(streamEvent?.Usage);
                continue;
            }

            if (string.Equals(streamEvent?.Type, "message_stop", StringComparison.OrdinalIgnoreCase))
            {
                completed = true;
                yield return new ChatStreamChunk(request.Model, string.Empty, Done: true, doneReason, Usage: doneUsage);
                yield break;
            }
        }

        if (!completed)
        {
            yield return new ChatStreamChunk(request.Model, string.Empty, Done: true, doneReason, Usage: doneUsage);
        }
    }

    private ProviderModel ToProviderModel(ClaudeModelOptions model)
    {
        var upstreamModel = model.UpstreamModel.Trim();
        var modelName = string.IsNullOrWhiteSpace(model.Name)
            ? $"{_providerName}/{upstreamModel}"
            : model.Name.Trim();
        var displayName = string.IsNullOrWhiteSpace(model.DisplayName)
            ? upstreamModel
            : model.DisplayName.Trim();

        return new ProviderModel(
            modelName,
            _providerName,
            upstreamModel,
            displayName,
            NormalizeAliases(model.Aliases));
    }

    private ClaudeMessagesRequest CreateMessagesRequest(ChatRequest request, bool stream)
    {
        if (string.IsNullOrWhiteSpace(request.UpstreamModel))
        {
            throw new ProviderException(ProviderErrorKind.InvalidRequest, "Claude 请求缺少上游模型名称。");
        }

        var systemMessages = request.Messages
            .Where(message => string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
            .Select(message => message.Content.Trim())
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .ToArray();

        // Anthropic Messages API 将 system 放在顶层；普通对话只允许 user / assistant 两类角色。
        var messages = request.Messages
            .Where(message => !string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
            .Select(ToClaudeChatMessage)
            .ToArray();

        if (messages.Length == 0)
        {
            throw new ProviderException(ProviderErrorKind.InvalidRequest, "Claude 请求至少需要一条 user 或 assistant 消息。");
        }

        var system = systemMessages.Length > 0 ? string.Join("\n\n", systemMessages) : null;
        return new ClaudeMessagesRequest(
            request.UpstreamModel,
            _maxTokens,
            messages,
            stream,
            system,
            ToClaudeTools(request.Tools),
            ToClaudeToolChoice(request.ToolChoice));
    }

    private HttpRequestMessage CreateJsonRequest(ClaudeMessagesRequest upstreamRequest)
    {
        var request = CreateRequest(HttpMethod.Post, "messages");
        var json = JsonSerializer.Serialize(
            upstreamRequest,
            ClaudeProviderJsonContext.Default.ClaudeMessagesRequest);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", _anthropicVersion);
        request.Headers.UserAgent.ParseAdd("VSCopilotSwitch/0.1");
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, completionOption, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderException(ProviderErrorKind.Timeout, "Claude 请求超时，请稍后重试。", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException(ProviderErrorKind.Unavailable, "Claude 网络请求失败，请检查 API 地址和网络连接。", ex);
        }
    }

    private async Task<ProviderException> CreateProviderExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var upstreamMessage = ExtractErrorMessage(rawContent);
        var message = BuildPublicErrorMessage(response.StatusCode, upstreamMessage);
        return new ProviderException(MapErrorKind(response.StatusCode), message);
    }

    private string BuildPublicErrorMessage(HttpStatusCode statusCode, string? upstreamMessage)
    {
        var sanitized = SanitizePublicMessage(upstreamMessage);
        if (!string.IsNullOrWhiteSpace(sanitized))
        {
            return statusCode switch
            {
                HttpStatusCode.BadRequest => $"Claude 拒绝了当前请求：{sanitized}",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"Claude 鉴权失败：{sanitized}",
                HttpStatusCode.TooManyRequests => $"Claude 额度或频率受限：{sanitized}",
                HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => $"Claude 请求超时：{sanitized}",
                _ => $"Claude 上游请求失败：{sanitized}"
            };
        }

        return statusCode switch
        {
            HttpStatusCode.BadRequest => "Claude 拒绝了当前请求，请检查模型名称和消息格式。",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Claude 鉴权失败，请检查 API Key 或平台权限。",
            HttpStatusCode.TooManyRequests => "Claude 额度或频率受限，请稍后重试。",
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => "Claude 请求超时，请稍后重试。",
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway => "Claude 服务暂不可用，请稍后重试。",
            _ => "Claude 上游请求失败，请稍后重试。"
        };
    }

    private static ProviderErrorKind MapErrorKind(HttpStatusCode statusCode)
        => statusCode switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.NotFound => ProviderErrorKind.InvalidRequest,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ProviderErrorKind.Unauthorized,
            HttpStatusCode.TooManyRequests => ProviderErrorKind.RateLimited,
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => ProviderErrorKind.Timeout,
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable => ProviderErrorKind.Unavailable,
            _ => ProviderErrorKind.UpstreamError
        };

    private static string? ExtractErrorMessage(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize(
                rawContent,
                ClaudeProviderJsonContext.Default.ClaudeErrorEnvelope);
            if (!string.IsNullOrWhiteSpace(payload?.Error?.Message))
            {
                return payload.Error.Message;
            }
        }
        catch (JsonException)
        {
            return rawContent;
        }

        return rawContent;
    }

    private void ThrowIfErrorPayload(ClaudeError? error)
    {
        if (error is null)
        {
            return;
        }

        var message = SanitizePublicMessage(error.Message);
        throw new ProviderException(
            ProviderErrorKind.UpstreamError,
            string.IsNullOrWhiteSpace(message)
                ? "Claude 返回了上游错误。"
                : $"Claude 返回了上游错误：{message}");
    }

    private string? SanitizePublicMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var sanitized = message.Trim();
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            sanitized = sanitized.Replace(_apiKey, "[已脱敏密钥]", StringComparison.Ordinal);
        }

        return sanitized.Length <= 300 ? sanitized : sanitized[..300] + "...";
    }

    private Uri BuildUri(string path)
    {
        var basePath = _baseUri.AbsolutePath.TrimEnd('/');
        var relativePath = basePath.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? path.TrimStart('/')
            : $"v1/{path.TrimStart('/')}";

        var builder = new UriBuilder(_baseUri)
        {
            Path = CombinePath(basePath, relativePath),
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    private static string CombinePath(string basePath, string relativePath)
    {
        var normalizedBase = basePath.TrimEnd('/');
        if (string.IsNullOrEmpty(normalizedBase))
        {
            return "/" + relativePath.TrimStart('/');
        }

        return normalizedBase + "/" + relativePath.TrimStart('/');
    }

    private static string NormalizeMessageRole(string role)
        => string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";

    private static ClaudeChatMessage ToClaudeChatMessage(ChatMessage message)
    {
        var role = string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase)
            ? "user"
            : NormalizeMessageRole(message.Role);
        var blocks = new List<ClaudeContentBlock>();

        if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
        {
            blocks.Add(new ClaudeContentBlock(
                "tool_result",
                ToolUseId: message.ToolCallId,
                Content: message.Content));
            return new ClaudeChatMessage(role, blocks);
        }

        if (!string.IsNullOrEmpty(message.Content))
        {
            blocks.Add(new ClaudeContentBlock("text", Text: message.Content));
        }

        if (message.ToolCalls is not null)
        {
            blocks.AddRange(message.ToolCalls.Select(toolCall => new ClaudeContentBlock(
                "tool_use",
                Id: toolCall.Id,
                Name: toolCall.Function.Name,
                Input: ParseToolInput(toolCall.Function.Arguments))));
        }

        if (blocks.Count == 0)
        {
            blocks.Add(new ClaudeContentBlock("text", Text: string.Empty));
        }

        return new ClaudeChatMessage(role, blocks);
    }

    private static IReadOnlyList<ClaudeTool>? ToClaudeTools(IReadOnlyList<ChatTool>? tools)
    {
        var mapped = tools?
            .Where(tool => tool.Function is not null && !string.IsNullOrWhiteSpace(tool.Function.Name))
            .Select(tool => new ClaudeTool(
                tool.Function.Name,
                tool.Function.Description,
                tool.Function.Parameters?.Clone() ?? CreateJsonElement("""{"type":"object","properties":{}}""")))
            .ToArray();

        return mapped is { Length: > 0 } ? mapped : null;
    }

    private static ClaudeToolChoice? ToClaudeToolChoice(ChatToolChoice? toolChoice)
    {
        if (toolChoice is null || string.IsNullOrWhiteSpace(toolChoice.Type))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(toolChoice.FunctionName))
        {
            return new ClaudeToolChoice("tool", toolChoice.FunctionName.Trim());
        }

        var type = toolChoice.Type.Trim().ToLowerInvariant() switch
        {
            "function" or "required" => "any",
            "auto" => "auto",
            "none" => "none",
            var value => value
        };
        return new ClaudeToolChoice(type);
    }

    private static string ExtractText(IReadOnlyList<ClaudeContentBlock>? blocks)
    {
        if (blocks is null || blocks.Count == 0)
        {
            return string.Empty;
        }

        return string.Concat(blocks
            .Where(block => string.Equals(block.Type, "text", StringComparison.OrdinalIgnoreCase))
            .Select(block => block.Text ?? string.Empty));
    }

    private static IReadOnlyList<ChatToolCall>? ToChatToolCalls(IReadOnlyList<ClaudeContentBlock>? blocks)
    {
        var mapped = blocks?
            .Where(block => string.Equals(block.Type, "tool_use", StringComparison.OrdinalIgnoreCase))
            .Select(block => new ChatToolCall(
                block.Id ?? string.Empty,
                "function",
                new ChatFunctionCall(block.Name ?? string.Empty, block.Input?.GetRawText() ?? "{}")))
            .ToArray();

        return mapped is { Length: > 0 } ? mapped : null;
    }

    private static ChatUsage? ToChatUsage(ClaudeUsage? usage)
    {
        if (usage is null)
        {
            return null;
        }

        var total = usage.InputTokens.HasValue || usage.OutputTokens.HasValue
            ? (usage.InputTokens ?? 0) + (usage.OutputTokens ?? 0)
            : (int?)null;
        return new ChatUsage(usage.InputTokens, usage.OutputTokens, total);
    }

    private static string NormalizeDoneReason(string? stopReason)
        => stopReason switch
        {
            null or "" => "stop",
            "end_turn" => "stop",
            "max_tokens" => "length",
            "tool_use" => "tool_calls",
            _ => stopReason
        };

    private static JsonElement ParseToolInput(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return CreateJsonElement("{}");
        }

        try
        {
            return CreateJsonElement(arguments);
        }
        catch (JsonException ex)
        {
            throw new ProviderException(ProviderErrorKind.InvalidRequest, "Claude 工具调用参数不是合法 JSON，无法转换为 Anthropic tool_use。", ex);
        }
    }

    private static JsonElement CreateJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<string>? NormalizeAliases(IReadOnlyList<string>? aliases)
    {
        var normalized = aliases?
            .Select(alias => alias.Trim())
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized is { Length: > 0 } ? normalized : null;
    }

    private static string NormalizeSseDataLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith(':') || trimmed.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? trimmed["data:".Length..].Trim()
            : trimmed;
    }

    private static Uri NormalizeBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Claude BaseUrl 必须是 http 或 https 绝对地址。", nameof(baseUrl));
        }

        return uri;
    }

    private static string NormalizeApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("Claude ApiKey 不能为空。", nameof(apiKey));
        }

        return apiKey.Trim();
    }

    private sealed record ClaudeModelListResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<ClaudeModelInfo>? Data);

    private sealed record ClaudeModelInfo(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("display_name")] string? DisplayName);

    private sealed record ClaudeMessagesRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("messages")] IReadOnlyList<ClaudeChatMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("system")] string? System,
        [property: JsonPropertyName("tools")] IReadOnlyList<ClaudeTool>? Tools = null,
        [property: JsonPropertyName("tool_choice")] ClaudeToolChoice? ToolChoice = null);

    private sealed record ClaudeChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] IReadOnlyList<ClaudeContentBlock> Content);

    private sealed record ClaudeTool(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("input_schema")] JsonElement InputSchema);

    private sealed record ClaudeToolChoice(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("name")] string? Name = null);

    private sealed record ClaudeMessageResponse(
        [property: JsonPropertyName("content")] IReadOnlyList<ClaudeContentBlock>? Content,
        [property: JsonPropertyName("stop_reason")] string? StopReason,
        [property: JsonPropertyName("usage")] ClaudeUsage? Usage,
        [property: JsonPropertyName("error")] ClaudeError? Error);

    private sealed record ClaudeContentBlock(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text = null,
        [property: JsonPropertyName("id")] string? Id = null,
        [property: JsonPropertyName("name")] string? Name = null,
        [property: JsonPropertyName("input")] JsonElement? Input = null,
        [property: JsonPropertyName("tool_use_id")] string? ToolUseId = null,
        [property: JsonPropertyName("content")] string? Content = null);

    private sealed record ClaudeStreamEvent(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("index")] int? Index,
        [property: JsonPropertyName("content_block")] ClaudeContentBlock? ContentBlock,
        [property: JsonPropertyName("delta")] ClaudeStreamDelta? Delta,
        [property: JsonPropertyName("usage")] ClaudeUsage? Usage,
        [property: JsonPropertyName("error")] ClaudeError? Error);

    private sealed record ClaudeStreamDelta(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("partial_json")] string? PartialJson,
        [property: JsonPropertyName("stop_reason")] string? StopReason);

    private sealed record ClaudeUsage(
        [property: JsonPropertyName("input_tokens")] int? InputTokens,
        [property: JsonPropertyName("output_tokens")] int? OutputTokens);

    private sealed record ClaudeErrorEnvelope(
        [property: JsonPropertyName("error")] ClaudeError? Error);

    private sealed record ClaudeError(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("message")] string? Message);

    [JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(ClaudeModelListResponse))]
    [JsonSerializable(typeof(ClaudeMessagesRequest))]
    [JsonSerializable(typeof(ClaudeMessageResponse))]
    [JsonSerializable(typeof(ClaudeStreamEvent))]
    [JsonSerializable(typeof(ClaudeErrorEnvelope))]
    private sealed partial class ClaudeProviderJsonContext : JsonSerializerContext;
}
