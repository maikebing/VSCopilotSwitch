using VSCopilotSwitch.Core.Providers.OpenAiCompatible;

namespace VSCopilotSwitch.Core.Providers.Moark;

public sealed class MoarkModelProvider : IModelProvider
{
    private readonly OpenAiCompatibleModelProvider _inner;

    public MoarkModelProvider(HttpClient httpClient, MoarkProviderOptions options)
    {
        _inner = new OpenAiCompatibleModelProvider(httpClient, new OpenAiCompatibleProviderOptions
        {
            ProviderName = string.IsNullOrWhiteSpace(options.ProviderName) ? "moark" : options.ProviderName,
            PublicProviderName = "MoArk",
            BaseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "https://moark.ai/v1" : options.BaseUrl,
            ApiKey = options.ApiKey,
            Timeout = options.Timeout,
            Models = options.Models.Select(ToOpenAiCompatibleModel).ToArray()
        });
    }

    public string Name => _inner.Name;

    public Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CancellationToken cancellationToken = default)
        => _inner.ListModelsAsync(cancellationToken);

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
        => _inner.ChatAsync(request, cancellationToken);

    public IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(ChatRequest request, CancellationToken cancellationToken = default)
        => _inner.ChatStreamAsync(request, cancellationToken);

    private static OpenAiCompatibleModelOptions ToOpenAiCompatibleModel(MoarkModelOptions model)
        => new(model.UpstreamModel, model.Name, model.DisplayName, model.Aliases);
}
