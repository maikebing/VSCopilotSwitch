namespace VSCopilotSwitch.Core.Providers.Sub2Api;

public sealed record Sub2ApiModelOptions(
    string UpstreamModel,
    string? Name = null,
    string? DisplayName = null,
    IReadOnlyList<string>? Aliases = null);

public sealed record Sub2ApiProviderOptions
{
    public string ProviderName { get; init; } = "sub2api";

    public string BaseUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);

    public IReadOnlyList<Sub2ApiModelOptions> Models { get; init; } = Array.Empty<Sub2ApiModelOptions>();
}
