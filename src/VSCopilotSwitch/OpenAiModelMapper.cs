using VSCopilotSwitch.Core.Ollama;

public static class OpenAiModelMapper
{
    public static OpenAiModelListResponse CreateListResponse(OllamaTagsResponse tags)
    {
        var models = tags.Models
            .Where(model => !string.IsNullOrWhiteSpace(model.Name))
            .Select(model => new OpenAiModelInfo(
                model.Name,
                "model",
                ToUnixTimeSeconds(model.ModifiedAt),
                ResolveOwner(model)))
            .DistinctBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new OpenAiModelListResponse("list", models);
    }

    public static OpenAiModelInfo? FindModel(OllamaTagsResponse tags, string modelId)
        => CreateListResponse(tags).Data.FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));

    private static long ToUnixTimeSeconds(DateTimeOffset value)
        => value == default ? 0 : value.ToUnixTimeSeconds();

    private static string ResolveOwner(OllamaModelInfo model)
    {
        if (model.ModelInfo.TryGetValue("vscopilotswitch.provider", out var provider)
            && provider is not null
            && !string.IsNullOrWhiteSpace(provider.ToString()))
        {
            return provider.ToString()!;
        }

        return "vscopilotswitch";
    }
}
