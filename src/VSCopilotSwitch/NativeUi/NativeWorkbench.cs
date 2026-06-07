using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Microsoft.Extensions.DependencyInjection;
using VSCopilotSwitch.Services;
using VSCopilotSwitch.VsCodeConfig.Models;

namespace VSCopilotSwitch;

internal sealed partial class NativeWorkbench : IDisposable
{
    private readonly VSCopilotSwitchNativeHost _nativeHost;
    private readonly InternalApiClient _api;
    private Win32TrayIcon? _trayIcon;
    private bool _exitRequested;

    private Label _statusText = null!;
    private Label _healthText = null!;
    private Label _providerText = null!;
    private Label _modelText = null!;
    private TabControl _tabs = null!;
    private StackPanel _overviewRoute = null!;
    private StackPanel _overviewProviders = null!;
    private StackPanel _overviewModels = null!;
    private StackPanel _overviewDirectories = null!;
    private StackPanel _overviewRecentRequest = null!;
    private StackPanel _overviewHealth = null!;
    private StackPanel _overviewCopilotHint = null!;
    private StackPanel _providerRows = null!;
    private StackPanel _providerEditor = null!;
    private StackPanel _providerPresets = null!;
    private StackPanel _providerImportPreview = null!;
    private TextBox _providerImportText = null!;
    private StackPanel _modelComparison = null!;
    private StackPanel _vscodeDirectories = null!;
    private StackPanel _vscodePreview = null!;
    private StackPanel _vscodeBackups = null!;
    private StackPanel _analyticsSummary = null!;
    private StackPanel _analyticsRequests = null!;
    private StackPanel _vs2026Panel = null!;

    private DashboardSnapshot? _dashboard;
    private IReadOnlyList<DashboardVsCodeUserDirectory> _directories = Array.Empty<DashboardVsCodeUserDirectory>();
    private string? _selectedVsCodeDirectory;
    private VsCodeConfigApplyResult? _lastVsCodePreview;
    private VsCodePreviewKind _lastVsCodePreviewKind;
    private ProviderEditorState _providerEditorState = ProviderEditorState.CreateNew();
    private string? _pendingDeleteProviderId;
    private string? _pendingRestoreBackupPath;
    private CopilotCompatibilityProbeResult? _lastCopilotProbe;

    public NativeWorkbench(VSCopilotSwitchNativeHost nativeHost)
    {
        _nativeHost = nativeHost;
        _api = new InternalApiClient(nativeHost.ServerUrl);
    }

    public void Run()
    {
        Application.Create()
            .UseAccent(Accent.Blue)
            .BuildMainWindow(CreateWindow)
            .Run();
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
        _api.Dispose();
    }

    private Window CreateWindow()
    {
        var window = new Window()
            .Title("VSCopilotSwitch")
            .Resizable(1220, 800, minWidth: 940, minHeight: 640)
            .Padding(0)
            .Content(BuildShell());

        window.Loaded += () =>
        {
            InitializeTray(window);
            _ = RefreshAsync();
        };

        window.Closing += args =>
        {
            if (_exitRequested)
            {
                return;
            }

            args.Cancel = true;
            window.Hide();
        };

        return window;
    }

    private void InitializeTray(Window window)
    {
        if (_trayIcon is not null)
        {
            return;
        }

        try
        {
            _trayIcon = new Win32TrayIcon(
                window,
                _nativeHost.Services.GetRequiredService<ITrayMenuService>(),
                RequestExit,
                HandleTrayCommandResult);
            _trayIcon.Initialize();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteCrashLog(ex, "Initialize tray icon failed.");
        }
    }


    private void HandleTrayCommandResult(TrayCommandResult result)
    {
        if (!result.Handled)
        {
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                SetStatus(result.Message);
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            SetStatus(result.Message);
        }

        if (result.OpenProviders)
        {
            _tabs.SelectedIndex = 1;
        }

        if (result.ShowWindow)
        {
            _tabs.SelectedIndex = 0;
        }

        if (result.ExitApplication)
        {
            RequestExit();
        }

        if (result.RefreshDashboard)
        {
            _ = RefreshAsync();
        }
    }

    private bool RequestExit()
    {
        _exitRequested = true;
        return true;
    }

    private Element BuildShell()
    {
        _statusText = BodyLabel("等待刷新");
        _healthText = ValueLabel("未知");
        _providerText = ValueLabel("未知");
        _modelText = ValueLabel("未知");
        _overviewRoute = new StackPanel().Spacing(10);
        _overviewProviders = new StackPanel().Spacing(10);
        _overviewModels = new StackPanel().Spacing(10);
        _overviewDirectories = new StackPanel().Spacing(10);
        _overviewRecentRequest = new StackPanel().Spacing(10);
        _overviewHealth = new StackPanel().Spacing(10);
        _overviewCopilotHint = new StackPanel().Spacing(10);
        _providerRows = new StackPanel().Spacing(10);
        _providerEditor = new StackPanel().Spacing(10);
        _providerPresets = new StackPanel().Spacing(10);
        _providerImportPreview = new StackPanel().Spacing(10);
        _providerImportText = new TextBox().Text(string.Empty);
        _modelComparison = new StackPanel().Spacing(10);
        _vscodeDirectories = new StackPanel().Spacing(10);
        _vscodePreview = new StackPanel().Spacing(10);
        _vscodeBackups = new StackPanel().Spacing(10);
        _analyticsSummary = new StackPanel().Spacing(10);
        _analyticsRequests = new StackPanel().Spacing(10);
        _vs2026Panel = new StackPanel().Spacing(10);

        _tabs = new TabControl()
            .AutoVerticalScroll()
            .TabItems(
                new TabItem().Header("概览", accessKey: false).Content(OverviewTab()),
                new TabItem().Header("供应商", accessKey: false).Content(ProvidersTab()),
                new TabItem().Header("VS Code", accessKey: false).Content(VsCodeTab()),
                new TabItem().Header("分析", accessKey: false).Content(AnalyticsTab()),
                new TabItem().Header("VS2026", accessKey: false).Content(Vs2026Tab()));

        return new DockPanel()
            .Padding(18)
            .Spacing(16)
            .Children(
                Header().DockTop(),
                new Grid()
                    .Columns("*,*,*")
                    .Spacing(14)
                    .Children(
                        MetricCard("代理状态", _healthText).Column(0),
                        MetricCard("当前供应商", _providerText).Column(1),
                        MetricCard("当前模型", _modelText).Column(2))
                    .DockTop(),
                _tabs.DockTop());
    }

    private Element Header()
        => new DockPanel()
            .LastChildFill(true)
            .Children(
                new StackPanel()
                    .Spacing(4)
                    .Children(
                        new Label().Text("VSCopilotSwitch").FontSize(26).Bold(),
                        BodyLabel("MewUI 原生工作台，内置本地 Ollama / OpenAI-compatible API。"))
                    .DockLeft(),
                new StackPanel()
                    .Horizontal()
                    .Spacing(8)
                    .Right()
                    .CenterVertical()
                    .Children(
                        new Button().Content("刷新").Padding(18, 8).OnClick(() => _ = RefreshAsync()),
                        new Button().Content("打开本地 API").Padding(18, 8).OnClick(OpenCurrentWebUi),
                        _statusText));

    private Element ProvidersTab()
        => new Grid()
            .Columns("2*,*")
            .Spacing(14)
            .Children(
                Panel("供应商列表", _providerRows).Column(0),
                new StackPanel()
                    .Spacing(14)
                    .Children(
                        Panel("预设模板", _providerPresets),
                        Panel("安全导入预览", new StackPanel()
                            .Spacing(10)
                            .Children(
                                BodyLabel("粘贴供应商导出 JSON 或简单对象/数组。导入预览不会保存密钥，应用后 API Key 字段保持为空。"),
                                _providerImportText,
                                new StackPanel()
                                    .Horizontal()
                                    .Spacing(8)
                                    .Children(
                                        new Button().Content("解析预览").Padding(14, 7).OnClick(ParseProviderImportPreview),
                                        new Button().Content("清空导入").Padding(14, 7).OnClick(ClearProviderImportPreview)),
                                _providerImportPreview)),
                        Panel("模型测试比较", _modelComparison),
                        Panel("新增或编辑", _providerEditor))
                    .Column(1));

    private Element VsCodeTab()
        => new StackPanel()
            .Spacing(14)
            .Children(
                Panel("目标目录", _vscodeDirectories),
                Panel("差异预览与写入", _vscodePreview),
                Panel("备份与回滚", _vscodeBackups));

    private Element AnalyticsTab()
        => new StackPanel()
            .Spacing(14)
            .Children(
                Panel("汇总", _analyticsSummary),
                Panel("最近请求", _analyticsRequests));

    private Element Vs2026Tab()
        => Panel("Azure BYOM 填写信息", _vs2026Panel);

    private async Task RefreshAsync()
    {
        SetStatus("读取中...");

        try
        {
            var dashboardTask = LoadDashboardAsync();
            var analyticsTask = _api.GetJsonAsync<RequestAnalyticsSnapshot>("/internal/analytics", MewUiJsonContext.Default.RequestAnalyticsSnapshot);
            var vs2026Task = _api.GetJsonAsync<Vs2026ByomInfoResponse>("/internal/vs2026/byom", MewUiJsonContext.Default.Vs2026ByomInfoResponse);

            await Task.WhenAll(dashboardTask, analyticsTask, vs2026Task);

            var dashboard = await dashboardTask;
            var analytics = await analyticsTask;
            var vs2026 = await vs2026Task;
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                ApplyDashboard(dashboard, analytics);
                ApplyAnalytics(analytics);
                ApplyVs2026(vs2026);
            });
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                SetStatus("读取失败");
                _healthText.Text = "无法连接";
                ReplaceChildren(_overviewProviders, ErrorLabel($"读取本地 API 失败：{ex.Message}"));
                ReplaceChildren(_overviewModels, BodyLabel("内置本地代理启动失败或端口被占用，请检查 127.0.0.1:5124。"));
                ReplaceChildren(_overviewDirectories, BodyLabel(_api.BaseAddress?.AbsoluteUri ?? "未配置服务地址"));
                ReplaceChildren(_overviewHealth, ErrorLabel($"/health 不可达：{ex.Message}"));
            });
        }
    }

    private async Task<DashboardSnapshot> LoadDashboardAsync()
    {
        var healthTask = _api.GetJsonAsync<HealthResponse>("/health", MewUiJsonContext.Default.HealthResponse);
        var providersTask = _api.GetJsonAsync<IReadOnlyList<DashboardProviderConfigView>>("/internal/providers", MewUiJsonContext.Default.IReadOnlyListDashboardProviderConfigView);
        var tagsTask = _api.GetJsonAsync<DashboardTagsResponse>("/api/tags", MewUiJsonContext.Default.DashboardTagsResponse);
        var directoriesTask = _api.GetJsonAsync<IReadOnlyList<DashboardVsCodeUserDirectory>>("/internal/vscode/user-directories", MewUiJsonContext.Default.IReadOnlyListDashboardVsCodeUserDirectory);

        await Task.WhenAll(healthTask, providersTask, tagsTask, directoriesTask);

        return new DashboardSnapshot(
            await healthTask,
            await providersTask,
            await tagsTask,
            await directoriesTask);
    }

    private void ApplyDashboard(DashboardSnapshot dashboard, RequestAnalyticsSnapshot? analytics = null)
    {
        _dashboard = dashboard;
        _directories = dashboard.Directories;
        _selectedVsCodeDirectory ??= dashboard.Directories.FirstOrDefault(directory => directory.Exists)?.Path
            ?? dashboard.Directories.FirstOrDefault()?.Path;

        SetStatus($"已刷新 {DateTime.Now:HH:mm:ss}");

        var activeProvider = dashboard.Providers.FirstOrDefault(provider => provider.Active);
        var model = dashboard.Tags.Models.FirstOrDefault();

        _healthText.Text = $"{dashboard.Health.Status} / {dashboard.Health.Mode}";
        _providerText.Text = activeProvider?.Name ?? "未启用";
        _modelText.Text = model?.Name ?? activeProvider?.Model ?? "未发现";

        ApplyOverview(dashboard, analytics);
        ReplaceChildren(_providerRows, BuildProviderRows(dashboard.Providers, includeActions: true));
        RenderProviderPresets();
        RenderProviderImportPreview(Array.Empty<ProviderImportPreview>(), "尚未解析导入 JSON。");
        RenderModelComparison(Array.Empty<ModelComparisonResult>());
        RenderProviderEditor();
        RenderVsCodeDirectories();
        RenderVsCodePreview(null);
        _ = RefreshBackupsAsync();
    }
}
