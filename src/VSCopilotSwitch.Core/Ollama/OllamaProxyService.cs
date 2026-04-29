using System.Runtime.CompilerServices;
using VSCopilotSwitch.Core.Providers;

namespace VSCopilotSwitch.Core.Ollama;

public interface IOllamaProxyService
{
    Task<OllamaTagsResponse> ListTagsAsync(CancellationToken cancellationToken = default);

    Task<OllamaShowResponse> ShowAsync(OllamaShowRequest request, CancellationToken cancellationToken = default);

    Task<OllamaChatResponse> ChatAsync(OllamaChatRequest request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<OllamaChatResponse> ChatStreamAsync(OllamaChatRequest request, CancellationToken cancellationToken = default);
}

public sealed class OllamaProxyService : IOllamaProxyService
{
    private const string VsCodeModelSuffix = "@vscs";
    private const int RemoteContextLength = 400_000;
    private readonly IReadOnlyList<IModelProvider> _providers;

    public OllamaProxyService(IEnumerable<IModelProvider> providers)
    {
        _providers = providers.ToArray();
    }

    public OllamaProxyService(IModelProvider provider)
        : this(new[] { provider })
    {
    }

    public async Task<OllamaTagsResponse> ListTagsAsync(CancellationToken cancellationToken = default)
    {
        var models = await ListAllModelsAsync(cancellationToken);
        return new OllamaTagsResponse(models.Select(model =>
        {
            var publicModelName = ToVsCodeModelName(model.Model.UpstreamModel);
            var capabilities = ResolveCapabilities(model.Model);
            return new OllamaModelInfo(
                publicModelName,
                publicModelName,
                DateTimeOffset.UtcNow,
                0,
                $"vscopilotswitch:{model.Model.Provider}:{model.Model.UpstreamModel}",
                BuildModelDetails(model.Model, capabilities),
                BuildCapabilities(capabilities),
                capabilities.ContextLength,
                BuildModelInfo(model.Model, capabilities),
                capabilities.SupportsTools,
                capabilities.SupportsVision,
                capabilities.SupportsThinking,
                capabilities.SupportsReasoning);
        }).ToArray());
    }

    public async Task<OllamaShowResponse> ShowAsync(OllamaShowRequest request, CancellationToken cancellationToken = default)
    {
        var route = await ResolveRouteAsync(request.Model, cancellationToken);
        var capabilities = ResolveCapabilities(route.Model);
        return new OllamaShowResponse(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            BuildModelDetails(route.Model, capabilities),
            BuildModelInfo(route.Model, capabilities),
            BuildCapabilities(capabilities),
            DateTimeOffset.UtcNow);
    }

    public async Task<OllamaChatResponse> ChatAsync(OllamaChatRequest request, CancellationToken cancellationToken = default)
    {
        var route = await ResolveRouteAsync(request.Model, cancellationToken);
        var response = await InvokeProviderAsync(route, BuildChatRequest(request, route, stream: false), cancellationToken);

        return new OllamaChatResponse(
            request.Model,
            DateTimeOffset.UtcNow,
            new OllamaChatMessage("assistant", response.Content, response.ToolCalls, ReasoningContent: response.ReasoningContent, Thinking: response.ReasoningContent),
            response.DoneReason,
            true,
            response.Usage);
    }

    public async IAsyncEnumerable<OllamaChatResponse> ChatStreamAsync(
        OllamaChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var route = await ResolveRouteAsync(request.Model, cancellationToken);
        var routedRequest = BuildChatRequest(request, route, stream: true);
        var completed = false;
        await foreach (var chunk in InvokeProviderStreamAsync(route, routedRequest, cancellationToken).WithCancellation(cancellationToken))
        {
            completed |= chunk.Done;
            var content = chunk.Delta?.Content ?? chunk.Content;
            var role = string.IsNullOrWhiteSpace(chunk.Delta?.Role) ? "assistant" : chunk.Delta!.Role!;
            yield return new OllamaChatResponse(
                request.Model,
                DateTimeOffset.UtcNow,
                new OllamaChatMessage(role, content, chunk.Delta?.ToolCalls, ReasoningContent: chunk.Delta?.ReasoningContent, Thinking: chunk.Delta?.ReasoningContent),
                chunk.Done ? chunk.DoneReason ?? "stop" : null,
                chunk.Done,
                chunk.Usage);
        }

        if (!completed)
        {
            yield return new OllamaChatResponse(
                request.Model,
                DateTimeOffset.UtcNow,
                new OllamaChatMessage("assistant", string.Empty),
                "stop",
                true);
        }
    }

    private async Task<IReadOnlyList<ModelRoute>> ListAllModelsAsync(CancellationToken cancellationToken)
    {
        if (_providers.Count == 0)
        {
            throw new OllamaProxyException(
                OllamaProxyErrorKind.ProviderUnavailable,
                "provider_unavailable",
                "当前没有可用的模型提供商。");
        }

        var routes = new List<ModelRoute>();
        foreach (var provider in _providers)
        {
            IReadOnlyList<ProviderModel> models;
            try
            {
                models = await provider.ListModelsAsync(cancellationToken);
            }
            catch (ProviderException ex)
            {
                throw MapProviderException(provider.Name, ex);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new OllamaProxyException(
                    OllamaProxyErrorKind.ProviderUnavailable,
                    "provider_unavailable",
                    $"提供商 `{provider.Name}` 模型列表读取失败。",
                    ex);
            }

            foreach (var model in models)
            {
                routes.Add(new ModelRoute(provider, model));
            }
        }

        return routes;
    }

    private async Task<ModelRoute> ResolveRouteAsync(string requestedModel, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestedModel))
        {
            throw new OllamaProxyException(
                OllamaProxyErrorKind.InvalidRequest,
                "invalid_request",
                "Ollama 请求必须包含 model。");
        }

        var routes = await ListAllModelsAsync(cancellationToken);
        var matches = routes
            .Where(route => MatchesModel(route.Model, requestedModel))
            .ToArray();

        if (matches.Length == 0)
        {
            throw new OllamaProxyException(
                OllamaProxyErrorKind.ModelNotFound,
                "model_not_found",
                $"模型 `{requestedModel}` 未配置 Provider 路由。");
        }

        var exactMatches = matches
            .Where(route => string.Equals(route.Model.Name, requestedModel, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exactMatches.Length == 1)
        {
            return exactMatches[0];
        }

        if (matches.Length > 1)
        {
            throw new OllamaProxyException(
                OllamaProxyErrorKind.AmbiguousModel,
                "ambiguous_model_alias",
                $"模型别名 `{requestedModel}` 同时匹配多个 Provider，请使用完整模型名。");
        }

        return matches[0];
    }

    private static bool MatchesModel(ProviderModel model, string requestedModel)
    {
        var normalizedRequestedModel = StripVsCodeModelSuffix(requestedModel);
        if (string.Equals(model.Name, requestedModel, StringComparison.OrdinalIgnoreCase)
            || string.Equals(model.UpstreamModel, normalizedRequestedModel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return model.Aliases?.Any(alias => string.Equals(alias, normalizedRequestedModel, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string StripVsCodeModelSuffix(string requestedModel)
    {
        var trimmed = requestedModel.Trim();
        return trimmed.EndsWith(VsCodeModelSuffix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^VsCodeModelSuffix.Length]
            : trimmed;
    }

    private static string ToVsCodeModelName(string upstreamModel)
    {
        var trimmed = StripVsCodeModelSuffix(upstreamModel);
        return $"{trimmed}{VsCodeModelSuffix}";
    }

    private static OllamaModelDetails BuildModelDetails(ProviderModel model, ModelCapabilities capabilities)
        => new(
            model.UpstreamModel,
            "provider-adapter",
            capabilities.Architecture,
            new[] { capabilities.Architecture, model.Provider },
            "remote",
            "remote");

    private static IReadOnlyList<string> BuildCapabilities(ModelCapabilities capabilities)
    {
        var values = new List<string> { "completion", "chat" };
        if (capabilities.SupportsTools)
        {
            values.Add("tools");
        }

        if (capabilities.SupportsVision)
        {
            values.Add("vision");
        }

        return values;
    }

    private static IReadOnlyDictionary<string, object> BuildModelInfo(ProviderModel model, ModelCapabilities capabilities)
        => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["general.architecture"] = capabilities.Architecture,
            ["general.basename"] = model.UpstreamModel,
            ["general.context_length"] = capabilities.ContextLength,
            [$"{capabilities.Architecture}.context_length"] = capabilities.ContextLength,
            ["vscopilotswitch.provider"] = model.Provider,
            ["vscopilotswitch.upstream_model"] = model.UpstreamModel,
            ["vscopilotswitch.context_length"] = capabilities.ContextLength
        };

    private static ModelCapabilities ResolveCapabilities(ProviderModel model)
    {
        var inferred = InferCapabilities(model);
        var explicitCapabilities = model.Capabilities;
        if (explicitCapabilities is null)
        {
            return inferred;
        }

        return inferred with
        {
            SupportsTools = explicitCapabilities.SupportsTools ?? inferred.SupportsTools,
            SupportsVision = explicitCapabilities.SupportsVision ?? inferred.SupportsVision,
            SupportsThinking = explicitCapabilities.SupportsThinking ?? inferred.SupportsThinking,
            SupportsReasoning = explicitCapabilities.SupportsReasoning ?? inferred.SupportsReasoning,
            ContextLength = explicitCapabilities.ContextLength is > 0 ? explicitCapabilities.ContextLength.Value : inferred.ContextLength,
            Architecture = string.IsNullOrWhiteSpace(explicitCapabilities.Architecture)
                ? inferred.Architecture
                : explicitCapabilities.Architecture.Trim()
        };
    }

    private static ModelCapabilities InferCapabilities(ProviderModel model)
    {
        var provider = NormalizeCapabilityToken(model.Provider);
        var modelName = NormalizeCapabilityToken(model.UpstreamModel);

        // 能力声明宁可保守，避免 Copilot 因虚报 tools / vision 而发送上游模型无法处理的输入。
        if (provider.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
            || modelName.Contains("deepseek", StringComparison.OrdinalIgnoreCase))
        {
            return ModelCapabilities.TextOnly;
        }

        if (provider == "openai" || IsOpenAiChatModel(modelName))
        {
            return ModelCapabilities.TextOnly with
            {
                SupportsTools = IsOpenAiToolModel(modelName),
                SupportsVision = IsOpenAiVisionModel(modelName)
            };
        }

        if (provider == "claude" || modelName.Contains("claude", StringComparison.OrdinalIgnoreCase))
        {
            return ModelCapabilities.TextOnly with
            {
                SupportsTools = true,
                SupportsVision = IsClaudeVisionModel(modelName)
            };
        }

        return ModelCapabilities.TextOnly;
    }

    private static string NormalizeCapabilityToken(string value)
        => value.Trim().ToLowerInvariant();

    private static bool IsOpenAiChatModel(string modelName)
        => modelName.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)
            || modelName.StartsWith("o1", StringComparison.OrdinalIgnoreCase)
            || modelName.StartsWith("o3", StringComparison.OrdinalIgnoreCase)
            || modelName.StartsWith("o4", StringComparison.OrdinalIgnoreCase)
            || modelName.StartsWith("chatgpt-", StringComparison.OrdinalIgnoreCase)
            || modelName.StartsWith("codex-", StringComparison.OrdinalIgnoreCase);

    private static bool IsOpenAiToolModel(string modelName)
        => IsOpenAiChatModel(modelName)
            && !ContainsAny(modelName, "embedding", "audio", "whisper", "tts", "dall-e", "image", "moderation", "transcribe");

    private static bool IsOpenAiVisionModel(string modelName)
        => ContainsAny(modelName, "gpt-4o", "gpt-4.1", "gpt-5", "vision", "omni");

    private static bool IsClaudeVisionModel(string modelName)
        => ContainsAny(modelName, "claude-3", "claude-4", "sonnet", "opus", "haiku");

    private static bool ContainsAny(string value, params string[] candidates)
        => candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static ChatRequest BuildChatRequest(OllamaChatRequest request, ModelRoute route, bool stream)
    {
        var messages = request.Messages?
            .Select(message => new ChatMessage(
                message.Role,
                message.Content,
                message.ToolCalls,
                message.ToolCallId,
                message.Name,
                ResolveMessageReasoningContent(message)))
            .ToArray() ?? Array.Empty<ChatMessage>();

        // Provider 只接收解析后的上游模型，避免把 Ollama 侧别名误传给私有协议层。
        return new ChatRequest(
            route.Model.Name,
            messages,
            stream,
            route.Model.Provider,
            route.Model.UpstreamModel,
            request.Tools,
            request.ToolChoice,
            ResolveReasoningEffort(request),
            request.Thinking,
            request.Think);
    }

    private static string? ResolveMessageReasoningContent(OllamaChatMessage message)
        => !string.IsNullOrWhiteSpace(message.ReasoningContent)
            ? message.ReasoningContent
            : string.IsNullOrWhiteSpace(message.Thinking) ? null : message.Thinking;

    private static string? ResolveReasoningEffort(OllamaChatRequest request)
        => !string.IsNullOrWhiteSpace(request.ReasoningEffort)
            ? request.ReasoningEffort
            : TryReadOllamaThinkLevel(request.Think);

    private static string? TryReadOllamaThinkLevel(System.Text.Json.JsonElement? think)
        => think is { ValueKind: System.Text.Json.JsonValueKind.String } value
            ? value.GetString()
            : null;

    private static async Task<ChatResponse> InvokeProviderAsync(ModelRoute route, ChatRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await route.Provider.ChatAsync(request, cancellationToken);
        }
        catch (ProviderException ex)
        {
            throw MapProviderException(route.Model.Provider, ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new OllamaProxyException(
                OllamaProxyErrorKind.UpstreamError,
                "upstream_error",
                $"提供商 `{route.Model.Provider}` 聊天请求失败。",
                ex);
        }
    }

    private static async IAsyncEnumerable<ChatStreamChunk> InvokeProviderStreamAsync(
        ModelRoute route,
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerator<ChatStreamChunk> enumerator;
        try
        {
            enumerator = route.Provider.ChatStreamAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch (ProviderException ex)
        {
            throw MapProviderException(route.Model.Provider, ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new OllamaProxyException(
                OllamaProxyErrorKind.UpstreamError,
                "upstream_error",
                $"提供商 `{route.Model.Provider}` 流式聊天请求失败。",
                ex);
        }

        await using (enumerator)
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (ProviderException ex)
                {
                    throw MapProviderException(route.Model.Provider, ex);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new OllamaProxyException(
                        OllamaProxyErrorKind.UpstreamError,
                        "upstream_error",
                        $"提供商 `{route.Model.Provider}` 流式聊天请求失败。",
                        ex);
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
    }

    private static OllamaProxyException MapProviderException(string providerName, ProviderException exception)
    {
        var (kind, code) = exception.Kind switch
        {
            ProviderErrorKind.InvalidRequest => (OllamaProxyErrorKind.InvalidRequest, "invalid_request"),
            ProviderErrorKind.Unauthorized => (OllamaProxyErrorKind.ProviderUnauthorized, "provider_unauthorized"),
            ProviderErrorKind.RateLimited => (OllamaProxyErrorKind.ProviderRateLimited, "provider_rate_limited"),
            ProviderErrorKind.Timeout => (OllamaProxyErrorKind.ProviderTimeout, "provider_timeout"),
            ProviderErrorKind.Unavailable => (OllamaProxyErrorKind.ProviderUnavailable, "provider_unavailable"),
            _ => (OllamaProxyErrorKind.UpstreamError, "upstream_error")
        };

        var publicMessage = string.IsNullOrWhiteSpace(exception.PublicMessage)
            ? $"提供商 `{providerName}` 请求失败。"
            : exception.PublicMessage;

        return new OllamaProxyException(kind, code, publicMessage, exception);
    }

    private sealed record ModelRoute(IModelProvider Provider, ProviderModel Model);

    private sealed record ModelCapabilities(
        bool SupportsTools,
        bool SupportsVision,
        bool SupportsThinking,
        bool SupportsReasoning,
        int ContextLength,
        string Architecture)
    {
        public static ModelCapabilities TextOnly { get; } = new(
            SupportsTools: false,
            SupportsVision: false,
            SupportsThinking: false,
            SupportsReasoning: false,
            ContextLength: RemoteContextLength,
            Architecture: "llama");
    }
}
