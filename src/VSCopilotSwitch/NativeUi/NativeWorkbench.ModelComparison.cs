using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using VSCopilotSwitch.Services;

namespace VSCopilotSwitch;

internal sealed partial class NativeWorkbench
{
    private void RenderModelComparison(IReadOnlyList<ModelComparisonResult> history)
    {
        var activeProvider = _dashboard?.Providers.FirstOrDefault(provider => provider.Active);
        var selectedModel = FirstNonEmpty(_providerEditorState.Model, activeProvider?.Model, _dashboard?.Tags.Models.FirstOrDefault()?.Model, _dashboard?.Tags.Models.FirstOrDefault()?.Name, string.Empty);
        var providerName = FirstNonEmpty(activeProvider?.Name, _providerEditorState.Name, "当前供应商");
        var children = new List<Element>
        {
            BodyLabel("对当前启用供应商的选定模型执行普通响应、首 token、流式结束、工具调用、上下文声明和费用估算探针。结果只保存在本次运行内存中。"),
            BodyLabel($"当前目标：{providerName} / {Empty(selectedModel, "未选择模型")}"),
            new StackPanel()
                .Horizontal()
                .Spacing(8)
                .Children(
                    new Button().Content("测试并保存结果").Padding(14, 7).OnClick(() => _ = CompareCurrentModelAsync(saveResult: true)),
                    new Button().Content("刷新历史").Padding(14, 7).OnClick(() => _ = RefreshModelComparisonHistoryAsync()))
        };

        if (history.Count == 0)
        {
            children.Add(BodyLabel("暂无模型比较历史。"));
        }
        else
        {
            children.AddRange(history.Take(5).Select(BuildModelComparisonRow));
        }

        ReplaceChildren(_modelComparison, children.ToArray());
    }

    private Element BuildModelComparisonRow(ModelComparisonResult result)
        => new StackPanel()
            .Spacing(4)
            .Children(
                new Label().Text($"{(result.Success ? "通过" : "失败")} {result.ProviderName} / {result.Model}").SemiBold().TextTrimming(TextTrimming.CharacterEllipsis),
                BodyLabel($"延迟 {result.LatencyMs}ms / 首 token {result.FirstTokenMs}ms / 流式 {(result.StreamFinished ? "完成" : "未完成")} / 工具 {DescribeProbe(result.ToolProbePassed)}"),
                BodyLabel($"上下文 {result.ContextLength?.ToString() ?? "未声明"} / 视觉 {DescribeProbe(result.SupportsVision)} / Token {result.InputTokens}+{result.OutputTokens} / 费用 {result.EstimatedCost:0.########} {result.Currency} ({result.CostSource})"),
                BodyLabel(string.Join("；", result.Steps.Select(step => $"{step.Label}:{step.Status}"))));

    private async Task CompareCurrentModelAsync(bool saveResult)
    {
        var activeProvider = _dashboard?.Providers.FirstOrDefault(provider => provider.Active);
        var selectedModel = FirstNonEmpty(_providerEditorState.Model, activeProvider?.Model, _dashboard?.Tags.Models.FirstOrDefault()?.Model, _dashboard?.Tags.Models.FirstOrDefault()?.Name, string.Empty);
        if (string.IsNullOrWhiteSpace(selectedModel))
        {
            SetStatus("请先选择或填写要测试的模型");
            return;
        }

        try
        {
            SetStatus($"正在比较模型 {selectedModel}...");
            var result = await _api.PostJsonAsync<ModelComparisonRequest, ModelComparisonResult>(
                "/internal/models/compare",
                new ModelComparisonRequest(activeProvider?.Name ?? _providerEditorState.Name, selectedModel, SaveResult: saveResult),
                MewUiJsonContext.Default.ModelComparisonResult);
            await RefreshModelComparisonHistoryAsync();
            SetStatus(result.Success
                ? $"模型比较完成：{selectedModel}，延迟 {result.LatencyMs}ms，首 token {result.FirstTokenMs}ms"
                : $"模型比较失败：{selectedModel}");
        }
        catch (Exception ex)
        {
            ShowError("模型比较失败", ex);
        }
    }

    private async Task RefreshModelComparisonHistoryAsync()
    {
        try
        {
            var history = await _api.GetJsonAsync<IReadOnlyList<ModelComparisonResult>>("/internal/models/compare/history", MewUiJsonContext.Default.IReadOnlyListModelComparisonResult);
            Application.Current.Dispatcher?.BeginInvoke(() => RenderModelComparison(history));
        }
        catch (Exception ex)
        {
            ShowError("刷新模型比较历史失败", ex);
        }
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string DescribeProbe(bool? value)
        => value switch
        {
            true => "通过/支持",
            false => "失败/不支持",
            _ => "未声明"
        };
}
