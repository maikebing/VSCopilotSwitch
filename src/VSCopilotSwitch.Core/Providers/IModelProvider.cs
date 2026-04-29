using System.Text.Json;

namespace VSCopilotSwitch.Core.Providers;

public sealed record ProviderModel(
    string Name,
    string Provider,
    string UpstreamModel,
    string DisplayName,
    IReadOnlyList<string>? Aliases = null,
    ProviderModelCapabilities? Capabilities = null);

public sealed record ProviderModelCapabilities(
    bool? SupportsTools = null,
    bool? SupportsVision = null,
    bool? SupportsThinking = null,
    bool? SupportsReasoning = null,
    int? ContextLength = null,
    string? Architecture = null);

public sealed record ChatMessage(
    string Role,
    string Content,
    IReadOnlyList<ChatToolCall>? ToolCalls = null,
    string? ToolCallId = null,
    string? Name = null,
    string? ReasoningContent = null);

public sealed record ChatTool(
    string Type,
    ChatFunctionTool Function);

public sealed record ChatFunctionTool(
    string Name,
    string? Description = null,
    JsonElement? Parameters = null);

public sealed record ChatToolChoice(
    string Type,
    string? FunctionName = null);

public sealed record ChatToolCall(
    string Id,
    string Type,
    ChatFunctionCall Function,
    int? Index = null);

public sealed record ChatFunctionCall(
    string Name,
    string Arguments);

public sealed record ChatUsage(
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens);

public sealed record ChatDelta(
    string? Role = null,
    string? Content = null,
    IReadOnlyList<ChatToolCall>? ToolCalls = null,
    string? ReasoningContent = null);

public sealed record ChatRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    bool Stream,
    string Provider,
    string UpstreamModel,
    IReadOnlyList<ChatTool>? Tools = null,
    ChatToolChoice? ToolChoice = null,
    string? ReasoningEffort = null,
    JsonElement? Thinking = null,
    JsonElement? Think = null)
{
    public ChatRequest(string model, IReadOnlyList<ChatMessage> messages, bool stream)
        : this(model, messages, stream, string.Empty, model)
    {
    }
}

public sealed record ChatResponse(
    string Model,
    string Content,
    string DoneReason = "stop",
    IReadOnlyList<ChatToolCall>? ToolCalls = null,
    ChatUsage? Usage = null,
    string? ReasoningContent = null)
{
    public string FinishReason => DoneReason;
}

public sealed record ChatStreamChunk(
    string Model,
    string Content,
    bool Done,
    string? DoneReason = null,
    ChatDelta? Delta = null,
    ChatUsage? Usage = null)
{
    public string? FinishReason => DoneReason;
}

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
