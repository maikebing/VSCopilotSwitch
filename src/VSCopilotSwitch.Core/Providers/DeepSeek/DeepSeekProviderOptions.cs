namespace VSCopilotSwitch.Core.Providers.DeepSeek;

public sealed record DeepSeekModelOptions(
    string UpstreamModel,
    string? Name = null,
    string? DisplayName = null,
    IReadOnlyList<string>? Aliases = null);

public sealed record DeepSeekProviderOptions
{
    public string ProviderName { get; init; } = "deepseek";

    public string BaseUrl { get; init; } = "https://api.deepseek.com";

    public string ApiKey { get; init; } = string.Empty;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);

    public IReadOnlyList<DeepSeekModelOptions> Models { get; init; } = Array.Empty<DeepSeekModelOptions>();
}
