using VSCopilotSwitch.Core.Providers.OpenAiCompatible;

namespace VSCopilotSwitch.Core.Providers.DeepSeek;

public sealed class DeepSeekModelProvider : IModelProvider
{
    private readonly OpenAiCompatibleModelProvider _inner;

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
        => _inner.ChatAsync(request, cancellationToken);

    public IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(ChatRequest request, CancellationToken cancellationToken = default)
        => _inner.ChatStreamAsync(request, cancellationToken);

    private static OpenAiCompatibleModelOptions ToOpenAiCompatibleModel(DeepSeekModelOptions model)
        => new(model.UpstreamModel, model.Name, model.DisplayName, model.Aliases);
}
