using VSCopilotSwitch.Core.Providers.Claude;
using VSCopilotSwitch.Core.Providers.DeepSeek;
using VSCopilotSwitch.Core.Providers.Moark;
using VSCopilotSwitch.Core.Providers.Nvidia;
using VSCopilotSwitch.Core.Providers.OpenAI;
using VSCopilotSwitch.Core.Providers.OpenAiCompatible;
using VSCopilotSwitch.Core.Providers.Sub2Api;

namespace VSCopilotSwitch.Core.Providers;

public sealed record ProviderAdapterConfig(
    string Id,
    string Name,
    string BaseUrl,
    string Model,
    string Vendor,
    string ApiKey);

public static class ProviderAdapterFactory
{
    public static IModelProvider Create(ProviderAdapterConfig config, TimeSpan timeout)
    {
        var providerName = NormalizeProviderName(config);
        var vendor = NormalizeVendor(config.Vendor);

        return vendor switch
        {
            "openai" => new OpenAiModelProvider(new HttpClient(), new OpenAiProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.BaseUrl,
                ApiKey = config.ApiKey,
                Timeout = timeout
            }),
            "deepseek" => new DeepSeekModelProvider(new HttpClient(), new DeepSeekProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.BaseUrl,
                ApiKey = config.ApiKey,
                Timeout = timeout
            }),
            "claude" => new ClaudeModelProvider(new HttpClient(), new ClaudeProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.BaseUrl,
                ApiKey = config.ApiKey,
                Timeout = timeout
            }),
            "nvidia" or "nvidia-nim" => new NvidiaNimModelProvider(new HttpClient(), new NvidiaNimProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.BaseUrl,
                ApiKey = config.ApiKey,
                Timeout = timeout
            }),
            "moark" => new MoarkModelProvider(new HttpClient(), new MoarkProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.BaseUrl,
                ApiKey = config.ApiKey,
                Timeout = timeout
            }),
            "sub2api" => new Sub2ApiModelProvider(new HttpClient(), new Sub2ApiProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.BaseUrl,
                ApiKey = config.ApiKey,
                Timeout = timeout
            }),
            _ => new OpenAiCompatibleModelProvider(new HttpClient(), new OpenAiCompatibleProviderOptions
            {
                ProviderName = providerName,
                PublicProviderName = config.Name,
                BaseUrl = config.BaseUrl,
                ApiKey = config.ApiKey,
                Timeout = timeout
            })
        };
    }

    private static string NormalizeProviderName(ProviderAdapterConfig config)
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
