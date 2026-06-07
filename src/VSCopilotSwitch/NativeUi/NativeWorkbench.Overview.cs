using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using VSCopilotSwitch.Services;

namespace VSCopilotSwitch;

internal sealed partial class NativeWorkbench
{
    private Element OverviewTab()
        => new StackPanel()
            .Spacing(14)
            .Children(
                Panel("当前生效链路", _overviewRoute),
                new Grid()
                    .Columns("*,*")
                    .Spacing(14)
                    .Children(
                        Panel("快速切换供应商", _overviewProviders).Column(0),
                        Panel("VS Code 配置状态", _overviewDirectories).Column(1)),
                new Grid()
                    .Columns("*,*")
                    .Spacing(14)
                    .Children(
                        Panel("VS Code / Copilot 可见模型", _overviewModels).Column(0),
                        Panel("最近请求结果", _overviewRecentRequest).Column(1)),
                Panel("路由健康解释", _overviewHealth),
                Panel("Copilot 重新发现提示", _overviewCopilotHint));

    private void ApplyOverview(DashboardSnapshot dashboard, RequestAnalyticsSnapshot? analytics)
    {
        var activeProvider = dashboard.Providers.FirstOrDefault(provider => provider.Active);
        var visibleModel = dashboard.Tags.Models.FirstOrDefault();
        var configuredModel = activeProvider?.Model;
        var publicModel = visibleModel?.Name ?? AddVsCodeSuffix(configuredModel);
        var realProvider = IsUsableProvider(activeProvider);
        var existingDirectories = dashboard.Directories.Where(directory => directory.Exists).ToArray();
        var selectedDirectory = dashboard.Directories.FirstOrDefault(directory => string.Equals(directory.Path, _selectedVsCodeDirectory, StringComparison.OrdinalIgnoreCase))
            ?? existingDirectories.FirstOrDefault()
            ?? dashboard.Directories.FirstOrDefault();

        ReplaceChildren(
            _overviewRoute,
            RouteSummaryRow("当前供应商", activeProvider?.Name ?? "未启用", realProvider ? "已保存密钥和模型，可作为真实上游路由。" : "缺少可用供应商时，本地代理会回退到内置占位 Provider。"),
            RouteSummaryRow("供应商协议", Empty(activeProvider?.Vendor, "未配置"), Empty(activeProvider?.ApiUrl, "未配置 API 地址")),
            RouteSummaryRow("上游模型", Empty(configuredModel, "未设置模型"), $"VS Code / Copilot 公开模型名：{Empty(publicModel, "等待模型列表")}"),
            RouteSummaryRow("代理健康", $"{dashboard.Health.Status} / {dashboard.Health.Mode}", _api.BaseAddress?.AbsoluteUri ?? "http://127.0.0.1:5124/"),
            new StackPanel()
                .Horizontal()
                .Spacing(8)
                .Children(
                    new Button().Content("刷新链路状态").Padding(16, 8).OnClick(() => _ = RefreshAsync()),
                    new Button().Content("运行健康探针").Padding(16, 8).OnClick(() => _ = RunCopilotProbeAsync()),
                    new Button().Content("打开 VS Code 写入向导").Padding(16, 8).OnClick(SelectVsCodeTab)));

        ReplaceChildren(_overviewProviders, BuildOverviewProviderRows(dashboard.Providers));
        ReplaceChildren(_overviewModels, BuildOverviewModelRows(dashboard.Tags.Models, activeProvider));
        ReplaceChildren(
            _overviewDirectories,
            RouteSummaryRow("已发现目录", $"{existingDirectories.Length} 个可用 / {dashboard.Directories.Count} 个候选", selectedDirectory?.Description ?? "未发现 VS Code User 目录"),
            RouteSummaryRow("当前目标", selectedDirectory?.Path ?? "未选择", "写入前仍需进入 VS Code 页生成 dry-run 差异预览并再次确认。"),
            new StackPanel()
                .Horizontal()
                .Spacing(8)
                .Children(
                    new Button().Content("进入 VS Code 页").Padding(16, 8).OnClick(SelectVsCodeTab),
                    new Button().Content("刷新目录").Padding(16, 8).OnClick(() => _ = RefreshAsync())));
        ReplaceChildren(_overviewRecentRequest, BuildRecentRequestRows(analytics));
        ReplaceChildren(_overviewHealth, BuildRouteHealthRows(dashboard, analytics, _lastCopilotProbe));
        ReplaceChildren(
            _overviewCopilotHint,
            BodyLabel("切换供应商或公开模型名变化后，VS Code Copilot 可能沿用旧模型缓存。请在 Copilot 模型选择器中刷新 Ollama Provider，或重新选择带 @vscs 后缀的模型。"),
            BodyLabel("首页只负责路由切换和状态解释；VS Code 配置写入仍由 VS Code 页的 dry-run、差异预览、二次确认和备份流程完成。"));
    }

    private Element[] BuildOverviewProviderRows(IReadOnlyList<DashboardProviderConfigView> providers)
    {
        if (providers.Count == 0)
        {
            return [BodyLabel("还没有供应商配置。请到供应商页新增并保存密钥。")];
        }

        return providers
            .Select(provider => OverviewProviderRow(provider))
            .Cast<Element>()
            .ToArray();
    }

    private Element OverviewProviderRow(DashboardProviderConfigView provider)
    {
        var usable = IsUsableProvider(provider);
        var title = provider.Active ? $"{provider.Name}  [当前路由]" : provider.Name;
        var subtitle = usable
            ? $"{provider.Vendor} / 上游模型 {provider.Model} / 密钥 {provider.ApiKeyPreview ?? "已保存"}"
            : $"{provider.Vendor} / 不可切换：缺少{(provider.HasApiKey ? "模型" : "密钥")}{(string.IsNullOrWhiteSpace(provider.Model) ? "或模型" : string.Empty)}";

        return new DockPanel()
            .Padding(8, 6)
            .Children(
                new StackPanel()
                    .Spacing(3)
                    .Children(
                        new Label().Text(title).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis),
                        BodyLabel(subtitle)).DockLeft(),
                new Button()
                    .Content(provider.Active ? "正在使用" : "切换到此供应商")
                    .Padding(12, 6)
                    .OnClick(() => _ = ActivateProviderAsync(provider))
                    .IsEnabled(usable && !provider.Active)
                    .DockRight());
    }

    private static Element[] BuildOverviewModelRows(IReadOnlyList<DashboardModelInfo> models, DashboardProviderConfigView? activeProvider)
    {
        if (models.Count == 0)
        {
            return [BodyLabel("当前路由还没有返回模型列表；若供应商刚切换，请刷新或先测试连接。")];
        }

        return models
            .Take(8)
            .Select(model => Row(
                model.Name,
                $"上游：{Empty(model.Model, activeProvider?.Model ?? "未识别")} / {model.Details?.Family ?? "provider"}"))
            .Cast<Element>()
            .Append(BodyLabel(models.Count > 8 ? $"另有 {models.Count - 8} 个模型未显示，可到供应商页刷新/测试。" : string.Empty))
            .Where(element => element is not Label label || !string.IsNullOrWhiteSpace(label.Text))
            .ToArray();
    }

    private static Element[] BuildRecentRequestRows(RequestAnalyticsSnapshot? analytics)
    {
        var request = analytics?.Requests.FirstOrDefault();
        if (request is null)
        {
            return [BodyLabel("还没有记录到 VS Code / Copilot 或 OpenAI-compatible 客户端请求。")];
        }

        var result = request.StatusCode is >= 200 and < 300 ? "成功" : "失败";
        var failure = request.StatusCode is >= 200 and < 300
            ? "上游已返回响应。"
            : ShortValue(request.ResponseBody ?? "响应体为空，请查看分析页详情。");

        return
        [
            RouteSummaryRow($"{request.Timestamp:HH:mm:ss} {request.Method} {request.Path}", $"{result} / HTTP {request.StatusCode}", failure),
            RouteSummaryRow("模型与耗时", $"{Empty(request.Model, "未识别模型")} / {request.DurationMilliseconds}ms", $"Token {request.TotalTokens} / {request.Cost:0.########} {request.Currency}")
        ];
    }

    private Element[] BuildRouteHealthRows(DashboardSnapshot dashboard, RequestAnalyticsSnapshot? analytics, CopilotCompatibilityProbeResult? probe)
    {
        var activeProvider = dashboard.Providers.FirstOrDefault(provider => provider.Active);
        var recentRequest = analytics?.Requests.FirstOrDefault();
        var providerReady = IsUsableProvider(activeProvider);
        var modelCount = dashboard.Tags.Models.Count;
        var probeSummary = probe is null
            ? "尚未运行；点击运行健康探针可验证模型选择器、/api/show、聊天、工具字段和流式结束。"
            : $"{(probe.Success ? "通过" : "失败")} / {probe.Steps.Count} 步 / {ProbeStepSummary(probe)}";

        return
        [
            RouteSummaryRow("本地代理", $"{dashboard.Health.Status} / {dashboard.Health.Mode}", "本地 API 可达；监听地址见顶部“打开本地 API”入口。"),
            RouteSummaryRow("当前供应商", ProviderRouteStatus(activeProvider), ProviderRouteAdvice(activeProvider, providerReady)),
            RouteSummaryRow("模型列表", modelCount > 0 ? $"/api/tags 返回 {modelCount} 个模型" : "/api/tags 模型列表为空", ModelListAdvice(modelCount, providerReady)),
            RouteSummaryRow("最近请求", RecentRequestStatus(recentRequest), RecentRequestAdvice(recentRequest)),
            RouteSummaryRow("Copilot 健康探针", probeSummary, "探针结果只保存在当前 UI 会话内，不写入配置或日志。"),
            new StackPanel()
                .Horizontal()
                .Spacing(8)
                .Children(
                    new Button().Content("运行健康探针").Padding(16, 8).OnClick(() => _ = RunCopilotProbeAsync()),
                    new Button().Content("打开分析页").Padding(16, 8).OnClick(SelectAnalyticsTab))
        ];
    }

    private static string ProviderRouteStatus(DashboardProviderConfigView? provider)
    {
        if (provider is null)
        {
            return "未启用供应商";
        }

        var keyStatus = provider.HasApiKey ? "密钥已保存" : "缺少密钥";
        var modelStatus = string.IsNullOrWhiteSpace(provider.Model) ? "缺少模型" : $"模型 {provider.Model}";
        return $"{provider.Name} / {keyStatus} / {modelStatus}";
    }

    private static string ProviderRouteAdvice(DashboardProviderConfigView? provider, bool providerReady)
    {
        if (provider is null)
        {
            return "请先到供应商页新增并启用一个真实供应商。";
        }

        if (providerReady)
        {
            return "当前供应商已具备真实路由条件；若请求失败，请运行连接测试或查看分析页。";
        }

        return provider.HasApiKey
            ? "供应商已保存密钥但缺少模型名；请测试连接并回填可用模型。"
            : "供应商缺少 API Key；请到供应商页保存密钥后再测试连接。";
    }

    private static string ModelListAdvice(int modelCount, bool providerReady)
    {
        if (modelCount > 0)
        {
            return "VS Code Copilot 应能发现带 @vscs 后缀的公开模型。";
        }

        return providerReady
            ? "模型列表为空，建议先测试连接、检查 Base URL/API Key 权限，再刷新模型。"
            : "供应商尚未满足真实路由条件，模型列表会保持为空或回退占位。";
    }

    private static string RecentRequestStatus(RequestLogEntry? request)
    {
        if (request is null)
        {
            return "尚无请求";
        }

        var result = request.StatusCode is >= 200 and < 300 ? "成功" : "失败";
        return $"{result} / HTTP {request.StatusCode} / {Empty(request.Model, "未识别模型")}";
    }

    private static string RecentRequestAdvice(RequestLogEntry? request)
    {
        if (request is null)
        {
            return "还没有客户端请求；可从 VS Code Copilot 或 OpenAI-compatible 客户端发起一次调用。";
        }

        return request.StatusCode switch
        {
            >= 200 and < 300 => $"最近请求已完成，耗时 {request.DurationMilliseconds}ms。",
            401 or 403 => "鉴权或权限失败：检查 API Key、组织/项目权限、供应商账号额度和模型访问权限。",
            404 => "模型或路径不存在：检查模型名、Base URL 是否重复带 /v1 或 /api。",
            429 => "供应商限流：稍后重试，或切换到备用供应商/模型。",
            500 or 502 or 503 or 504 => "上游或网络异常：检查供应商状态、代理网络和 Base URL 可达性。",
            _ => "请打开分析页查看脱敏后的请求/响应摘要，定位具体失败原因。"
        };
    }

    private static string ProbeStepSummary(CopilotCompatibilityProbeResult probe)
    {
        var failed = probe.Steps.FirstOrDefault(step => string.Equals(step.Status, "failed", StringComparison.OrdinalIgnoreCase));
        if (failed is not null)
        {
            return $"失败项：{failed.Label} - {ShortValue(failed.Message)}";
        }

        var skipped = probe.Steps.Count(step => string.Equals(step.Status, "skipped", StringComparison.OrdinalIgnoreCase));
        return skipped > 0 ? $"含 {skipped} 个跳过项" : "全部步骤通过";
    }

    private static Element RouteSummaryRow(string title, string value, string detail)
        => new DockPanel()
            .Padding(8, 6)
            .Children(
                new StackPanel()
                    .Spacing(3)
                    .Children(
                        BodyLabel(title),
                        new Label().Text(value).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis),
                        BodyLabel(detail)));

    private void SelectVsCodeTab()
    {
        _tabs.SelectedIndex = 2;
        SetStatus("请先生成 dry-run 差异预览，再确认写入 VS Code 配置");
    }

    private void SelectAnalyticsTab()
    {
        _tabs.SelectedIndex = 3;
        SetStatus("请查看最近请求的脱敏分析详情");
    }

    private static bool IsUsableProvider(DashboardProviderConfigView? provider)
        => provider is not null && provider.HasApiKey && !string.IsNullOrWhiteSpace(provider.Model);

    private static string AddVsCodeSuffix(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return string.Empty;
        }

        var trimmed = model.Trim();
        return trimmed.EndsWith("@vscs", StringComparison.OrdinalIgnoreCase) ? trimmed : $"{trimmed}@vscs";
    }
}
