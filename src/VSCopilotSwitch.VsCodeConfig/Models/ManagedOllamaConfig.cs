namespace VSCopilotSwitch.VsCodeConfig.Models;

public sealed record ManagedOllamaConfig(
    string BaseUrl,
    IReadOnlyList<ManagedOllamaModel> Models)
{
    public static ManagedOllamaConfig Default => new(
        "http://127.0.0.1:11434",
        new[]
        {
            new ManagedOllamaModel("vscopilotswitch/default", "VSCopilotSwitch Default", "vscopilotswitch/default")
        });
}

public sealed record ManagedOllamaModel(
    string Id,
    string DisplayName,
    string ProviderModelId);
