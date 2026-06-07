using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using VSCopilotSwitch.Services;

namespace VSCopilotSwitch;

internal sealed partial class NativeWorkbench
{
    private void ApplyAnalytics(RequestAnalyticsSnapshot snapshot)
    {
        ReplaceChildren(
            _analyticsSummary,
            Row("监听地址", $"{snapshot.Listener.Url} / {snapshot.Listener.Status}"),
            Row("请求数", $"{snapshot.Summary.TotalRequests} 次"),
            Row("Token", $"输入 {snapshot.Summary.InputTokens} / 输出 {snapshot.Summary.OutputTokens} / 合计 {snapshot.Summary.TotalTokens}"),
            Row("费用", $"{snapshot.Summary.TotalCost:0.########} {snapshot.Summary.Currency}，已计价 {snapshot.Summary.PricedRequests}，未计价 {snapshot.Summary.UnpricedRequests}"),
            Row("平均耗时", $"{snapshot.Summary.AverageLatencySeconds:0.###} 秒"),
            new StackPanel()
                .Horizontal()
                .Spacing(8)
                .Children(
                    new Button().Content("刷新分析").Padding(16, 8).OnClick(() => _ = RefreshAnalyticsAsync()),
                    new Button().Content("清空日志").Padding(16, 8).OnClick(() => _ = ClearAnalyticsAsync()),
                    new Button().Content("运行 Copilot 探针").Padding(16, 8).OnClick(() => _ = RunCopilotProbeAsync())));

        if (snapshot.Requests.Count == 0)
        {
            ReplaceChildren(_analyticsRequests, BodyLabel("还没有记录到本地代理请求。"));
            return;
        }

        ReplaceChildren(
            _analyticsRequests,
            snapshot.Requests
                .Take(20)
                .Select(request => Row(
                    $"{request.Timestamp:HH:mm:ss}  {request.Method} {request.Path}",
                    $"{request.StatusCode} / {Empty(request.Model, "未识别模型")} / {request.DurationMilliseconds}ms / Token {request.TotalTokens} / {request.Cost:0.########} {request.Currency} / {request.UserAgent}"))
                .Cast<Element>()
                .ToArray());
    }

    private async Task RefreshAnalyticsAsync()
    {
        try
        {
            var snapshot = await _api.GetJsonAsync<RequestAnalyticsSnapshot>("/internal/analytics", MewUiJsonContext.Default.RequestAnalyticsSnapshot);
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                ApplyAnalytics(snapshot);
                SetStatus("分析统计已刷新");
            });
        }
        catch (Exception ex)
        {
            ShowError("刷新分析统计失败", ex);
        }
    }

    private async Task ClearAnalyticsAsync()
    {
        try
        {
            var snapshot = await _api.PostJsonAsync<object, RequestAnalyticsSnapshot>(
                "/internal/analytics/clear",
                new { },
                MewUiJsonContext.Default.RequestAnalyticsSnapshot);
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                ApplyAnalytics(snapshot);
                SetStatus("分析日志已清空");
            });
        }
        catch (Exception ex)
        {
            ShowError("清空分析日志失败", ex);
        }
    }

    private async Task RunCopilotProbeAsync()
    {
        try
        {
            SetStatus("运行 Copilot 探针...");
            var result = await _api.PostJsonAsync<object, CopilotCompatibilityProbeResult>(
                "/internal/copilot/probe",
                new { },
                MewUiJsonContext.Default.CopilotCompatibilityProbeResult);
            _lastCopilotProbe = result;
            if (_dashboard is not null)
            {
                var analytics = await _api.GetJsonAsync<RequestAnalyticsSnapshot>("/internal/analytics", MewUiJsonContext.Default.RequestAnalyticsSnapshot);
                ApplyOverview(_dashboard, analytics);
            }

            var detail = string.Join(
                Environment.NewLine,
                result.Steps.Select(step => $"{step.Status} {step.Label}: {step.Message}"));
            NativeMessageBox.Show(
                $"{(result.Success ? "探针通过。" : "探针失败。")}{Environment.NewLine}{detail}",
                "Copilot 兼容探针",
                NativeMessageBoxButtons.Ok,
                result.Success ? NativeMessageBoxIcon.Information : NativeMessageBoxIcon.Warning);
            SetStatus(result.Success ? "Copilot 探针通过" : "Copilot 探针失败");
        }
        catch (Exception ex)
        {
            ShowError("运行 Copilot 探针失败", ex);
        }
    }
}
