namespace VSCopilotSwitch.Core.Providers.OpenAiCompatible;

public sealed record OpenAiCompatibleModelOptions(
    string UpstreamModel,
    string? Name = null,
    string? DisplayName = null,
    IReadOnlyList<string>? Aliases = null);

public sealed record OpenAiCompatibleProviderOptions
{
    public string ProviderName { get; init; } = "openai-compatible";

    public string PublicProviderName { get; init; } = "OpenAI-compatible";

    public string BaseUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);

    public string? ApiPathPrefix { get; init; } = "v1";

    public IReadOnlyList<OpenAiCompatibleModelOptions> Models { get; init; } = Array.Empty<OpenAiCompatibleModelOptions>();

    public IReadOnlyDictionary<string, string> AdditionalHeaders { get; init; } = new Dictionary<string, string>();
}
