namespace VSCopilotSwitch.Core.Providers.Moark;

public sealed record MoarkModelOptions(
    string UpstreamModel,
    string? Name = null,
    string? DisplayName = null,
    IReadOnlyList<string>? Aliases = null);

public sealed record MoarkProviderOptions
{
    public string ProviderName { get; init; } = "moark";

    public string BaseUrl { get; init; } = "https://moark.ai/v1";

    public string ApiKey { get; init; } = string.Empty;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);

    public IReadOnlyList<MoarkModelOptions> Models { get; init; } = Array.Empty<MoarkModelOptions>();
}
