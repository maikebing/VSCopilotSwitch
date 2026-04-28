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
    private const string VsCodeModelSuffix = "@vscc";
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
            return new OllamaModelInfo(
                publicModelName,
                publicModelName,
                DateTimeOffset.UtcNow,
                0,
                $"vscopilotswitch:{model.Model.Provider}:{model.Model.UpstreamModel}",
                new OllamaModelDetails(
                    model.Model.UpstreamModel,
                    "provider-adapter",
                    model.Model.Provider,
                    new[] { model.Model.Provider },
                    "remote",
                    "remote"));
        }).ToArray());
    }

    public async Task<OllamaShowResponse> ShowAsync(OllamaShowRequest request, CancellationToken cancellationToken = default)
    {
        var route = await ResolveRouteAsync(request.Model, cancellationToken);
        var details = new OllamaModelDetails(
            route.Model.UpstreamModel,
            "provider-adapter",
            route.Model.Provider,
            new[] { route.Model.Provider },
            "remote",
            "remote");

        return new OllamaShowResponse(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            details,
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["general.architecture"] = "llama",
                ["general.context_length"] = RemoteContextLength,
                ["llama.context_length"] = RemoteContextLength,
                ["vscopilotswitch.provider"] = route.Model.Provider,
                ["vscopilotswitch.upstream_model"] = route.Model.UpstreamModel,
                ["vscopilotswitch.context_length"] = RemoteContextLength
            },
            new[] { "completion", "tools", "vision" },
            DateTimeOffset.UtcNow);
    }

    public async Task<OllamaChatResponse> ChatAsync(OllamaChatRequest request, CancellationToken cancellationToken = default)
    {
        var route = await ResolveRouteAsync(request.Model, cancellationToken);
        var response = await InvokeProviderAsync(route, BuildChatRequest(request, route, stream: false), cancellationToken);

        return new OllamaChatResponse(
            request.Model,
            DateTimeOffset.UtcNow,
            new OllamaChatMessage("assistant", response.Content),
            response.DoneReason,
            true);
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
            yield return new OllamaChatResponse(
                request.Model,
                DateTimeOffset.UtcNow,
                new OllamaChatMessage("assistant", chunk.Content),
                chunk.Done ? chunk.DoneReason ?? "stop" : null,
                chunk.Done);
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
        var trimmed = upstreamModel.Trim();
        return trimmed.EndsWith(VsCodeModelSuffix, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}{VsCodeModelSuffix}";
    }

    private static ChatRequest BuildChatRequest(OllamaChatRequest request, ModelRoute route, bool stream)
    {
        var messages = request.Messages?
            .Select(message => new ChatMessage(message.Role, message.Content))
            .ToArray() ?? Array.Empty<ChatMessage>();

        // Provider 只接收解析后的上游模型，避免把 Ollama 侧别名误传给私有协议层。
        return new ChatRequest(route.Model.Name, messages, stream, route.Model.Provider, route.Model.UpstreamModel);
    }

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
}
