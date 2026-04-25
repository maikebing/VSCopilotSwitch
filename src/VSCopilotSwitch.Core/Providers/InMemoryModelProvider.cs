namespace VSCopilotSwitch.Core.Providers;

public sealed class InMemoryModelProvider : IModelProvider
{
    private readonly IReadOnlyList<ProviderModel> _models = new[]
    {
        new ProviderModel("vscopilotswitch/default", "vscopilotswitch", "mock/default", "VSCopilotSwitch Default")
    };

    public string Name => "vscopilotswitch";

    public Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_models);
    }

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var lastUserMessage = request.Messages.LastOrDefault(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase));
        var content = lastUserMessage is null
            ? "VSCopilotSwitch Ollama proxy is running. Configure an upstream provider to route real model traffic."
            : $"VSCopilotSwitch Ollama proxy received: {lastUserMessage.Content}";

        return Task.FromResult(new ChatResponse(request.Model, content));
    }
}
