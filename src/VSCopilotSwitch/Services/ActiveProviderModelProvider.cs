using System.Runtime.CompilerServices;
using VSCopilotSwitch.Core.Providers;
using VSCopilotSwitch.Core.Providers.Claude;
using VSCopilotSwitch.Core.Providers.DeepSeek;
using VSCopilotSwitch.Core.Providers.Moark;
using VSCopilotSwitch.Core.Providers.Nvidia;
using VSCopilotSwitch.Core.Providers.OpenAI;
using VSCopilotSwitch.Core.Providers.OpenAiCompatible;
using VSCopilotSwitch.Core.Providers.Sub2Api;

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
    {
        var providerName = NormalizeProviderName(config);
        var vendor = NormalizeVendor(config.Vendor);

        return vendor switch
        {
            "openai" => new OpenAiModelProvider(new HttpClient(), new OpenAiProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.ApiUrl,
                ApiKey = config.ApiKey!,
                Timeout = RuntimeTimeout
            }),
            "deepseek" => new DeepSeekModelProvider(new HttpClient(), new DeepSeekProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.ApiUrl,
                ApiKey = config.ApiKey!,
                Timeout = RuntimeTimeout
            }),
            "claude" => new ClaudeModelProvider(new HttpClient(), new ClaudeProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.ApiUrl,
                ApiKey = config.ApiKey!,
                Timeout = RuntimeTimeout
            }),
            "nvidia" or "nvidia-nim" => new NvidiaNimModelProvider(new HttpClient(), new NvidiaNimProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.ApiUrl,
                ApiKey = config.ApiKey!,
                Timeout = RuntimeTimeout
            }),
            "moark" => new MoarkModelProvider(new HttpClient(), new MoarkProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.ApiUrl,
                ApiKey = config.ApiKey!,
                Timeout = RuntimeTimeout
            }),
            "sub2api" => new Sub2ApiModelProvider(new HttpClient(), new Sub2ApiProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.ApiUrl,
                ApiKey = config.ApiKey!,
                Timeout = RuntimeTimeout
            }),
            _ => new OpenAiCompatibleModelProvider(new HttpClient(), new OpenAiCompatibleProviderOptions
            {
                ProviderName = providerName,
                PublicProviderName = config.Name,
                BaseUrl = config.ApiUrl,
                ApiKey = config.ApiKey!,
                Timeout = RuntimeTimeout
            })
        };
    }

    private static string NormalizeProviderName(ProviderRuntimeConfig config)
    {
        var source = string.IsNullOrWhiteSpace(config.Id) ? config.Name : config.Id;
        var normalized = new string(source.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());
        normalized = string.Join('-', normalized.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(normalized) ? "active-provider" : normalized;
    }

    private static string NormalizeVendor(string vendor)
        => string.IsNullOrWhiteSpace(vendor) ? "openai-compatible" : vendor.Trim().ToLowerInvariant();
}
