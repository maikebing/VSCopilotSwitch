using VSCopilotSwitch.Core.Providers.OpenAiCompatible;

namespace VSCopilotSwitch.Core.Providers.Nvidia;

public sealed class NvidiaNimModelProvider : IModelProvider
{
    private readonly OpenAiCompatibleModelProvider _inner;

    public NvidiaNimModelProvider(HttpClient httpClient, NvidiaNimProviderOptions options)
    {
        _inner = new OpenAiCompatibleModelProvider(httpClient, new OpenAiCompatibleProviderOptions
        {
            ProviderName = string.IsNullOrWhiteSpace(options.ProviderName) ? "nvidia-nim" : options.ProviderName,
            PublicProviderName = "NVIDIA NIM",
            BaseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "https://integrate.api.nvidia.com" : options.BaseUrl,
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

    private static OpenAiCompatibleModelOptions ToOpenAiCompatibleModel(NvidiaNimModelOptions model)
        => new(model.UpstreamModel, model.Name, model.DisplayName, model.Aliases);
}
