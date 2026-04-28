using System.Diagnostics;

namespace VSCopilotSwitch.Core.Providers;

public sealed record ProviderConnectionTestStep(
    string Name,
    string Label,
    bool Success,
    string Message,
    long ElapsedMilliseconds);

public sealed record ProviderConnectionTestResult(
    bool Success,
    string Message,
    int ModelCount,
    string? SelectedModel,
    IReadOnlyList<string> Models,
    IReadOnlyList<ProviderConnectionTestStep> Steps);

public sealed class ProviderConnectionTester
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(45);

    private readonly Func<ProviderAdapterConfig, TimeSpan, IModelProvider> _providerFactory;

    public ProviderConnectionTester(Func<ProviderAdapterConfig, TimeSpan, IModelProvider>? providerFactory = null)
    {
        _providerFactory = providerFactory ?? ProviderAdapterFactory.Create;
    }

    public async Task<ProviderConnectionTestResult> TestAsync(
        ProviderAdapterConfig config,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<ProviderConnectionTestStep>();

        if (!Uri.TryCreate(config.BaseUrl?.Trim(), UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not "http" and not "https")
        {
            steps.Add(CreateStep("base_url", "Base URL", false, "Base URL 必须是 http 或 https 绝对地址。", TimeSpan.Zero));
            return CreateFailure("连接测试未开始：Base URL 无效。", steps);
        }

        var endpoint = baseUri.IsDefaultPort ? baseUri.Host : $"{baseUri.Host}:{baseUri.Port}";
        steps.Add(CreateStep("base_url", "Base URL", true, $"将测试 {baseUri.Scheme}://{endpoint}。", TimeSpan.Zero));

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            steps.Add(CreateStep("api_key", "API Key", false, "API Key 不能为空；编辑已有供应商时可留空并使用已保存密钥。", TimeSpan.Zero));
            return CreateFailure("连接测试未开始：缺少 API Key。", steps);
        }

        steps.Add(CreateStep("api_key", "API Key", true, "API Key 已提供，测试结果不会返回密钥原文。", TimeSpan.Zero));

        IModelProvider provider;
        try
        {
            provider = _providerFactory(config, ProbeTimeout);
        }
        catch (Exception ex)
        {
            steps.Add(CreateStep("model_list", "模型列表", false, SanitizePublicMessage(ex.Message, config.ApiKey), TimeSpan.Zero));
            return CreateFailure("连接测试失败：Provider 初始化失败。", steps);
        }

        IReadOnlyList<ProviderModel> models;
        var elapsed = Stopwatch.StartNew();
        try
        {
            models = await provider.ListModelsAsync(cancellationToken);
            elapsed.Stop();
            steps.Add(CreateStep("model_list", "模型列表", true, $"获取到 {models.Count} 个可用模型。", elapsed.Elapsed));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            elapsed.Stop();
            steps.Add(CreateStep("model_list", "模型列表", false, SanitizeException(ex, config.ApiKey), elapsed.Elapsed));
            return CreateFailure("连接测试失败：模型列表获取失败。", steps);
        }

        var selectedModel = SelectModel(models, config.Model);
        if (selectedModel is null)
        {
            var message = string.IsNullOrWhiteSpace(config.Model)
                ? "供应商没有返回可用于聊天探测的模型。"
                : $"模型列表中没有找到“{config.Model.Trim()}”，请检查模型名称。";
            steps.Add(CreateStep("chat_probe", "聊天探测", false, message, TimeSpan.Zero));
            return new ProviderConnectionTestResult(false, "连接测试失败：无法选择聊天探测模型。", models.Count, null, ToPublicModels(models), steps);
        }

        elapsed.Restart();
        try
        {
            // 只发送最小 user 消息，不携带本地配置、路径或密钥，避免探测请求泄露用户环境。
            await provider.ChatAsync(new ChatRequest(
                selectedModel.Name,
                new[] { new ChatMessage("user", "ping") },
                false,
                selectedModel.Provider,
                selectedModel.UpstreamModel), cancellationToken);
            elapsed.Stop();
            steps.Add(CreateStep("chat_probe", "聊天探测", true, $"模型“{selectedModel.UpstreamModel}”已返回非流式响应。", elapsed.Elapsed));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            elapsed.Stop();
            steps.Add(CreateStep("chat_probe", "聊天探测", false, SanitizeException(ex, config.ApiKey), elapsed.Elapsed));
            return new ProviderConnectionTestResult(false, "连接测试失败：最小聊天探测未通过。", models.Count, selectedModel.UpstreamModel, ToPublicModels(models), steps);
        }

        return new ProviderConnectionTestResult(
            true,
            $"连接测试通过：模型列表 {models.Count} 个，聊天探测成功。",
            models.Count,
            selectedModel.UpstreamModel,
            ToPublicModels(models),
            steps);
    }

    private static ProviderModel? SelectModel(IReadOnlyList<ProviderModel> models, string requestedModel)
    {
        if (models.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(requestedModel))
        {
            return SelectPreferredModel(models) ?? models[0];
        }

        var requested = requestedModel.Trim();
        return models.FirstOrDefault(model =>
            string.Equals(model.Name, requested, StringComparison.OrdinalIgnoreCase)
            || string.Equals(model.UpstreamModel, requested, StringComparison.OrdinalIgnoreCase)
            || string.Equals(model.DisplayName, requested, StringComparison.OrdinalIgnoreCase)
            || model.Aliases?.Any(alias => string.Equals(alias, requested, StringComparison.OrdinalIgnoreCase)) == true);
    }

    private static ProviderModel? SelectPreferredModel(IReadOnlyList<ProviderModel> models)
    {
        var preferences = new[] { "gpt55", "sonnet46" };
        foreach (var preference in preferences)
        {
            var matched = models.FirstOrDefault(model => GetSearchTokens(model)
                .Any(token => NormalizeSearchToken(token).Contains(preference, StringComparison.OrdinalIgnoreCase)));
            if (matched is not null)
            {
                return matched;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetSearchTokens(ProviderModel model)
    {
        yield return model.Name;
        yield return model.UpstreamModel;
        yield return model.DisplayName;

        foreach (var alias in model.Aliases ?? Array.Empty<string>())
        {
            yield return alias;
        }
    }

    private static string NormalizeSearchToken(string value)
        => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static IReadOnlyList<string> ToPublicModels(IReadOnlyList<ProviderModel> models)
        => models
            .Select(model => model.UpstreamModel)
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToArray();

    private static ProviderConnectionTestResult CreateFailure(
        string message,
        IReadOnlyList<ProviderConnectionTestStep> steps)
        => new(false, message, 0, null, Array.Empty<string>(), steps);

    private static ProviderConnectionTestStep CreateStep(
        string name,
        string label,
        bool success,
        string message,
        TimeSpan elapsed)
        => new(name, label, success, message, Math.Max(0, (long)elapsed.TotalMilliseconds));

    private static string SanitizeException(Exception exception, string apiKey)
        => exception is ProviderException providerException
            ? SanitizePublicMessage(providerException.PublicMessage, apiKey)
            : SanitizePublicMessage(exception.Message, apiKey);

    private static string SanitizePublicMessage(string? message, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "上游返回了空错误，请检查供应商控制台。";
        }

        var sanitized = message.Trim();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            sanitized = sanitized.Replace(apiKey.Trim(), "[已脱敏密钥]", StringComparison.Ordinal);
        }

        return sanitized.Length <= 300 ? sanitized : sanitized[..300] + "...";
    }
}
