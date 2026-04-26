using VSCopilotSwitch.Core.Providers.OpenAiCompatible;

namespace VSCopilotSwitch.Core.Providers.OpenAI;

public sealed class OpenAiModelProvider : IModelProvider
{
    private readonly OpenAiCompatibleModelProvider _inner;

    public OpenAiModelProvider(HttpClient httpClient, OpenAiProviderOptions options)
    {
        _inner = new OpenAiCompatibleModelProvider(httpClient, new OpenAiCompatibleProviderOptions
        {
            ProviderName = string.IsNullOrWhiteSpace(options.ProviderName) ? "openai" : options.ProviderName,
            PublicProviderName = "OpenAI",
            BaseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "https://api.openai.com" : options.BaseUrl,
            ApiKey = options.ApiKey,
            Timeout = options.Timeout,
            Models = options.Models.Select(ToOpenAiCompatibleModel).ToArray(),
            AdditionalHeaders = BuildOpenAiHeaders(options)
        });
    }

    public string Name => _inner.Name;

    public Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CancellationToken cancellationToken = default)
        => _inner.ListModelsAsync(cancellationToken);

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
        => _inner.ChatAsync(request, cancellationToken);

    public IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(ChatRequest request, CancellationToken cancellationToken = default)
        => _inner.ChatStreamAsync(request, cancellationToken);

    private static OpenAiCompatibleModelOptions ToOpenAiCompatibleModel(OpenAiModelOptions model)
        => new(model.UpstreamModel, model.Name, model.DisplayName, model.Aliases);

    private static IReadOnlyDictionary<string, string> BuildOpenAiHeaders(OpenAiProviderOptions options)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(options.OrganizationId))
        {
            headers["OpenAI-Organization"] = options.OrganizationId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(options.ProjectId))
        {
            headers["OpenAI-Project"] = options.ProjectId.Trim();
        }

        return headers;
    }
}
