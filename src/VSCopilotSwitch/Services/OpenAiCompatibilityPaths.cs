namespace VSCopilotSwitch.Services;

public static class OpenAiCompatibilityPaths
{
    public static readonly string[] ModelListPaths =
    {
        "/v1/models",
        "/openai/v1/models"
    };

    public static readonly string[] ModelDetailPaths =
    {
        "/v1/models/{modelId}",
        "/openai/v1/models/{modelId}"
    };

    public static readonly string[] ChatCompletionPaths =
    {
        "/v1/chat/completions",
        "/openai/v1/chat/completions",
        "/chat/completions",
        "/v1/v1/chat/completions",
        "/api/v1/chat/completions"
    };

    public static bool IsChatCompletionPath(string path)
        => ChatCompletionPaths.Contains(path, StringComparer.OrdinalIgnoreCase);
}
