namespace VSCopilotSwitch.Core.Providers;

public sealed record ProviderModel(
    string Name,
    string Provider,
    string UpstreamModel,
    string DisplayName);

public sealed record ChatMessage(string Role, string Content);

public sealed record ChatRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    bool Stream);

public sealed record ChatResponse(
    string Model,
    string Content,
    string DoneReason = "stop");

public interface IModelProvider
{
    string Name { get; }

    Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CancellationToken cancellationToken = default);

    Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
