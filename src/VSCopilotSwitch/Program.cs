using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Microsoft.Extensions.DependencyInjection;
using VSCopilotSwitch.Core.Providers;
using VSCopilotSwitch.Services;
using VSCopilotSwitch.VsCodeConfig.Models;

namespace VSCopilotSwitch;

internal static class Program
{
    private static readonly HttpClient Http = new();
    private static readonly JsonSerializerOptions HttpJsonOptions = new()
    {
        TypeInfoResolver = MewUiJsonContext.Default,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static VSCopilotSwitchNativeHost? _nativeHost;
    private static Win32TrayIcon? _trayIcon;
    private static bool _exitRequested;
    private static Label _statusText = null!;
    private static Label _healthText = null!;
    private static Label _providerText = null!;
    private static Label _modelText = null!;
    private static TabControl _tabs = null!;
    private static StackPanel _overviewProviders = null!;
    private static StackPanel _overviewModels = null!;
    private static StackPanel _overviewDirectories = null!;
    private static StackPanel _providerRows = null!;
    private static StackPanel _providerEditor = null!;
    private static StackPanel _vscodeDirectories = null!;
    private static StackPanel _vscodePreview = null!;
    private static StackPanel _vscodeBackups = null!;
    private static StackPanel _analyticsSummary = null!;
    private static StackPanel _analyticsRequests = null!;
    private static StackPanel _vs2026Panel = null!;

    private static DashboardSnapshot? _dashboard;
    private static IReadOnlyList<DashboardVsCodeUserDirectory> _directories = Array.Empty<DashboardVsCodeUserDirectory>();
    private static string? _selectedVsCodeDirectory;
    private static VsCodeConfigApplyResult? _lastVsCodePreview;
    private static VsCodePreviewKind _lastVsCodePreviewKind;
    private static ProviderEditorState _providerEditorState = ProviderEditorState.CreateNew();
    private static string? _pendingDeleteProviderId;
    private static string? _pendingRestoreBackupPath;

    [STAThread]
    private static void Main(string[] args)
    {
        Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
        Thread.CurrentThread.SetApartmentState(ApartmentState.STA);

        Win32Platform.Register();
        GdiBackend.Register();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                StartupDiagnostics.WriteCrashLog(ex);
                NativeMessageBox.Show(ex.ToString(), "VSCopilotSwitch fatal error", NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Error);
            }
        };

        Application.DispatcherUnhandledException += e =>
        {
            StartupDiagnostics.WriteCrashLog(e.Exception, "MewUI dispatcher error.");
            NativeMessageBox.Show(e.Exception.ToString(), "VSCopilotSwitch UI error", NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Error);
            e.Handled = true;
        };

        try
        {
            _nativeHost = VSCopilotSwitchNativeHost.StartAsync(args).GetAwaiter().GetResult();
            Http.BaseAddress = new Uri(_nativeHost.ServerUrl.TrimEnd('/') + "/");

            Application.Create()
                .UseAccent(Accent.Blue)
                .BuildMainWindow(CreateWindow)
                .Run();
        }
        finally
        {
            _trayIcon?.Dispose();
            _nativeHost?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static Window CreateWindow()
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

    private static void InitializeTray(Window window)
    {
        if (_trayIcon is not null || _nativeHost is null)
        {
            return;
        }

        try
        {
            _trayIcon = new Win32TrayIcon(
                window,
                _nativeHost.Services.GetRequiredService<ITrayMenuService>(),
                RequestExit);
            _trayIcon.Initialize();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteCrashLog(ex, "Initialize tray icon failed.");
        }
    }

    private static bool RequestExit()
    {
        _exitRequested = true;
        return true;
    }

    private static Element BuildShell()
    {
        _statusText = BodyLabel("等待刷新");
        _healthText = ValueLabel("未知");
        _providerText = ValueLabel("未知");
        _modelText = ValueLabel("未知");
        _overviewProviders = new StackPanel().Spacing(10);
        _overviewModels = new StackPanel().Spacing(10);
        _overviewDirectories = new StackPanel().Spacing(10);
        _providerRows = new StackPanel().Spacing(10);
        _providerEditor = new StackPanel().Spacing(10);
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

    private static Element Header()
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

    private static Element OverviewTab()
        => new StackPanel()
            .Spacing(14)
            .Children(
                Panel("供应商", _overviewProviders),
                Panel("模型", _overviewModels),
                Panel("VS Code 配置目录", _overviewDirectories),
                Panel("闭环说明",
                    new StackPanel()
                        .Spacing(6)
                        .Children(
                            BodyLabel("供应商、VS Code 写入、分析统计和 VS2026 信息均通过本进程 /internal API 完成。"),
                            BodyLabel("VS Code 写入仍然需要先生成 dry-run 差异，再点击确认写入；恢复备份前会先为当前文件创建安全备份。"))));

    private static Element ProvidersTab()
        => new Grid()
            .Columns("2*,*")
            .Spacing(14)
            .Children(
                Panel("供应商列表", _providerRows).Column(0),
                Panel("新增或编辑", _providerEditor).Column(1));

    private static Element VsCodeTab()
        => new StackPanel()
            .Spacing(14)
            .Children(
                Panel("目标目录", _vscodeDirectories),
                Panel("差异预览与写入", _vscodePreview),
                Panel("备份与回滚", _vscodeBackups));

    private static Element AnalyticsTab()
        => new StackPanel()
            .Spacing(14)
            .Children(
                Panel("汇总", _analyticsSummary),
                Panel("最近请求", _analyticsRequests));

    private static Element Vs2026Tab()
        => Panel("Azure BYOM 填写信息", _vs2026Panel);

    private static Element MetricCard(string title, Label value)
        => new GroupBox()
            .Header(title, accessKey: false)
            .Padding(14)
            .Content(
                new StackPanel()
                    .Spacing(5)
                    .Children(value));

    private static Element Panel(string title, Element content)
        => new GroupBox()
            .Header(title, accessKey: false)
            .Padding(14)
            .Content(content);

    private static async Task RefreshAsync()
    {
        SetStatus("读取中...");

        try
        {
            var dashboardTask = LoadDashboardAsync();
            var analyticsTask = GetJsonAsync<RequestAnalyticsSnapshot>("/internal/analytics", MewUiJsonContext.Default.RequestAnalyticsSnapshot);
            var vs2026Task = GetJsonAsync<Vs2026ByomInfoResponse>("/internal/vs2026/byom", MewUiJsonContext.Default.Vs2026ByomInfoResponse);

            await Task.WhenAll(dashboardTask, analyticsTask, vs2026Task);

            var dashboard = await dashboardTask;
            var analytics = await analyticsTask;
            var vs2026 = await vs2026Task;
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                ApplyDashboard(dashboard);
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
                ReplaceChildren(_overviewDirectories, BodyLabel(Http.BaseAddress?.AbsoluteUri ?? "未配置服务地址"));
            });
        }
    }

    private static async Task<DashboardSnapshot> LoadDashboardAsync()
    {
        var healthTask = GetJsonAsync<HealthResponse>("/health", MewUiJsonContext.Default.HealthResponse);
        var providersTask = GetJsonAsync<IReadOnlyList<DashboardProviderConfigView>>("/internal/providers", MewUiJsonContext.Default.IReadOnlyListDashboardProviderConfigView);
        var tagsTask = GetJsonAsync<DashboardTagsResponse>("/api/tags", MewUiJsonContext.Default.DashboardTagsResponse);
        var directoriesTask = GetJsonAsync<IReadOnlyList<DashboardVsCodeUserDirectory>>("/internal/vscode/user-directories", MewUiJsonContext.Default.IReadOnlyListDashboardVsCodeUserDirectory);

        await Task.WhenAll(healthTask, providersTask, tagsTask, directoriesTask);

        return new DashboardSnapshot(
            await healthTask,
            await providersTask,
            await tagsTask,
            await directoriesTask);
    }

    private static async Task<T> GetJsonAsync<T>(string path, JsonTypeInfo<T> jsonTypeInfo)
    {
        using var response = await Http.GetAsync(path);
        await EnsureSuccessAsync(response, path);

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo)
            ?? throw new InvalidOperationException($"接口 {path} 返回空响应。");
    }

    private static async Task<T> PostJsonAsync<TRequest, T>(string path, TRequest request, JsonTypeInfo<T> jsonTypeInfo)
    {
        using var content = JsonContent.Create(request, options: HttpJsonOptions);
        using var response = await Http.PostAsync(path, content);
        await EnsureSuccessAsync(response, path);

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo)
            ?? throw new InvalidOperationException($"接口 {path} 返回空响应。");
    }

    private static async Task<T> DeleteJsonAsync<T>(string path, JsonTypeInfo<T> jsonTypeInfo)
    {
        using var response = await Http.DeleteAsync(path);
        await EnsureSuccessAsync(response, path);

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo)
            ?? throw new InvalidOperationException($"接口 {path} 返回空响应。");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string path)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var error = TryReadError(body);
        throw new InvalidOperationException($"{path} 返回 {(int)response.StatusCode}：{error}");
    }

    private static string TryReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "无错误正文";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("Error", out var error))
            {
                return error.GetString() ?? body;
            }

            if (document.RootElement.TryGetProperty("error", out var lowerError))
            {
                return lowerError.ValueKind == JsonValueKind.String
                    ? lowerError.GetString() ?? body
                    : lowerError.ToString();
            }
        }
        catch
        {
        }

        return body.Length <= 500 ? body : body[..500] + "...";
    }

    private static void ApplyDashboard(DashboardSnapshot dashboard)
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

        ReplaceChildren(_overviewProviders, BuildProviderRows(dashboard.Providers, includeActions: false));
        ReplaceChildren(_overviewModels, BuildModelRows(dashboard.Tags.Models));
        ReplaceChildren(_overviewDirectories, BuildDirectoryRows(dashboard.Directories, selectable: false));
        ReplaceChildren(_providerRows, BuildProviderRows(dashboard.Providers, includeActions: true));
        RenderProviderEditor();
        RenderVsCodeDirectories();
        RenderVsCodePreview(null);
        _ = RefreshBackupsAsync();
    }

    private static void ApplyAnalytics(RequestAnalyticsSnapshot snapshot)
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

    private static void ApplyVs2026(Vs2026ByomInfoResponse info)
    {
        var endpoint = Empty(info.Endpoint, "未启用 HTTPS");
        var validateUrl = info.Endpoint is null ? "未启用 HTTPS" : $"{info.Endpoint.TrimEnd('/')}/models/{info.ModelId}";
        var chatUrl = info.Endpoint is null ? "未启用 HTTPS" : $"{info.Endpoint.TrimEnd('/')}/chat/completions";
        ReplaceChildren(
            _vs2026Panel,
            Row("Provider", "Azure"),
            Row("Resource Endpoint / Custom URL", endpoint),
            Row("Model ID", info.ModelId),
            Row("API Key", info.ApiKeyPlaceholder),
            Row("模型校验 URL", validateUrl),
            Row("聊天 URL", chatUrl),
            Row("HTTPS 状态", info.HttpsEnabled ? "已启用" : "未启用"),
            BodyLabel(info.Message),
            new StackPanel()
                .Horizontal()
                .Spacing(8)
                .Children(
                    new Button().Content("复制全部").Padding(16, 8).OnClick(() => CopyVs2026Info(info, validateUrl, chatUrl)),
                    new Button().Content("刷新 VS2026").Padding(16, 8).OnClick(() => _ = RefreshVs2026Async())));
    }

    private static Element[] BuildProviderRows(IReadOnlyList<DashboardProviderConfigView> providers, bool includeActions)
    {
        if (providers.Count == 0)
        {
            return [BodyLabel("还没有供应商配置。")];
        }

        return providers
            .Select((provider, index) => ProviderRow(provider, includeActions, index, providers.Count))
            .Cast<Element>()
            .ToArray();
    }

    private static Element ProviderRow(DashboardProviderConfigView provider, bool includeActions, int index, int count)
    {
        var title = provider.Active ? $"{provider.Name}  [当前]" : provider.Name;
        var subtitle = $"{provider.Vendor} / {Empty(provider.Model, "未设置模型")} / 密钥：{(provider.HasApiKey ? provider.ApiKeyPreview ?? "已保存" : "未保存")} / {provider.ApiUrl}";
        var text = new StackPanel()
            .Spacing(3)
            .Children(
                new Label().Text(title).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis),
                BodyLabel(subtitle).TextWrapping(TextWrapping.Wrap));

        if (!includeActions)
        {
            return new DockPanel().Padding(8, 6).Children(text);
        }

        return new DockPanel()
            .Padding(8, 6)
            .Children(
                text.DockLeft(),
                new StackPanel()
                    .Horizontal()
                    .Spacing(6)
                    .Right()
                    .CenterVertical()
                    .Children(
                        new Button().Content("编辑").Padding(12, 6).OnClick(() => EditProvider(provider)),
                        new Button().Content("测试连接").Padding(12, 6).OnClick(() => _ = TestProviderAsync(provider)),
                        new Button().Content("上移").Padding(12, 6).OnClick(() => _ = MoveProviderAsync(provider, -1)).IsEnabled(index > 0),
                        new Button().Content("下移").Padding(12, 6).OnClick(() => _ = MoveProviderAsync(provider, 1)).IsEnabled(index < count - 1),
                        new Button().Content("启用").Padding(12, 6).OnClick(() => _ = ActivateProviderAsync(provider)).IsEnabled(!provider.Active),
                        new Button()
                            .Content(string.Equals(_pendingDeleteProviderId, provider.Id, StringComparison.OrdinalIgnoreCase) ? "再次点击删除" : "删除")
                            .Padding(12, 6)
                            .OnClick(() => _ = DeleteProviderAsync(provider))).DockRight());
    }

    private static Element[] BuildModelRows(IReadOnlyList<DashboardModelInfo> models)
    {
        if (models.Count == 0)
        {
            return [BodyLabel("当前供应商未返回模型列表。")];
        }

        return models
            .Take(16)
            .Select(model => Row(model.Name, $"{model.Details?.Family ?? "provider"} / {model.Details?.ParentModel ?? model.Model}"))
            .Cast<Element>()
            .Append(BodyLabel(models.Count > 16 ? $"另有 {models.Count - 16} 个模型未显示。" : string.Empty))
            .Where(element => element is not Label label || !string.IsNullOrWhiteSpace(label.Text))
            .ToArray();
    }

    private static Element[] BuildDirectoryRows(IReadOnlyList<DashboardVsCodeUserDirectory> directories, bool selectable)
    {
        if (directories.Count == 0)
        {
            return [BodyLabel("没有发现 VS Code User 配置目录。")];
        }

        return directories
            .Select(directory =>
            {
                var title = selectable && string.Equals(directory.Path, _selectedVsCodeDirectory, StringComparison.OrdinalIgnoreCase)
                    ? $"{directory.Description}  [已选择]"
                    : directory.Description;
                var subtitle = $"{(directory.Exists ? "存在" : "可创建")} / {directory.Path}";
                if (!selectable)
                {
                    return Row(title, subtitle);
                }

                return new DockPanel()
                    .Padding(8, 6)
                    .Children(
                        new StackPanel()
                            .Spacing(3)
                            .Children(
                                new Label().Text(title).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis),
                                BodyLabel(subtitle).TextWrapping(TextWrapping.Wrap))
                            .DockLeft(),
                        new Button()
                            .Content("选择")
                            .Padding(14, 6)
                            .OnClick(() => SelectVsCodeDirectory(directory.Path))
                            .DockRight());
            })
            .Cast<Element>()
            .ToArray();
    }

    private static void RenderProviderEditor()
    {
        ReplaceChildren(
            _providerEditor,
            Field("名称", _providerEditorState.Name, value => _providerEditorState = _providerEditorState with { Name = value }),
            Field("备注", _providerEditorState.Remark, value => _providerEditorState = _providerEditorState with { Remark = value }),
            Field("官网 URL", _providerEditorState.Url, value => _providerEditorState = _providerEditorState with { Url = value }),
            Field("API URL", _providerEditorState.ApiUrl, value => _providerEditorState = _providerEditorState with { ApiUrl = value }),
            Field("模型", _providerEditorState.Model, value => _providerEditorState = _providerEditorState with { Model = value }),
            Field("协议类型", _providerEditorState.Vendor, value => _providerEditorState = _providerEditorState with { Vendor = value }),
            SecretField("API Key", value => _providerEditorState = _providerEditorState with { ApiKey = value }),
            new StackPanel()
                .Horizontal()
                .Spacing(8)
                .Children(
                    new Button().Content(_providerEditorState.IsNew ? "新增供应商" : "保存供应商").Padding(16, 8).OnClick(() => _ = SaveProviderAsync()),
                    new Button().Content("测试表单").Padding(16, 8).OnClick(() => _ = TestProviderFormAsync()),
                    new Button().Content("清空表单").Padding(16, 8).OnClick(NewProvider)));
    }

    private static Element Field(string label, string value, Action<string> onChanged)
    {
        var textBox = new TextBox()
            .Text(value)
            .OnTextChanged(onChanged);

        return new StackPanel()
            .Spacing(4)
            .Children(
                BodyLabel(label),
                textBox);
    }

    private static Element SecretField(string label, Action<string> onChanged)
    {
        var password = new PasswordBox()
            .OnTextChanged(onChanged);

        return new StackPanel()
            .Spacing(4)
            .Children(
                BodyLabel(label),
                password,
                BodyLabel("留空保存时会沿用已有密钥；新密钥只发送给后端加密保存，不会显示在界面列表中。"));
    }

    private static void RenderVsCodeDirectories()
    {
        ReplaceChildren(
            _vscodeDirectories,
            BuildDirectoryRows(_directories, selectable: true)
                .Append(new StackPanel()
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("生成 dry-run 预览").Padding(16, 8).OnClick(() => _ = PreviewVsCodeApplyAsync()),
                        new Button().Content("检查状态").Padding(16, 8).OnClick(() => _ = CheckVsCodeStatusAsync()),
                        new Button().Content("刷新备份").Padding(16, 8).OnClick(() => _ = RefreshBackupsAsync())))
                .ToArray());
    }

    private static void RenderVsCodePreview(VsCodeConfigApplyResult? result)
    {
        if (result is null)
        {
            ReplaceChildren(
                _vscodePreview,
                BodyLabel("先选择 VS Code User 目录，再生成 dry-run 差异预览。"),
                BodyLabel("写入只会维护 chatLanguageModels.json 中 name=vscs、vendor=ollama 的 Provider 条目。"));
            return;
        }

        var changes = result.Changes.SelectMany(change =>
            change.FieldChanges.Select(field => Row(
                $"{Path.GetFileName(change.FilePath)} / {field.Path}",
                $"{(field.Changed ? "将变更" : "无变化")}：{ShortValue(field.BeforeValue)} -> {ShortValue(field.AfterValue)}")))
            .Cast<Element>()
            .ToList();

        changes.Insert(0, Row("目标目录", result.UserDirectory));
        changes.Add(new StackPanel()
            .Horizontal()
            .Spacing(8)
            .Children(
                new Button().Content("确认写入 VS Code Ollama 配置").Padding(16, 8).OnClick(() => _ = ApplyVsCodeConfigAsync()).IsEnabled(result.Changes.Any(change => change.Changed)),
                new Button().Content("dry-run 撤销预览").Padding(16, 8).OnClick(() => _ = PreviewVsCodeRemoveAsync()),
                new Button().Content("确认撤销 vscs Provider").Padding(16, 8).OnClick(() => _ = RemoveVsCodeConfigAsync())));

        ReplaceChildren(_vscodePreview, changes.ToArray());
    }

    private static async Task RefreshBackupsAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedVsCodeDirectory))
        {
            ReplaceChildren(_vscodeBackups, BodyLabel("尚未选择 VS Code User 目录。"));
            return;
        }

        try
        {
            var backups = await PostJsonAsync<ListVsCodeConfigBackupsRequest, IReadOnlyList<VsCodeConfigBackup>>(
                "/internal/vscode/backups",
                new ListVsCodeConfigBackupsRequest(_selectedVsCodeDirectory),
                MewUiJsonContext.Default.IReadOnlyListVsCodeConfigBackup);

            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                if (backups.Count == 0)
                {
                    ReplaceChildren(_vscodeBackups, BodyLabel("当前目录还没有 VSCopilotSwitch 备份。"));
                    return;
                }

                ReplaceChildren(
                    _vscodeBackups,
                    backups
                        .Take(10)
                        .Select(backup => BackupRow(backup))
                        .Cast<Element>()
                        .ToArray());
            });
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher?.BeginInvoke(() =>
                ReplaceChildren(_vscodeBackups, ErrorLabel($"读取备份失败：{ex.Message}")));
        }
    }

    private static Element BackupRow(VsCodeConfigBackup backup)
        => new DockPanel()
            .Padding(8, 6)
            .Children(
                new StackPanel()
                    .Spacing(3)
                    .Children(
                        new Label().Text($"{backup.FileName}  {backup.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}").SemiBold(),
                        BodyLabel($"{backup.SizeBytes} bytes / {backup.BackupPath}").TextWrapping(TextWrapping.Wrap))
                    .DockLeft(),
                new Button()
                    .Content(string.Equals(_pendingRestoreBackupPath, backup.BackupPath, StringComparison.OrdinalIgnoreCase)
                        ? "再次点击恢复"
                        : "恢复此备份")
                    .Padding(14, 6)
                    .OnClick(() => _ = RestoreBackupAsync(backup))
                    .DockRight());

    private static void SelectVsCodeDirectory(string path)
    {
        _selectedVsCodeDirectory = path;
        _lastVsCodePreview = null;
        _lastVsCodePreviewKind = VsCodePreviewKind.None;
        RenderVsCodeDirectories();
        RenderVsCodePreview(null);
        _ = RefreshBackupsAsync();
        SetStatus("已选择 VS Code 目录");
    }

    private static void EditProvider(DashboardProviderConfigView provider)
    {
        _providerEditorState = new ProviderEditorState(
            provider.Id,
            provider.Name,
            provider.Remark,
            provider.Url,
            provider.ApiUrl,
            provider.Model,
            provider.Vendor,
            ApiKey: string.Empty,
            provider.Active,
            IsNew: false);
        _pendingDeleteProviderId = null;
        RenderProviderEditor();
        if (_dashboard is not null)
        {
            ReplaceChildren(_providerRows, BuildProviderRows(_dashboard.Providers, includeActions: true));
        }

        SetStatus($"正在编辑 {provider.Name}");
    }

    private static void NewProvider()
    {
        _providerEditorState = ProviderEditorState.CreateNew();
        _pendingDeleteProviderId = null;
        RenderProviderEditor();
        if (_dashboard is not null)
        {
            ReplaceChildren(_providerRows, BuildProviderRows(_dashboard.Providers, includeActions: true));
        }

        SetStatus("已清空供应商表单");
    }

    private static async Task SaveProviderAsync()
    {
        try
        {
            SetStatus("保存供应商...");
            var providers = await PostJsonAsync<SaveProviderConfigRequest, IReadOnlyList<DashboardProviderConfigView>>(
                "/internal/providers",
                _providerEditorState.ToSaveRequest(),
                MewUiJsonContext.Default.IReadOnlyListDashboardProviderConfigView);
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                _providerEditorState = ProviderEditorState.CreateNew();
                if (_dashboard is not null)
                {
                    ApplyDashboard(_dashboard with { Providers = providers });
                }

                SetStatus("供应商已保存");
            });
            _ = RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError("保存供应商失败", ex);
        }
    }

    private static async Task TestProviderFormAsync()
    {
        await TestProviderAsync(_providerEditorState.ToTestRequest(), "表单连接测试");
    }

    private static async Task TestProviderAsync(DashboardProviderConfigView provider)
    {
        await TestProviderAsync(
            new TestProviderConnectionRequest(provider.Id, provider.Name, provider.ApiUrl, provider.Model, provider.Vendor, null),
            $"{provider.Name} 连接测试");
    }

    private static async Task TestProviderAsync(TestProviderConnectionRequest request, string title)
    {
        try
        {
            SetStatus("测试连接...");
            var result = await PostJsonAsync<TestProviderConnectionRequest, ProviderConnectionTestResult>(
                "/internal/providers/test-connection",
                request,
                MewUiJsonContext.Default.ProviderConnectionTestResult);
            var detail = string.Join(
                Environment.NewLine,
                result.Steps.Select(step => $"{(step.Success ? "通过" : "失败")} {step.Label}: {step.Message}"));
            NativeMessageBox.Show(
                $"{(result.Success ? "连接测试通过。" : "连接测试失败。")}{Environment.NewLine}{detail}",
                title,
                NativeMessageBoxButtons.Ok,
                result.Success ? NativeMessageBoxIcon.Information : NativeMessageBoxIcon.Warning);
            SetStatus(result.Success ? "连接测试通过" : "连接测试失败");
        }
        catch (Exception ex)
        {
            ShowError("测试连接失败", ex);
        }
    }

    private static async Task MoveProviderAsync(DashboardProviderConfigView provider, int delta)
    {
        if (_dashboard is null)
        {
            return;
        }

        var providers = _dashboard.Providers.ToList();
        var index = providers.FindIndex(item => string.Equals(item.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
        var targetIndex = index + delta;
        if (index < 0 || targetIndex < 0 || targetIndex >= providers.Count)
        {
            return;
        }

        providers.RemoveAt(index);
        providers.Insert(targetIndex, provider);

        try
        {
            SetStatus("调整供应商排序...");
            var sorted = await PostJsonAsync<ReorderProvidersRequest, IReadOnlyList<DashboardProviderConfigView>>(
                "/internal/providers/reorder",
                new ReorderProvidersRequest(providers.Select(item => item.Id).ToArray()),
                MewUiJsonContext.Default.IReadOnlyListDashboardProviderConfigView);
            _pendingDeleteProviderId = null;
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                if (_dashboard is not null)
                {
                    ApplyDashboard(_dashboard with { Providers = sorted });
                }

                SetStatus("供应商排序已更新");
            });
        }
        catch (Exception ex)
        {
            ShowError("调整供应商排序失败", ex);
        }
    }

    private static async Task ActivateProviderAsync(DashboardProviderConfigView provider)
    {
        try
        {
            SetStatus("切换供应商...");
            await PostJsonAsync<object, IReadOnlyList<DashboardProviderConfigView>>(
                $"/internal/providers/{Uri.EscapeDataString(provider.Id)}/activate",
                new { },
                MewUiJsonContext.Default.IReadOnlyListDashboardProviderConfigView);
            await RefreshAsync();
            SetStatus($"已启用 {provider.Name}");
        }
        catch (Exception ex)
        {
            ShowError("启用供应商失败", ex);
        }
    }

    private static async Task DeleteProviderAsync(DashboardProviderConfigView provider)
    {
        if (!string.Equals(_pendingDeleteProviderId, provider.Id, StringComparison.OrdinalIgnoreCase))
        {
            _pendingDeleteProviderId = provider.Id;
            if (_dashboard is not null)
            {
                ReplaceChildren(_providerRows, BuildProviderRows(_dashboard.Providers, includeActions: true));
            }

            SetStatus($"再次点击删除以确认移除 {provider.Name}");
            return;
        }

        try
        {
            SetStatus("删除供应商...");
            await DeleteJsonAsync<IReadOnlyList<DashboardProviderConfigView>>(
                $"/internal/providers/{Uri.EscapeDataString(provider.Id)}",
                MewUiJsonContext.Default.IReadOnlyListDashboardProviderConfigView);
            _pendingDeleteProviderId = null;
            await RefreshAsync();
            SetStatus("供应商已删除");
        }
        catch (Exception ex)
        {
            ShowError("删除供应商失败", ex);
        }
    }

    private static async Task PreviewVsCodeApplyAsync()
    {
        if (!EnsureVsCodeDirectorySelected())
        {
            return;
        }

        try
        {
            SetStatus("生成 VS Code dry-run...");
            var result = await PostJsonAsync<ApplyVsCodeOllamaConfigRequest, VsCodeConfigApplyResult>(
                "/internal/vscode/apply-ollama",
                new ApplyVsCodeOllamaConfigRequest(_selectedVsCodeDirectory!, null, DryRun: true),
                MewUiJsonContext.Default.VsCodeConfigApplyResult);
            _lastVsCodePreview = result;
            _lastVsCodePreviewKind = VsCodePreviewKind.Apply;
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                RenderVsCodePreview(result);
                SetStatus("dry-run 预览已生成");
            });
        }
        catch (Exception ex)
        {
            ShowError("生成 VS Code 预览失败", ex);
        }
    }

    private static async Task ApplyVsCodeConfigAsync()
    {
        if (!EnsureVsCodeDirectorySelected())
        {
            return;
        }

        if (_lastVsCodePreview is null || _lastVsCodePreviewKind != VsCodePreviewKind.Apply)
        {
            NativeMessageBox.Show("请先生成写入 dry-run 差异预览，再确认写入。", "需要预览", NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Warning);
            return;
        }

        try
        {
            SetStatus("写入 VS Code 配置...");
            var result = await PostJsonAsync<ApplyVsCodeOllamaConfigRequest, VsCodeConfigApplyResult>(
                "/internal/vscode/apply-ollama",
                new ApplyVsCodeOllamaConfigRequest(_selectedVsCodeDirectory!, null, DryRun: false),
                MewUiJsonContext.Default.VsCodeConfigApplyResult);
            _lastVsCodePreview = result;
            _lastVsCodePreviewKind = VsCodePreviewKind.Apply;
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                RenderVsCodePreview(result);
                SetStatus("VS Code 配置已写入");
            });
            await RefreshBackupsAsync();
        }
        catch (Exception ex)
        {
            ShowError("写入 VS Code 配置失败", ex);
        }
    }

    private static async Task PreviewVsCodeRemoveAsync()
    {
        if (!EnsureVsCodeDirectorySelected())
        {
            return;
        }

        try
        {
            SetStatus("生成撤销 dry-run...");
            var result = await PostJsonAsync<RemoveVsCodeOllamaConfigRequest, VsCodeConfigApplyResult>(
                "/internal/vscode/remove-ollama",
                new RemoveVsCodeOllamaConfigRequest(_selectedVsCodeDirectory!, DryRun: true),
                MewUiJsonContext.Default.VsCodeConfigApplyResult);
            _lastVsCodePreview = result;
            _lastVsCodePreviewKind = VsCodePreviewKind.Remove;
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                RenderVsCodePreview(result);
                SetStatus("撤销预览已生成");
            });
        }
        catch (Exception ex)
        {
            ShowError("生成撤销预览失败", ex);
        }
    }

    private static async Task RemoveVsCodeConfigAsync()
    {
        if (!EnsureVsCodeDirectorySelected())
        {
            return;
        }

        if (_lastVsCodePreview is null || _lastVsCodePreviewKind != VsCodePreviewKind.Remove)
        {
            NativeMessageBox.Show("请先生成撤销 dry-run 差异预览，再确认撤销。", "需要预览", NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Warning);
            return;
        }

        try
        {
            SetStatus("撤销 VS Code 配置...");
            var result = await PostJsonAsync<RemoveVsCodeOllamaConfigRequest, VsCodeConfigApplyResult>(
                "/internal/vscode/remove-ollama",
                new RemoveVsCodeOllamaConfigRequest(_selectedVsCodeDirectory!, DryRun: false),
                MewUiJsonContext.Default.VsCodeConfigApplyResult);
            _lastVsCodePreview = result;
            _lastVsCodePreviewKind = VsCodePreviewKind.Remove;
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                RenderVsCodePreview(result);
                SetStatus("VS Code vscs Provider 已撤销");
            });
            await RefreshBackupsAsync();
        }
        catch (Exception ex)
        {
            ShowError("撤销 VS Code 配置失败", ex);
        }
    }

    private static async Task CheckVsCodeStatusAsync()
    {
        if (!EnsureVsCodeDirectorySelected())
        {
            return;
        }

        try
        {
            var status = await PostJsonAsync<VsCodeUserDirectoryRequest, VsCodeOllamaConfigStatus>(
                "/internal/vscode/ollama-status",
                new VsCodeUserDirectoryRequest(_selectedVsCodeDirectory!),
                MewUiJsonContext.Default.VsCodeOllamaConfigStatus);
            NativeMessageBox.Show(status.Message, "VS Code 配置状态", NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Information);
            SetStatus(status.Enabled ? "VS Code 已配置" : "VS Code 未配置");
        }
        catch (Exception ex)
        {
            ShowError("检查 VS Code 配置失败", ex);
        }
    }

    private static async Task RestoreBackupAsync(VsCodeConfigBackup backup)
    {
        if (!EnsureVsCodeDirectorySelected())
        {
            return;
        }

        if (!string.Equals(_pendingRestoreBackupPath, backup.BackupPath, StringComparison.OrdinalIgnoreCase))
        {
            _pendingRestoreBackupPath = backup.BackupPath;
            await RefreshBackupsAsync();
            SetStatus($"再次点击恢复以确认还原 {backup.FileName}");
            return;
        }

        try
        {
            SetStatus("恢复备份...");
            var result = await PostJsonAsync<RestoreVsCodeConfigBackupRequest, VsCodeConfigRestoreResult>(
                "/internal/vscode/restore-backup",
                new RestoreVsCodeConfigBackupRequest(_selectedVsCodeDirectory!, backup.BackupPath),
                MewUiJsonContext.Default.VsCodeConfigRestoreResult);
            _pendingRestoreBackupPath = null;
            NativeMessageBox.Show(
                $"已恢复：{result.FilePath}{Environment.NewLine}安全备份：{result.SafetyBackupPath ?? "当前文件原本不存在"}",
                "恢复完成",
                NativeMessageBoxButtons.Ok,
                NativeMessageBoxIcon.Information);
            await RefreshBackupsAsync();
            SetStatus("备份已恢复");
        }
        catch (Exception ex)
        {
            ShowError("恢复备份失败", ex);
        }
    }

    private static bool EnsureVsCodeDirectorySelected()
    {
        if (!string.IsNullOrWhiteSpace(_selectedVsCodeDirectory))
        {
            return true;
        }

        NativeMessageBox.Show("请先选择 VS Code User 配置目录。", "缺少目标目录", NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Warning);
        return false;
    }

    private static async Task RefreshAnalyticsAsync()
    {
        try
        {
            var snapshot = await GetJsonAsync<RequestAnalyticsSnapshot>("/internal/analytics", MewUiJsonContext.Default.RequestAnalyticsSnapshot);
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

    private static async Task ClearAnalyticsAsync()
    {
        try
        {
            var snapshot = await PostJsonAsync<object, RequestAnalyticsSnapshot>(
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

    private static async Task RunCopilotProbeAsync()
    {
        try
        {
            SetStatus("运行 Copilot 探针...");
            var result = await PostJsonAsync<object, CopilotCompatibilityProbeResult>(
                "/internal/copilot/probe",
                new { },
                MewUiJsonContext.Default.CopilotCompatibilityProbeResult);
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

    private static async Task RefreshVs2026Async()
    {
        try
        {
            var info = await GetJsonAsync<Vs2026ByomInfoResponse>("/internal/vs2026/byom", MewUiJsonContext.Default.Vs2026ByomInfoResponse);
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                ApplyVs2026(info);
                SetStatus("VS2026 信息已刷新");
            });
        }
        catch (Exception ex)
        {
            ShowError("刷新 VS2026 信息失败", ex);
        }
    }

    private static void CopyVs2026Info(Vs2026ByomInfoResponse info, string validateUrl, string chatUrl)
    {
        var text = string.Join(Environment.NewLine, new[]
        {
            "Provider: Azure",
            $"Resource Endpoint / Custom URL: {Empty(info.Endpoint, "未启用 HTTPS")}",
            $"Model ID: {info.ModelId}",
            $"API Key: {info.ApiKeyPlaceholder}",
            $"Model validate URL: {validateUrl}",
            $"Chat URL: {chatUrl}"
        });
        NativeClipboard.SetText(text);
        SetStatus("VS2026 信息已复制");
    }

    private static Element Row(string title, string subtitle)
        => new DockPanel()
            .Padding(8, 6)
            .Children(
                new StackPanel()
                    .Spacing(3)
                    .Children(
                        new Label().Text(title).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis),
                        BodyLabel(subtitle).TextWrapping(TextWrapping.Wrap)));

    private static Label BodyLabel(string text)
        => new Label()
            .Text(text)
            .TextWrapping(TextWrapping.Wrap);

    private static Label ValueLabel(string text)
        => new Label()
            .Text(text)
            .FontSize(20)
            .SemiBold()
            .TextTrimming(TextTrimming.CharacterEllipsis);

    private static Label ErrorLabel(string text)
        => new Label()
            .Text(text)
            .TextWrapping(TextWrapping.Wrap);

    private static void ReplaceChildren(StackPanel panel, params Element[] children)
    {
        panel.Clear();
        panel.AddRange(children);
    }

    private static void SetStatus(string text)
    {
        if (_statusText is not null)
        {
            _statusText.Text = text;
        }
    }

    private static void ShowError(string title, Exception ex)
    {
        Application.Current.Dispatcher?.BeginInvoke(() =>
        {
            SetStatus(title);
            NativeMessageBox.Show(ex.Message, title, NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Error);
        });
    }

    private static void OpenCurrentWebUi()
    {
        var url = Http.BaseAddress?.AbsoluteUri ?? "http://127.0.0.1:5124/";
        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    private static string Empty(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string ShortValue(string value)
    {
        var normalized = value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return normalized.Length <= 160 ? normalized : normalized[..160] + "...";
    }

    private sealed record ProviderEditorState(
        string? Id,
        string Name,
        string Remark,
        string Url,
        string ApiUrl,
        string Model,
        string Vendor,
        string ApiKey,
        bool Active,
        bool IsNew)
    {
        public static ProviderEditorState CreateNew()
            => new(
                null,
                string.Empty,
                string.Empty,
                "https://example.com",
                "https://api.example.com/v1",
                string.Empty,
                "openai-compatible",
                string.Empty,
                Active: true,
                IsNew: true);

        public SaveProviderConfigRequest ToSaveRequest()
            => new(
                Id,
                Name,
                Remark,
                Url,
                ApiUrl,
                Model,
                Vendor,
                string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey,
                Active);

        public TestProviderConnectionRequest ToTestRequest()
            => new(
                Id,
                Name,
                ApiUrl,
                Model,
                Vendor,
                string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey);
    }

    private enum VsCodePreviewKind
    {
        None,
        Apply,
        Remove
    }
}

internal static class NativeClipboard
{
    public static void SetText(string text)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd", "/c clip")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return;
            }

            process.StandardInput.Write(text);
            process.StandardInput.Close();
            process.WaitForExit(2000);
        }
        catch
        {
        }
    }
}

internal sealed record DashboardSnapshot(
    HealthResponse Health,
    IReadOnlyList<DashboardProviderConfigView> Providers,
    DashboardTagsResponse Tags,
    IReadOnlyList<DashboardVsCodeUserDirectory> Directories);

internal sealed record DashboardProviderConfigView(
    string Id,
    string Name,
    string Remark,
    string Url,
    string ApiUrl,
    string Model,
    string Vendor,
    string Avatar,
    bool Active,
    bool HasApiKey,
    string? ApiKeyPreview,
    int SortOrder);

internal sealed record DashboardTagsResponse(IReadOnlyList<DashboardModelInfo> Models);

internal sealed record DashboardModelInfo(
    string Name,
    string Model,
    DateTimeOffset ModifiedAt,
    long Size,
    string Digest,
    DashboardModelDetails? Details);

internal sealed record DashboardModelDetails(
    [property: JsonPropertyName("parent_model")] string? ParentModel,
    string Family,
    [property: JsonPropertyName("parameter_size")] string ParameterSize,
    [property: JsonPropertyName("quantization_level")] string QuantizationLevel);

internal sealed record DashboardVsCodeUserDirectory(
    string Path,
    string Profile,
    bool Exists,
    string Description);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ErrorMessageResponse))]
[JsonSerializable(typeof(Vs2026ByomInfoResponse))]
[JsonSerializable(typeof(ApplyVsCodeOllamaConfigRequest))]
[JsonSerializable(typeof(VsCodeUserDirectoryRequest))]
[JsonSerializable(typeof(RemoveVsCodeOllamaConfigRequest))]
[JsonSerializable(typeof(ListVsCodeConfigBackupsRequest))]
[JsonSerializable(typeof(RestoreVsCodeConfigBackupRequest))]
[JsonSerializable(typeof(VsCodeConfigApplyResult))]
[JsonSerializable(typeof(VsCodeOllamaConfigStatus))]
[JsonSerializable(typeof(VsCodeConfigFileChange))]
[JsonSerializable(typeof(VsCodeConfigFieldChange))]
[JsonSerializable(typeof(VsCodeConfigBackup))]
[JsonSerializable(typeof(VsCodeConfigRestoreResult))]
[JsonSerializable(typeof(SaveProviderConfigRequest))]
[JsonSerializable(typeof(TestProviderConnectionRequest))]
[JsonSerializable(typeof(ReorderProvidersRequest))]
[JsonSerializable(typeof(ProviderConnectionTestResult))]
[JsonSerializable(typeof(ProviderConnectionTestStep))]
[JsonSerializable(typeof(ProviderConfigExportDocument))]
[JsonSerializable(typeof(ProviderConfigExportItem))]
[JsonSerializable(typeof(RequestAnalyticsSnapshot))]
[JsonSerializable(typeof(RequestAnalyticsSummary))]
[JsonSerializable(typeof(ListenerStatus))]
[JsonSerializable(typeof(RequestLogEntry))]
[JsonSerializable(typeof(CopilotCompatibilityProbeResult))]
[JsonSerializable(typeof(CopilotCompatibilityProbeStep))]
[JsonSerializable(typeof(DashboardProviderConfigView))]
[JsonSerializable(typeof(DashboardTagsResponse))]
[JsonSerializable(typeof(DashboardModelInfo))]
[JsonSerializable(typeof(DashboardModelDetails))]
[JsonSerializable(typeof(DashboardVsCodeUserDirectory))]
[JsonSerializable(typeof(IReadOnlyList<DashboardProviderConfigView>))]
[JsonSerializable(typeof(IReadOnlyList<DashboardModelInfo>))]
[JsonSerializable(typeof(IReadOnlyList<DashboardVsCodeUserDirectory>))]
[JsonSerializable(typeof(IReadOnlyList<VsCodeConfigBackup>))]
[JsonSerializable(typeof(IReadOnlyList<VsCodeConfigFileChange>))]
[JsonSerializable(typeof(IReadOnlyList<VsCodeConfigFieldChange>))]
[JsonSerializable(typeof(IReadOnlyList<ProviderConnectionTestStep>))]
[JsonSerializable(typeof(IReadOnlyList<ProviderConfigExportItem>))]
[JsonSerializable(typeof(IReadOnlyList<RequestLogEntry>))]
[JsonSerializable(typeof(IReadOnlyList<CopilotCompatibilityProbeStep>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class MewUiJsonContext : JsonSerializerContext;
