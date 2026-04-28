using System.Runtime.CompilerServices;
using VSCopilotSwitch.Core.Providers;

namespace VSCopilotSwitch.Services;

public sealed class ActiveProviderModelProvider : IModelProvider
{
    private static readonly TimeSpan RuntimeTimeout = TimeSpan.FromSeconds(120);

    private readonly IProviderConfigService _providerConfigService;
    private readonly InMemoryModelProvider _fallbackProvider = new();

    public ActiveProviderModelProvider(IProviderConfigService providerConfigService)
    {
        _providerConfigService = providerConfigService;
    }

    public string Name => "active-provider";

    public async Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var provider = await CreateActiveProviderAsync(cancellationToken);
        return await provider.ListModelsAsync(cancellationToken);
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var provider = await CreateActiveProviderAsync(cancellationToken);
        return await provider.ChatAsync(request, cancellationToken);
    }

    public async IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var provider = await CreateActiveProviderAsync(cancellationToken);
        await foreach (var chunk in provider.ChatStreamAsync(request, cancellationToken).WithCancellation(cancellationToken))
        {
            yield return chunk;
        }
    }

    private async Task<IModelProvider> CreateActiveProviderAsync(CancellationToken cancellationToken)
    {
        var config = await _providerConfigService.GetActiveRuntimeConfigAsync(cancellationToken);
        // 没有真实密钥时只启用本地占位 Provider，避免把半配置供应商误当成可用上游。
        if (config is null
            || string.IsNullOrWhiteSpace(config.ApiKey)
            || string.IsNullOrWhiteSpace(config.ApiUrl)
            || string.IsNullOrWhiteSpace(config.Model))
        {
            return _fallbackProvider;
        }

        return CreateProvider(config);
    }

    private static IModelProvider CreateProvider(ProviderRuntimeConfig config)
        => ProviderAdapterFactory.Create(new ProviderAdapterConfig(
            config.Id,
            config.Name,
            config.ApiUrl,
            config.Model,
            config.Vendor,
            config.ApiKey!), RuntimeTimeout);
}
