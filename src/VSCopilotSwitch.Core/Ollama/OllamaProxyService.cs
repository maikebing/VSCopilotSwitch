using VSCopilotSwitch.Core.Providers;

namespace VSCopilotSwitch.Core.Ollama;

public interface IOllamaProxyService
{
    Task<OllamaTagsResponse> ListTagsAsync(CancellationToken cancellationToken = default);

    Task<OllamaChatResponse> ChatAsync(OllamaChatRequest request, CancellationToken cancellationToken = default);
}

public sealed class OllamaProxyService(IModelProvider provider) : IOllamaProxyService
{
    public async Task<OllamaTagsResponse> ListTagsAsync(CancellationToken cancellationToken = default)
    {
        var models = await provider.ListModelsAsync(cancellationToken);
        return new OllamaTagsResponse(models.Select(model => new OllamaModelInfo(
            model.Name,
            model.Name,
            DateTimeOffset.UtcNow,
            0,
            $"vscopilotswitch:{model.Provider}:{model.UpstreamModel}",
            new OllamaModelDetails(
                model.UpstreamModel,
                "provider-adapter",
                model.Provider,
                new[] { model.Provider },
                "remote",
                "remote"))).ToArray());
    }

    public async Task<OllamaChatResponse> ChatAsync(OllamaChatRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new ArgumentException("Ollama chat request requires a model.", nameof(request));
        }

        var messages = request.Messages?.Select(message => new ChatMessage(message.Role, message.Content)).ToArray() ?? Array.Empty<ChatMessage>();
        var response = await provider.ChatAsync(new ChatRequest(request.Model, messages, request.Stream ?? false), cancellationToken);

        return new OllamaChatResponse(
            response.Model,
            DateTimeOffset.UtcNow,
            new OllamaChatMessage("assistant", response.Content),
            response.DoneReason,
            true);
    }
}
