using System.Text.Json.Serialization;
using VSCopilotSwitch.Core.Providers;

namespace VSCopilotSwitch.Core.Ollama;

public sealed record OllamaTagsResponse(
    [property: JsonPropertyName("models")] IReadOnlyList<OllamaModelInfo> Models);

public sealed record OllamaModelInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("modified_at")] DateTimeOffset ModifiedAt,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("digest")] string Digest,
    [property: JsonPropertyName("details")] OllamaModelDetails Details,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string> Capabilities,
    [property: JsonPropertyName("context_length")] int ContextLength,
    [property: JsonPropertyName("model_info")] IReadOnlyDictionary<string, object> ModelInfo,
    [property: JsonPropertyName("supports_tool_calling")] bool SupportsToolCalling,
    [property: JsonPropertyName("supports_vision")] bool SupportsVision,
    [property: JsonPropertyName("supports_thinking")] bool SupportsThinking,
    [property: JsonPropertyName("supports_reasoning")] bool SupportsReasoning);

public sealed record OllamaModelDetails(
    [property: JsonPropertyName("parent_model")] string ParentModel,
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("family")] string Family,
    [property: JsonPropertyName("families")] IReadOnlyList<string> Families,
    [property: JsonPropertyName("parameter_size")] string ParameterSize,
    [property: JsonPropertyName("quantization_level")] string QuantizationLevel);

public sealed record OllamaChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<OllamaChatMessage>? Messages,
    [property: JsonPropertyName("stream")] bool? Stream,
    [property: JsonPropertyName("tools")] IReadOnlyList<ChatTool>? Tools = null,
    [property: JsonPropertyName("tool_choice")] ChatToolChoice? ToolChoice = null,
    [property: JsonPropertyName("reasoning_effort")] string? ReasoningEffort = null,
    [property: JsonPropertyName("thinking")] System.Text.Json.JsonElement? Thinking = null,
    [property: JsonPropertyName("think")] System.Text.Json.JsonElement? Think = null);

public sealed record OllamaShowRequest(
    [property: JsonPropertyName("model")] string Model);

public sealed record OllamaShowResponse(
    [property: JsonPropertyName("license")] string License,
    [property: JsonPropertyName("modelfile")] string Modelfile,
    [property: JsonPropertyName("parameters")] string Parameters,
    [property: JsonPropertyName("template")] string Template,
    [property: JsonPropertyName("details")] OllamaModelDetails Details,
    [property: JsonPropertyName("model_info")] IReadOnlyDictionary<string, object> ModelInfo,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string> Capabilities,
    [property: JsonPropertyName("modified_at")] DateTimeOffset ModifiedAt);

public sealed record OllamaChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("tool_calls")] IReadOnlyList<ChatToolCall>? ToolCalls = null,
    [property: JsonPropertyName("tool_call_id")] string? ToolCallId = null,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("reasoning_content")] string? ReasoningContent = null,
    [property: JsonPropertyName("thinking")] string? Thinking = null);

public sealed record OllamaChatResponse(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("message")] OllamaChatMessage Message,
    [property: JsonPropertyName("done_reason")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DoneReason,
    [property: JsonPropertyName("done")] bool Done,
    [property: JsonPropertyName("usage")] ChatUsage? Usage = null);

public sealed record OllamaErrorResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("code")] string Code);
