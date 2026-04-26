namespace VSCopilotSwitch.Core.Providers;

public sealed record ProviderModel(
    string Name,
    string Provider,
    string UpstreamModel,
    string DisplayName,
    IReadOnlyList<string>? Aliases = null);

public sealed record ChatMessage(string Role, string Content);

public sealed record ChatRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    bool Stream,
    string Provider,
    string UpstreamModel)
{
    public ChatRequest(string model, IReadOnlyList<ChatMessage> messages, bool stream)
        : this(model, messages, stream, string.Empty, model)
    {
    }
}

public sealed record ChatResponse(
    string Model,
    string Content,
    string DoneReason = "stop");

public sealed record ChatStreamChunk(
    string Model,
    string Content,
    bool Done,
    string? DoneReason = null);

public enum ProviderErrorKind
{
    InvalidRequest,
    Unauthorized,
    RateLimited,
    Timeout,
    Unavailable,
    UpstreamError
}

public sealed class ProviderException : Exception
{
    public ProviderException(ProviderErrorKind kind, string publicMessage, Exception? innerException = null)
        : base(publicMessage, innerException)
    {
        Kind = kind;
        PublicMessage = publicMessage;
    }

    public ProviderErrorKind Kind { get; }

    public string PublicMessage { get; }
}

public interface IModelProvider
{
    string Name { get; }

    Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CancellationToken cancellationToken = default);

    Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
