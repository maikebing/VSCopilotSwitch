namespace VSCopilotSwitch.Core.Providers;

public sealed class InMemoryModelProvider : IModelProvider
{
    private readonly IReadOnlyList<ProviderModel> _models = new[]
    {
        new ProviderModel(
            "vscopilotswitch/default",
            "vscopilotswitch",
            "mock/default",
            "VSCopilotSwitch Default",
            new[] { "default", "mock/default" })
    };

    public string Name => "vscopilotswitch";

    public Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_models);
    }

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BuildResponse(request));
    }

    public async IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = BuildResponse(request);
        foreach (var chunk in SplitForStreaming(response.Content))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new ChatStreamChunk(response.Model, chunk, Done: false);
        }

        yield return new ChatStreamChunk(response.Model, string.Empty, Done: true, response.DoneReason);
    }

    private static ChatResponse BuildResponse(ChatRequest request)
    {
        var lastUserMessage = request.Messages.LastOrDefault(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase));
        var content = lastUserMessage is null
            ? "VSCopilotSwitch Ollama proxy is running. Configure an upstream provider to route real model traffic."
            : $"VSCopilotSwitch Ollama proxy received: {lastUserMessage.Content}";

        return new ChatResponse(request.Model, content);
    }

    private static IEnumerable<string> SplitForStreaming(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            yield break;
        }

        var start = 0;
        for (var index = 0; index < content.Length; index++)
        {
            if (!char.IsWhiteSpace(content[index]))
            {
                continue;
            }

            yield return content[start..(index + 1)];
            start = index + 1;
        }

        if (start < content.Length)
        {
            yield return content[start..];
        }
    }
}
