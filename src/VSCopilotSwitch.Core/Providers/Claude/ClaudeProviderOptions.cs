namespace VSCopilotSwitch.Core.Providers.Claude;

public sealed record ClaudeModelOptions(
    string UpstreamModel,
    string? Name = null,
    string? DisplayName = null,
    IReadOnlyList<string>? Aliases = null);

public sealed record ClaudeProviderOptions
{
    public string ProviderName { get; init; } = "claude";

    public string BaseUrl { get; init; } = "https://api.anthropic.com";

    public string ApiKey { get; init; } = string.Empty;

    public string AnthropicVersion { get; init; } = "2023-06-01";

    public int MaxTokens { get; init; } = 4096;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);

    public IReadOnlyList<ClaudeModelOptions> Models { get; init; } = Array.Empty<ClaudeModelOptions>();
}
