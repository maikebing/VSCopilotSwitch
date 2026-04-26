namespace VSCopilotSwitch.Core.Providers.OpenAI;

public sealed record OpenAiModelOptions(
    string UpstreamModel,
    string? Name = null,
    string? DisplayName = null,
    IReadOnlyList<string>? Aliases = null);

public sealed record OpenAiProviderOptions
{
    public string ProviderName { get; init; } = "openai";

    public string BaseUrl { get; init; } = "https://api.openai.com";

    public string ApiKey { get; init; } = string.Empty;

    public string? OrganizationId { get; init; }

    public string? ProjectId { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);

    public IReadOnlyList<OpenAiModelOptions> Models { get; init; } = Array.Empty<OpenAiModelOptions>();
}
