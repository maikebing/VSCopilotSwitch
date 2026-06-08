using System.Text.Json;
using System.Text.Json.Serialization;
using VSCopilotSwitch.Core.Ollama;
using VSCopilotSwitch.Core.Providers;
using VSCopilotSwitch.Services;
using VSCopilotSwitch.VsCodeConfig.Models;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(AboutInfoResponse))]
[JsonSerializable(typeof(ErrorMessageResponse))]
[JsonSerializable(typeof(PortStatusResponse))]
[JsonSerializable(typeof(OllamaVersionResponse))]
[JsonSerializable(typeof(ApplyVsCodeOllamaConfigRequest))]
[JsonSerializable(typeof(VsCodeUserDirectoryRequest))]
[JsonSerializable(typeof(RemoveVsCodeOllamaConfigRequest))]
[JsonSerializable(typeof(ListVsCodeConfigBackupsRequest))]
[JsonSerializable(typeof(RestoreVsCodeConfigBackupRequest))]
[JsonSerializable(typeof(OllamaTagsResponse))]
[JsonSerializable(typeof(OllamaModelInfo))]
[JsonSerializable(typeof(OllamaModelInfo[]))]
[JsonSerializable(typeof(OllamaModelDetails))]
[JsonSerializable(typeof(OllamaChatRequest))]
[JsonSerializable(typeof(OllamaShowRequest))]
[JsonSerializable(typeof(OllamaShowResponse))]
[JsonSerializable(typeof(OllamaChatMessage))]
[JsonSerializable(typeof(OllamaChatResponse))]
[JsonSerializable(typeof(OllamaErrorResponse))]
[JsonSerializable(typeof(OpenAiModelListResponse))]
[JsonSerializable(typeof(OpenAiModelInfo))]
[JsonSerializable(typeof(OpenAiModelInfo[]))]
[JsonSerializable(typeof(Vs2026ByomInfoResponse))]
[JsonSerializable(typeof(OpenAiChatCompletionRequest))]
[JsonSerializable(typeof(OpenAiChatRequestMessage))]
[JsonSerializable(typeof(OpenAiChatCompletionResponse))]
[JsonSerializable(typeof(OpenAiChatCompletionChoice))]
[JsonSerializable(typeof(OpenAiChatCompletionMessage))]
[JsonSerializable(typeof(OpenAiChatCompletionChunk))]
[JsonSerializable(typeof(OpenAiChatCompletionChunkChoice))]
[JsonSerializable(typeof(OpenAiChatCompletionDelta))]
[JsonSerializable(typeof(OpenAiTool))]
[JsonSerializable(typeof(OpenAiToolFunction))]
[JsonSerializable(typeof(OpenAiToolCall))]
[JsonSerializable(typeof(OpenAiFunctionCall))]
[JsonSerializable(typeof(OpenAiUsage))]
[JsonSerializable(typeof(OpenAiErrorResponse))]
[JsonSerializable(typeof(OpenAiErrorBody))]
[JsonSerializable(typeof(ProviderModel))]
[JsonSerializable(typeof(ProviderModel[]))]
[JsonSerializable(typeof(ProviderModelCapabilities))]
[JsonSerializable(typeof(ChatMessage))]
[JsonSerializable(typeof(ChatTool))]
[JsonSerializable(typeof(ChatFunctionTool))]
[JsonSerializable(typeof(ChatToolChoice))]
[JsonSerializable(typeof(ChatToolCall))]
[JsonSerializable(typeof(ChatFunctionCall))]
[JsonSerializable(typeof(ChatUsage))]
[JsonSerializable(typeof(ChatDelta))]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatResponse))]
[JsonSerializable(typeof(ChatStreamChunk))]
[JsonSerializable(typeof(ProviderConnectionTestStep))]
[JsonSerializable(typeof(ProviderConnectionTestResult))]
[JsonSerializable(typeof(ModelComparisonRequest))]
[JsonSerializable(typeof(ModelComparisonResult))]
[JsonSerializable(typeof(ModelComparisonStep))]
[JsonSerializable(typeof(IReadOnlyList<ModelComparisonResult>))]
[JsonSerializable(typeof(ProviderConfigView))]
[JsonSerializable(typeof(ProviderConfigView[]))]
[JsonSerializable(typeof(ProviderTimeoutConfig))]
[JsonSerializable(typeof(ProviderTimeoutView))]
[JsonSerializable(typeof(ProviderConfigExportDocument))]
[JsonSerializable(typeof(ProviderConfigExportItem))]
[JsonSerializable(typeof(ProviderConfigExportItem[]))]
[JsonSerializable(typeof(ProviderRuntimeConfig))]
[JsonSerializable(typeof(TestProviderConnectionRequest))]
[JsonSerializable(typeof(SaveProviderConfigRequest))]
[JsonSerializable(typeof(ReorderProvidersRequest))]
[JsonSerializable(typeof(RequestAnalyticsSnapshot))]
[JsonSerializable(typeof(RequestAnalyticsSummary))]
[JsonSerializable(typeof(ListenerStatus))]
[JsonSerializable(typeof(RequestLogEntry))]
[JsonSerializable(typeof(RequestLogEntry[]))]
[JsonSerializable(typeof(UsagePricingOptions))]
[JsonSerializable(typeof(UsagePriceRule))]
[JsonSerializable(typeof(UsagePriceRule[]))]
[JsonSerializable(typeof(UsageCostEstimate))]
[JsonSerializable(typeof(CopilotCompatibilityProbeResult))]
[JsonSerializable(typeof(CopilotCompatibilityProbeStep))]
[JsonSerializable(typeof(CopilotCompatibilityProbeStep[]))]
[JsonSerializable(typeof(UpdateCheckResult))]
[JsonSerializable(typeof(UpdateSourceCheckResult))]
[JsonSerializable(typeof(UpdateSourceCheckResult[]))]
[JsonSerializable(typeof(UpdateReleaseInfo))]
[JsonSerializable(typeof(UpdateAssetInfo))]
[JsonSerializable(typeof(UpdateDownloadRequest))]
[JsonSerializable(typeof(UpdateDownloadResult))]
[JsonSerializable(typeof(VsCodeUserDirectory))]
[JsonSerializable(typeof(VsCodeUserDirectory[]))]
[JsonSerializable(typeof(VsCodeConfigApplyResult))]
[JsonSerializable(typeof(VsCodeOllamaConfigStatus))]
[JsonSerializable(typeof(VsCodeConfigFileChange))]
[JsonSerializable(typeof(VsCodeConfigFileChange[]))]
[JsonSerializable(typeof(VsCodeConfigFieldChange))]
[JsonSerializable(typeof(VsCodeConfigFieldChange[]))]
[JsonSerializable(typeof(VsCodeConfigBackup))]
[JsonSerializable(typeof(VsCodeConfigBackup[]))]
[JsonSerializable(typeof(VsCodeConfigRestoreResult))]
[JsonSerializable(typeof(ManagedOllamaConfig))]
[JsonSerializable(typeof(ManagedOllamaModel))]
[JsonSerializable(typeof(ManagedOllamaModel[]))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(IReadOnlyList<OllamaChatMessage>))]
[JsonSerializable(typeof(IReadOnlyList<OllamaModelInfo>))]
[JsonSerializable(typeof(IReadOnlyList<OpenAiModelInfo>))]
[JsonSerializable(typeof(IReadOnlyList<OpenAiChatRequestMessage>))]
[JsonSerializable(typeof(IReadOnlyList<OpenAiChatCompletionChoice>))]
[JsonSerializable(typeof(IReadOnlyList<OpenAiChatCompletionChunkChoice>))]
[JsonSerializable(typeof(IReadOnlyList<OpenAiTool>))]
[JsonSerializable(typeof(IReadOnlyList<OpenAiToolCall>))]
[JsonSerializable(typeof(IReadOnlyList<ChatTool>))]
[JsonSerializable(typeof(IReadOnlyList<ChatToolCall>))]
[JsonSerializable(typeof(IReadOnlyList<ProviderConfigView>))]
[JsonSerializable(typeof(IReadOnlyList<ProviderConfigExportItem>))]
[JsonSerializable(typeof(IReadOnlyList<RequestLogEntry>))]
[JsonSerializable(typeof(IReadOnlyList<UsagePriceRule>))]
[JsonSerializable(typeof(IReadOnlyList<CopilotCompatibilityProbeStep>))]
[JsonSerializable(typeof(IReadOnlyList<UpdateSourceCheckResult>))]
[JsonSerializable(typeof(IReadOnlyList<VsCodeUserDirectory>))]
[JsonSerializable(typeof(IReadOnlyList<VsCodeConfigFileChange>))]
[JsonSerializable(typeof(IReadOnlyList<VsCodeConfigFieldChange>))]
[JsonSerializable(typeof(IReadOnlyList<VsCodeConfigBackup>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class VSCopilotSwitchApiJsonContext : JsonSerializerContext;

public sealed record HealthResponse(string Name, string Status, string Mode);

public sealed record AboutInfoResponse(
    string Title,
    string Version,
    string GitHubUrl,
    string EnterpriseWeChatQrPath);

public sealed record ErrorMessageResponse(string Error);

public sealed record PortStatusResponse(int Port, bool Available, string Message);

public sealed record OllamaVersionResponse(
    [property: JsonPropertyName("version")] string Version);

public sealed record OpenAiModelListResponse(
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("data")] IReadOnlyList<OpenAiModelInfo> Data);

public sealed record OpenAiModelInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("owned_by")] string OwnedBy);

public sealed record Vs2026ByomInfoResponse(
    string? Endpoint,
    string ModelId,
    string ApiKeyPlaceholder,
    bool HttpsEnabled,
    string Message);

public sealed record ApplyVsCodeOllamaConfigRequest(
    string UserDirectory,
    ManagedOllamaConfig? Config,
    bool DryRun = true);

public sealed record VsCodeUserDirectoryRequest(string UserDirectory);

public sealed record RemoveVsCodeOllamaConfigRequest(string UserDirectory, bool DryRun = true);

public sealed record ListVsCodeConfigBackupsRequest(string UserDirectory);

public sealed record RestoreVsCodeConfigBackupRequest(string UserDirectory, string BackupPath);

public sealed record OpenAiChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<OpenAiChatRequestMessage>? Messages,
    [property: JsonPropertyName("stream")] bool? Stream,
    [property: JsonPropertyName("tools")] IReadOnlyList<OpenAiTool>? Tools = null,
    [property: JsonPropertyName("tool_choice")] JsonElement? ToolChoice = null,
    [property: JsonPropertyName("reasoning_effort")] string? ReasoningEffort = null,
    [property: JsonPropertyName("thinking")] JsonElement? Thinking = null,
    [property: JsonPropertyName("think")] JsonElement? Think = null);

public sealed record OpenAiChatRequestMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] JsonElement? Content,
    [property: JsonPropertyName("tool_calls")] IReadOnlyList<OpenAiToolCall>? ToolCalls = null,
    [property: JsonPropertyName("tool_call_id")] string? ToolCallId = null,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("reasoning_content")] string? ReasoningContent = null,
    [property: JsonPropertyName("thinking")] string? Thinking = null);

public sealed record OpenAiChatCompletionResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("choices")] IReadOnlyList<OpenAiChatCompletionChoice> Choices,
    [property: JsonPropertyName("usage")] OpenAiUsage? Usage = null);

public sealed record OpenAiChatCompletionChoice(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("message")] OpenAiChatCompletionMessage Message,
    [property: JsonPropertyName("finish_reason")] string FinishReason);

public sealed record OpenAiChatCompletionMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("tool_calls")] IReadOnlyList<OpenAiToolCall>? ToolCalls = null,
    [property: JsonPropertyName("reasoning_content")] string? ReasoningContent = null);

public sealed record OpenAiChatCompletionChunk(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("choices")] IReadOnlyList<OpenAiChatCompletionChunkChoice> Choices,
    [property: JsonPropertyName("usage")] OpenAiUsage? Usage = null);

public sealed record OpenAiChatCompletionChunkChoice(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("delta")] OpenAiChatCompletionDelta Delta,
    [property: JsonPropertyName("finish_reason")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    string? FinishReason);

public sealed record OpenAiChatCompletionDelta(
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("tool_calls")] IReadOnlyList<OpenAiToolCall>? ToolCalls = null,
    [property: JsonPropertyName("reasoning_content")] string? ReasoningContent = null);

public sealed record OpenAiTool(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] OpenAiToolFunction Function);

public sealed record OpenAiToolFunction(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("parameters")] JsonElement? Parameters);

public sealed record OpenAiToolCall(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("function")] OpenAiFunctionCall? Function,
    [property: JsonPropertyName("index")] int? Index = null);

public sealed record OpenAiFunctionCall(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("arguments")] string? Arguments);

public sealed record OpenAiUsage(
    [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
    [property: JsonPropertyName("total_tokens")] int? TotalTokens);

public sealed record OpenAiErrorResponse(
    [property: JsonPropertyName("error")] OpenAiErrorBody Error);

public sealed record OpenAiErrorBody(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("code")] string Code);
