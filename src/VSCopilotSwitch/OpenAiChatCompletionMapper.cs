using VSCopilotSwitch.Core.Ollama;
using VSCopilotSwitch.Core.Providers;

public static class OpenAiChatCompletionMapper
{
    public static OpenAiChatCompletionResponse CreateResponse(
        string id,
        long created,
        string requestedModel,
        OllamaChatResponse response)
    {
        var toolCalls = ToToolCalls(response.Message.ToolCalls);
        var reasoningContent = ToReasoningContent(response.Message);
        return new OpenAiChatCompletionResponse(
            id,
            "chat.completion",
            created,
            requestedModel,
            new[]
            {
                new OpenAiChatCompletionChoice(
                    0,
                    new OpenAiChatCompletionMessage(
                        "assistant",
                        ToAssistantContent(response.Message.Content, toolCalls, reasoningContent),
                        toolCalls,
                        reasoningContent),
                    ToFinishReason(response.DoneReason))
            },
            ToUsage(response.Usage));
    }

    public static OpenAiChatCompletionChunk CreateRoleChunk(
        string id,
        long created,
        string requestedModel)
        => new(
            id,
            "chat.completion.chunk",
            created,
            requestedModel,
            new[]
            {
                new OpenAiChatCompletionChunkChoice(
                    0,
                new OpenAiChatCompletionDelta("assistant", null),
                    null)
            });

    public static OpenAiChatCompletionChunk CreateDeltaChunk(
        string id,
        long created,
        string requestedModel,
        OllamaChatResponse chunk)
    {
        var toolCalls = ToToolCalls(chunk.Message.ToolCalls);
        var reasoningContent = ToReasoningContent(chunk.Message);
        return new OpenAiChatCompletionChunk(
            id,
            "chat.completion.chunk",
            created,
            requestedModel,
            new[]
            {
                new OpenAiChatCompletionChunkChoice(
                    0,
                    new OpenAiChatCompletionDelta(
                        null,
                        ToAssistantContent(chunk.Message.Content, toolCalls, reasoningContent),
                        toolCalls,
                        reasoningContent),
                    null)
            },
            ToUsage(chunk.Usage));
    }

    public static OpenAiChatCompletionChunk CreateFinishChunk(
        string id,
        long created,
        string requestedModel,
        string? doneReason,
        ChatUsage? usage)
        => new(
            id,
            "chat.completion.chunk",
            created,
            requestedModel,
            new[]
            {
                new OpenAiChatCompletionChunkChoice(
                    0,
                    new OpenAiChatCompletionDelta(null, null),
                    ToFinishReason(doneReason))
            },
            ToUsage(usage));

    public static IReadOnlyList<OpenAiToolCall>? ToToolCalls(IReadOnlyList<ChatToolCall>? toolCalls)
    {
        var mapped = toolCalls?
            .Select(toolCall => new OpenAiToolCall(
                string.IsNullOrWhiteSpace(toolCall.Id) ? null : toolCall.Id,
                ShouldEmitToolCallType(toolCall)
                    ? string.IsNullOrWhiteSpace(toolCall.Type) ? "function" : toolCall.Type
                    : null,
                new OpenAiFunctionCall(
                    string.IsNullOrWhiteSpace(toolCall.Function.Name) ? null : toolCall.Function.Name,
                    toolCall.Function.Arguments),
                toolCall.Index))
            .ToArray();

        return mapped is { Length: > 0 } ? mapped : null;
    }

    public static OpenAiUsage? ToUsage(ChatUsage? usage)
        => usage is null
            ? null
            : new OpenAiUsage(usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens);

    public static string ToFinishReason(string? doneReason)
        => doneReason switch
        {
            null or "" => "stop",
            "stop" => "stop",
            "length" => "length",
            "tool_calls" => "tool_calls",
            "content_filter" => "content_filter",
            _ => doneReason
        };

    private static string? ToAssistantContent(
        string? content,
        IReadOnlyList<OpenAiToolCall>? toolCalls,
        string? reasoningContent)
        => string.IsNullOrEmpty(content) && (toolCalls is not null || !string.IsNullOrEmpty(reasoningContent))
            ? null
            : content;

    private static string? ToReasoningContent(OllamaChatMessage message)
        => string.IsNullOrWhiteSpace(message.ReasoningContent)
            ? string.IsNullOrWhiteSpace(message.Thinking) ? null : message.Thinking
            : message.ReasoningContent;

    private static bool ShouldEmitToolCallType(ChatToolCall toolCall)
        => !string.IsNullOrWhiteSpace(toolCall.Id)
            || !string.IsNullOrWhiteSpace(toolCall.Function.Name)
            || string.IsNullOrWhiteSpace(toolCall.Function.Arguments);
}
