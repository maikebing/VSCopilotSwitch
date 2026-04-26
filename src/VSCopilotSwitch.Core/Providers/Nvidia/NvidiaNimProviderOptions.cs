namespace VSCopilotSwitch.Core.Providers.Nvidia;

public sealed record NvidiaNimModelOptions(
    string UpstreamModel,
    string? Name = null,
    string? DisplayName = null,
    IReadOnlyList<string>? Aliases = null);

public sealed record NvidiaNimProviderOptions
{
    public string ProviderName { get; init; } = "nvidia-nim";

    public string BaseUrl { get; init; } = "https://integrate.api.nvidia.com";

    public string ApiKey { get; init; } = string.Empty;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);

    public IReadOnlyList<NvidiaNimModelOptions> Models { get; init; } = Array.Empty<NvidiaNimModelOptions>();
}
