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
    string ApiKey,
    ProviderTimeoutPolicy? TimeoutPolicy = null);

public sealed record ProviderTimeoutPolicy(
    TimeSpan? ConnectionTimeout,
    TimeSpan? FirstTokenTimeout,
    TimeSpan? TotalRequestTimeout)
{
    public static ProviderTimeoutPolicy Default { get; } = new(
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(45),
        TimeSpan.FromSeconds(120));

    public TimeSpan EffectiveConnectionTimeout => Normalize(ConnectionTimeout, Default.ConnectionTimeout!.Value);

    public TimeSpan EffectiveFirstTokenTimeout => Normalize(FirstTokenTimeout, Default.FirstTokenTimeout!.Value);

    public TimeSpan EffectiveTotalRequestTimeout => Normalize(TotalRequestTimeout, Default.TotalRequestTimeout!.Value);

    public TimeSpan HttpClientTimeout => Max(EffectiveConnectionTimeout, EffectiveFirstTokenTimeout, EffectiveTotalRequestTimeout);

    public static ProviderTimeoutPolicy FromTotalTimeout(TimeSpan totalTimeout)
        => Default with { TotalRequestTimeout = totalTimeout };

    private static TimeSpan Normalize(TimeSpan? value, TimeSpan fallback)
        => value is { } timeout && timeout > TimeSpan.Zero ? timeout : fallback;

    private static TimeSpan Max(params TimeSpan[] values)
        => values.Aggregate(TimeSpan.Zero, (current, next) => next > current ? next : current);
}

public static class ProviderAdapterFactory
{
    public static IModelProvider Create(ProviderAdapterConfig config, TimeSpan timeout)
        => Create(config with { TimeoutPolicy = config.TimeoutPolicy ?? ProviderTimeoutPolicy.FromTotalTimeout(timeout) });

    public static IModelProvider Create(ProviderAdapterConfig config)
    {
        var providerName = NormalizeProviderName(config);
        var vendor = NormalizeVendor(config.Vendor);
        var timeoutPolicy = config.TimeoutPolicy ?? ProviderTimeoutPolicy.Default;
        var httpClientTimeout = timeoutPolicy.HttpClientTimeout;

        return vendor switch
        {
            "openai" => new OpenAiModelProvider(new HttpClient(), new OpenAiProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.BaseUrl,
                ApiKey = config.ApiKey,
                Timeout = httpClientTimeout
            }),
            "deepseek" => new DeepSeekModelProvider(new HttpClient(), new DeepSeekProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.BaseUrl,
                ApiKey = config.ApiKey,
                Timeout = httpClientTimeout
            }),
            "claude" => new ClaudeModelProvider(new HttpClient(), new ClaudeProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.BaseUrl,
                ApiKey = config.ApiKey,
                Timeout = httpClientTimeout
            }),
            "nvidia" or "nvidia-nim" => new NvidiaNimModelProvider(new HttpClient(), new NvidiaNimProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.BaseUrl,
                ApiKey = config.ApiKey,
                Timeout = httpClientTimeout
            }),
            "moark" => new MoarkModelProvider(new HttpClient(), new MoarkProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.BaseUrl,
                ApiKey = config.ApiKey,
                Timeout = httpClientTimeout
            }),
            "sub2api" => new Sub2ApiModelProvider(new HttpClient(), new Sub2ApiProviderOptions
            {
                ProviderName = providerName,
                BaseUrl = config.BaseUrl,
                ApiKey = config.ApiKey,
                Timeout = httpClientTimeout
            }),
            _ => new OpenAiCompatibleModelProvider(new HttpClient(), new OpenAiCompatibleProviderOptions
            {
                ProviderName = providerName,
                PublicProviderName = config.Name,
                BaseUrl = config.BaseUrl,
                ApiKey = config.ApiKey,
                Timeout = httpClientTimeout
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
