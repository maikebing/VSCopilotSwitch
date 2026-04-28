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
        var config = await _providerConfigService.GetActiveRuntimeConfigAsync(cancellationToken);
        if (!HasUsableRuntimeConfig(config))
        {
            return await _fallbackProvider.ListModelsAsync(cancellationToken);
        }

        var provider = CreateProvider(config!);
        try
        {
            var models = await provider.ListModelsAsync(cancellationToken);
            return models.Count > 0 ? models : CreateConfiguredModelFallback(config!);
        }
        catch (ProviderException ex) when (CanUseConfiguredModelFallback(ex))
        {
            return CreateConfiguredModelFallback(config!);
        }
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
        if (!HasUsableRuntimeConfig(config))
        {
            return _fallbackProvider;
        }

        return CreateProvider(config!);
    }

    private static bool HasUsableRuntimeConfig(ProviderRuntimeConfig? config)
        => config is not null
            && !string.IsNullOrWhiteSpace(config.ApiKey)
            && !string.IsNullOrWhiteSpace(config.ApiUrl)
            && !string.IsNullOrWhiteSpace(config.Model);

    private static IModelProvider CreateProvider(ProviderRuntimeConfig config)
        => ProviderAdapterFactory.Create(new ProviderAdapterConfig(
            config.Id,
            config.Name,
            config.ApiUrl,
            config.Model,
            config.Vendor,
            config.ApiKey!), RuntimeTimeout);

    private static bool CanUseConfiguredModelFallback(ProviderException exception)
        => exception.Kind is ProviderErrorKind.Timeout
            or ProviderErrorKind.Unavailable
            or ProviderErrorKind.UpstreamError
            or ProviderErrorKind.InvalidRequest;

    private static IReadOnlyList<ProviderModel> CreateConfiguredModelFallback(ProviderRuntimeConfig config)
    {
        var model = config.Model.Trim();
        return new[]
        {
            new ProviderModel(
                $"{config.Id}/{model}",
                config.Id,
                model,
                model,
                new[] { model })
        };
    }
}
