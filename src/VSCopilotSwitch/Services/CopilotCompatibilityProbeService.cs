using System.Diagnostics;
using System.Text.Json;
using VSCopilotSwitch.Core.Ollama;
using VSCopilotSwitch.Core.Providers;

namespace VSCopilotSwitch.Services;

public interface ICopilotCompatibilityProbeService
{
    Task<CopilotCompatibilityProbeResult> RunAsync(CancellationToken cancellationToken = default);
}

public sealed record CopilotCompatibilityProbeResult(
    bool Success,
    string Message,
    IReadOnlyList<CopilotCompatibilityProbeStep> Steps);

public sealed record CopilotCompatibilityProbeStep(
    string Name,
    string Label,
    string Status,
    string Message,
    long ElapsedMilliseconds);

public sealed class CopilotCompatibilityProbeService : ICopilotCompatibilityProbeService
{
    private const string ModelSuffix = "@vscs";
    private readonly IOllamaProxyService _ollama;

    public CopilotCompatibilityProbeService(IOllamaProxyService ollama)
    {
        _ollama = ollama;
    }

    public async Task<CopilotCompatibilityProbeResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var steps = new List<CopilotCompatibilityProbeStep>();
        OllamaModelInfo? selectedModel = null;

        try
        {
            var tags = await RunStepAsync(
                steps,
                "model_selector",
                "模型选择器",
                async () =>
                {
                    var response = await _ollama.ListTagsAsync(cancellationToken);
                    selectedModel = response.Models.FirstOrDefault();
                    if (selectedModel is null)
                    {
                        return StepOutcome.Fail("模型列表为空，Copilot 模型选择器不会出现可选模型。");
                    }

                    if (!selectedModel.Name.EndsWith(ModelSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        return StepOutcome.Fail($"模型 `{selectedModel.Name}` 未带 {ModelSuffix} 后缀，可能与 Copilot 内置模型冲突。");
                    }

                    return StepOutcome.Pass($"模型 `{selectedModel.Name}` 可出现在 Copilot 模型选择器。");
                });

            if (!tags.Passed || selectedModel is null)
            {
                return CreateResult(steps);
            }

            await RunStepAsync(
                steps,
                "model_metadata",
                "模型能力元信息",
                async () =>
                {
                    var show = await _ollama.ShowAsync(new OllamaShowRequest(selectedModel.Name), cancellationToken);
                    if (!show.ModelInfo.ContainsKey("general.architecture")
                        || !show.ModelInfo.ContainsKey("general.basename"))
                    {
                        return StepOutcome.Fail("/api/show 缺少 Copilot 模型探测需要的 general.architecture 或 general.basename。");
                    }

                    if (show.Capabilities.Any(capability =>
                            string.Equals(capability, "thinking", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(capability, "reasoning", StringComparison.OrdinalIgnoreCase)))
                    {
                        return StepOutcome.Fail("/api/show 不应默认声明未验证的 thinking / reasoning 能力。");
                    }

                    return StepOutcome.Pass("/api/show 返回了基础模型能力元信息。");
                });

            await RunStepAsync(
                steps,
                "chat_completion",
                "普通聊天",
                async () =>
                {
                    var response = await _ollama.ChatAsync(new OllamaChatRequest(
                        selectedModel.Name,
                        new[] { new OllamaChatMessage("user", "ping") },
                        false), cancellationToken);

                    return response.Done
                        ? StepOutcome.Pass("非流式聊天已返回 done=true。")
                        : StepOutcome.Fail("非流式聊天没有返回 done=true。");
                });

            await RunStepAsync(
                steps,
                "agent_tool_call",
                "Agent 工具调用",
                async () =>
                {
                    if (!SupportsToolCalling(selectedModel))
                    {
                        return StepOutcome.Skip("当前模型未声明 tools 能力，跳过 Agent 工具调用探针。");
                    }

                    var response = await _ollama.ChatAsync(new OllamaChatRequest(
                        selectedModel.Name,
                        new[] { new OllamaChatMessage("user", "Use the lookup tool with query ping.") },
                        false,
                        new[] { CreateLookupTool() },
                        new ChatToolChoice("auto")), cancellationToken);

                    return response.Done
                        ? StepOutcome.Pass("携带工具定义的聊天请求已完成，工具字段链路可被代理接受。")
                        : StepOutcome.Fail("携带工具定义的聊天请求没有完成。");
                });

            await RunStepAsync(
                steps,
                "stream_finish",
                "流式结束",
                async () =>
                {
                    var chunks = 0;
                    OllamaChatResponse? finalChunk = null;
                    await foreach (var chunk in _ollama.ChatStreamAsync(new OllamaChatRequest(
                                       selectedModel.Name,
                                       new[] { new OllamaChatMessage("user", "stream ping") },
                                       true), cancellationToken).WithCancellation(cancellationToken))
                    {
                        chunks++;
                        finalChunk = chunk;
                        if (chunk.Done)
                        {
                            break;
                        }
                    }

                    return finalChunk?.Done == true
                        ? StepOutcome.Pass($"流式聊天收到 {chunks} 个分块，并以 done=true 结束。")
                        : StepOutcome.Fail("流式聊天没有收到 done=true 结束分块。");
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }

        return CreateResult(steps);
    }

    private static async Task<StepOutcome> RunStepAsync(
        ICollection<CopilotCompatibilityProbeStep> steps,
        string name,
        string label,
        Func<Task<StepOutcome>> run)
    {
        var elapsed = Stopwatch.StartNew();
        try
        {
            var outcome = await run();
            elapsed.Stop();
            steps.Add(new CopilotCompatibilityProbeStep(name, label, outcome.Status, outcome.Message, elapsed.ElapsedMilliseconds));
            return outcome;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            elapsed.Stop();
            var outcome = StepOutcome.Fail(ex.Message);
            steps.Add(new CopilotCompatibilityProbeStep(name, label, outcome.Status, outcome.Message, elapsed.ElapsedMilliseconds));
            return outcome;
        }
    }

    private static CopilotCompatibilityProbeResult CreateResult(IReadOnlyList<CopilotCompatibilityProbeStep> steps)
    {
        var success = steps.Count > 0 && steps.All(step => !string.Equals(step.Status, "failed", StringComparison.OrdinalIgnoreCase));
        return new CopilotCompatibilityProbeResult(
            success,
            success
                ? "Copilot 兼容探针通过。"
                : "Copilot 兼容探针发现失败项，请查看 steps。",
            steps);
    }

    private static bool SupportsToolCalling(OllamaModelInfo model)
        => model.SupportsToolCalling
            || model.Capabilities.Any(capability => string.Equals(capability, "tools", StringComparison.OrdinalIgnoreCase));

    private static ChatTool CreateLookupTool()
    {
        using var document = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "query": { "type": "string" }
              },
              "required": ["query"]
            }
            """);
        return new ChatTool(
            "function",
            new ChatFunctionTool("lookup", "Search project context", document.RootElement.Clone()));
    }

    private sealed record StepOutcome(bool Passed, string Status, string Message)
    {
        public static StepOutcome Pass(string message) => new(true, "passed", message);

        public static StepOutcome Fail(string message) => new(false, "failed", message);

        public static StepOutcome Skip(string message) => new(true, "skipped", message);
    }
}
