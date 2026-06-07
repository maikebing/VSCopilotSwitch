using System.Diagnostics;
using System.Text.Json;
using VSCopilotSwitch.Core.Providers;

namespace VSCopilotSwitch.Services;

public sealed class ModelComparisonService
{
    private const int MaxHistory = 50;
    private readonly IModelProvider? _provider;
    private readonly Func<ModelComparisonRequest, CancellationToken, Task<IModelProvider>>? _providerFactory;
    private readonly IUsageCostEstimator _costEstimator;
    private readonly List<ModelComparisonResult> _history = new();
    private readonly object _gate = new();

    public ModelComparisonService(IModelProvider provider, IUsageCostEstimator costEstimator)
    {
        _provider = provider;
        _costEstimator = costEstimator;
    }

    public ModelComparisonService(
        Func<ModelComparisonRequest, CancellationToken, Task<IModelProvider>> providerFactory,
        IUsageCostEstimator costEstimator)
    {
        _providerFactory = providerFactory;
        _costEstimator = costEstimator;
    }

    public IReadOnlyList<ModelComparisonResult> GetHistory()
    {
        lock (_gate)
        {
            return _history.ToArray();
        }
    }

    public async Task<ModelComparisonResult> CompareAsync(ModelComparisonRequest request, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.Now;
        var steps = new List<ModelComparisonStep>();
        ProviderModel? modelInfo = null;
        ChatUsage? chatUsage = null;
        ChatUsage? streamUsage = null;
        var latencyMs = 0L;
        var firstTokenMs = 0L;
        var streamFinished = false;
        bool? toolProbePassed = null;

        var provider = await ResolveProviderAsync(request, cancellationToken);

        try
        {
            modelInfo = await MeasureStepAsync(
                steps,
                "model_metadata",
                "模型声明",
                async () => (await provider.ListModelsAsync(cancellationToken))
                    .FirstOrDefault(model => MatchesRequestedModel(model, request.Model)),
                value => value is null ? "未找到模型声明，继续使用请求模型测试。" : $"上下文 {value.Capabilities?.ContextLength?.ToString() ?? "未声明"}，工具 {DescribeBool(value.Capabilities?.SupportsTools)}，视觉 {DescribeBool(value.Capabilities?.SupportsVision)}");

            var chatWatch = Stopwatch.StartNew();
            var chatResponse = await MeasureStepAsync(
                steps,
                "chat_latency",
                "普通响应延迟",
                () => provider.ChatAsync(CreateChatRequest(request), cancellationToken),
                response => $"完成原因 {response.DoneReason}，输出 {response.Content.Length} 字符。");
            chatWatch.Stop();
            latencyMs = chatWatch.ElapsedMilliseconds;
            chatUsage = chatResponse.Usage;

            var streamWatch = Stopwatch.StartNew();
            await foreach (var chunk in provider.ChatStreamAsync(CreateChatRequest(request, stream: true), cancellationToken).WithCancellation(cancellationToken))
            {
                if (firstTokenMs == 0 && !string.IsNullOrEmpty(chunk.Content))
                {
                    firstTokenMs = streamWatch.ElapsedMilliseconds;
                }

                if (chunk.Done)
                {
                    streamFinished = true;
                    streamUsage = chunk.Usage ?? streamUsage;
                    break;
                }

                streamUsage = chunk.Usage ?? streamUsage;
            }

            streamWatch.Stop();
            steps.Add(new ModelComparisonStep(
                "stream_finish",
                "流式结束",
                streamFinished ? "passed" : "failed",
                streamWatch.ElapsedMilliseconds,
                streamFinished ? "流式输出已收到 done。" : "流式输出未收到 done。"));

            if (request.RunToolProbe)
            {
                var toolWatch = Stopwatch.StartNew();
                try
                {
                    var toolResponse = await provider.ChatAsync(CreateToolProbeRequest(request), cancellationToken);
                    toolWatch.Stop();
                    toolProbePassed = toolResponse.ToolCalls is { Count: > 0 };
                    steps.Add(new ModelComparisonStep(
                        "tool_probe",
                        "工具调用探针",
                        toolProbePassed.Value ? "passed" : "failed",
                        toolWatch.ElapsedMilliseconds,
                        toolProbePassed.Value ? "模型返回工具调用。" : "模型未返回工具调用。"));
                }
                catch (Exception ex)
                {
                    toolWatch.Stop();
                    toolProbePassed = false;
                    steps.Add(new ModelComparisonStep("tool_probe", "工具调用探针", "failed", toolWatch.ElapsedMilliseconds, SanitizeMessage(ex.Message)));
                }
            }
        }
        catch (Exception ex)
        {
            steps.Add(new ModelComparisonStep("comparison", "模型比较", "failed", 0, SanitizeMessage(ex.Message)));
        }

        var inputTokens = FirstNonNull(chatUsage?.PromptTokens, streamUsage?.PromptTokens) ?? EstimateTokens(request.Prompt);
        var outputTokens = FirstNonNull(chatUsage?.CompletionTokens, streamUsage?.CompletionTokens) ?? 0;
        var cost = _costEstimator.Estimate(request.Model, inputTokens, outputTokens);
        var success = steps.Count > 0 && steps.All(step => string.Equals(step.Status, "passed", StringComparison.OrdinalIgnoreCase));
        var result = new ModelComparisonResult(
            Guid.NewGuid().ToString("N"),
            startedAt,
            request.ProviderName,
            request.Model,
            success,
            latencyMs,
            firstTokenMs,
            streamFinished,
            toolProbePassed,
            modelInfo?.Capabilities?.SupportsVision,
            modelInfo?.Capabilities?.ContextLength,
            inputTokens,
            outputTokens,
            cost.Amount,
            cost.Currency,
            cost.Source,
            cost.PricingRule,
            steps.ToArray());

        if (request.SaveResult)
        {
            Save(result);
        }

        return result;
    }

    private async Task<IModelProvider> ResolveProviderAsync(ModelComparisonRequest request, CancellationToken cancellationToken)
    {
        if (_provider is not null)
        {
            return _provider;
        }

        if (_providerFactory is null)
        {
            throw new InvalidOperationException("模型比较服务未配置供应商。");
        }

        return await _providerFactory(request, cancellationToken);
    }

    private void Save(ModelComparisonResult result)
    {
        lock (_gate)
        {
            _history.Insert(0, result);
            if (_history.Count > MaxHistory)
            {
                _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
            }
        }
    }

    private static async Task<T> MeasureStepAsync<T>(
        List<ModelComparisonStep> steps,
        string name,
        string label,
        Func<Task<T>> action,
        Func<T, string> buildMessage)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await action();
            stopwatch.Stop();
            steps.Add(new ModelComparisonStep(name, label, "passed", stopwatch.ElapsedMilliseconds, buildMessage(result)));
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            steps.Add(new ModelComparisonStep(name, label, "failed", stopwatch.ElapsedMilliseconds, SanitizeMessage(ex.Message)));
            throw;
        }
    }

    private static ChatRequest CreateChatRequest(ModelComparisonRequest request, bool stream = false)
        => new(
            request.Model,
            [new ChatMessage("user", string.IsNullOrWhiteSpace(request.Prompt) ? "请用一句话回答 pong。" : request.Prompt.Trim())],
            stream,
            request.ProviderName,
            request.Model);

    private static ChatRequest CreateToolProbeRequest(ModelComparisonRequest request)
    {
        using var parameters = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "query": { "type": "string" }
              },
              "required": ["query"]
            }
            """);

        return new ChatRequest(
            request.Model,
            [new ChatMessage("user", "调用 lookup 工具查询 ping。")],
            false,
            request.ProviderName,
            request.Model,
            [new ChatTool("function", new ChatFunctionTool("lookup", "查询关键字。", parameters.RootElement.Clone()))],
            new ChatToolChoice("function", "lookup"));
    }

    private static bool MatchesRequestedModel(ProviderModel model, string requestedModel)
        => string.Equals(model.Name, requestedModel, StringComparison.OrdinalIgnoreCase)
            || string.Equals(model.UpstreamModel, requestedModel, StringComparison.OrdinalIgnoreCase)
            || (model.Aliases?.Any(alias => string.Equals(alias, requestedModel, StringComparison.OrdinalIgnoreCase)) ?? false);

    private static string DescribeBool(bool? value)
        => value switch
        {
            true => "支持",
            false => "不支持",
            _ => "未声明"
        };

    private static int? FirstNonNull(int? first, int? second)
        => first ?? second;

    private static int EstimateTokens(string? text)
        => Math.Max(1, (string.IsNullOrWhiteSpace(text) ? 12 : text.Trim().Length) / 4);

    private static string SanitizeMessage(string message)
        => message.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
}

public sealed record ModelComparisonRequest(
    string ProviderName,
    string Model,
    string? Prompt = null,
    bool RunToolProbe = true,
    bool SaveResult = false);

public sealed record ModelComparisonResult(
    string Id,
    DateTimeOffset StartedAt,
    string ProviderName,
    string Model,
    bool Success,
    long LatencyMs,
    long FirstTokenMs,
    bool StreamFinished,
    bool? ToolProbePassed,
    bool? SupportsVision,
    int? ContextLength,
    int InputTokens,
    int OutputTokens,
    decimal EstimatedCost,
    string Currency,
    string CostSource,
    string? PricingRule,
    IReadOnlyList<ModelComparisonStep> Steps);

public sealed record ModelComparisonStep(
    string Name,
    string Label,
    string Status,
    long DurationMs,
    string Message);
