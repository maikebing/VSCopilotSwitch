using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace VSCopilotSwitch.MewUi;

internal static class Program
{
    private static readonly HttpClient Http = new();

    private static Label _statusText = null!;
    private static Label _healthText = null!;
    private static Label _providerText = null!;
    private static Label _modelText = null!;
    private static Label _vscodeText = null!;
    private static StackPanel _providerList = null!;
    private static StackPanel _modelList = null!;
    private static StackPanel _directoryList = null!;

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
                NativeMessageBox.Show(ex.ToString(), "VSCopilotSwitch MewUI fatal error", NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Error);
            }
        };

        Application.DispatcherUnhandledException += e =>
        {
            NativeMessageBox.Show(e.Exception.ToString(), "VSCopilotSwitch MewUI UI error", NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Error);
            e.Handled = true;
        };

        MewUiNativeHost? nativeHost = null;
        try
        {
            nativeHost = MewUiNativeHost.StartAsync(args).GetAwaiter().GetResult();
            Http.BaseAddress = new Uri(nativeHost.ServerUrl.TrimEnd('/') + "/");

            Application.Create()
                .UseAccent(Accent.Blue)
                .BuildMainWindow(CreateWindow)
                .Run();
        }
        finally
        {
            nativeHost?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static Window CreateWindow()
    {
        var window = new Window()
            .Title("VSCopilotSwitch MewUI Preview")
            .Resizable(1180, 760, minWidth: 900, minHeight: 620)
            .Padding(0)
            .Content(BuildShell())
            .OnLoaded(() => _ = RefreshAsync());

        return window;
    }

    private static Element BuildShell()
    {
        _statusText = BodyLabel("等待刷新");
        _healthText = ValueLabel("未知");
        _providerText = ValueLabel("未知");
        _modelText = ValueLabel("未知");
        _vscodeText = ValueLabel("未知");
        _providerList = new StackPanel().Spacing(10);
        _modelList = new StackPanel().Spacing(10);
        _directoryList = new StackPanel().Spacing(10);

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
                new ScrollViewer()
                    .Content(
                        new StackPanel()
                            .Spacing(14)
                            .Children(
                                Panel("供应商", _providerList),
                                Panel("模型", _modelList),
                                Panel("VS Code 配置目录", _directoryList),
                                ReadOnlyNotice()))
            );
    }

    private static Element Header()
        => new DockPanel()
            .LastChildFill(true)
            .Children(
                new StackPanel()
                    .Spacing(4)
                    .Children(
                        new Label().Text("VSCopilotSwitch").FontSize(26).Bold(),
                        BodyLabel("MewUI 原生界面，已内置本地 Ollama / OpenAI-compatible API。"))
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

    private static Element ReadOnlyNotice()
        => new GroupBox()
            .Header("迁移边界", accessKey: false)
            .Padding(14)
            .Content(
                new StackPanel()
                    .Spacing(6)
                    .Children(
                        BodyLabel("这个 MewUI 预览只调用 GET 接口读取状态，不保存供应商、不写入 VS Code 配置，也不导出密钥。"),
                        BodyLabel("本进程已经启动本地代理 API；供应商管理和 VS Code 写入流程会继续迁移到原生控件。")));

    private static async Task RefreshAsync()
    {
        SetStatus("读取中...");

        try
        {
            var dashboard = await LoadDashboardAsync();
            Application.Current.Dispatcher?.BeginInvoke(() => ApplyDashboard(dashboard));
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                SetStatus("读取失败");
                _healthText.Text = "无法连接";
                ReplaceChildren(_providerList, ErrorLabel($"读取本地 API 失败：{ex.Message}"));
                ReplaceChildren(_modelList, BodyLabel("内置本地代理启动失败或端口被占用，请检查 127.0.0.1:5124。"));
                ReplaceChildren(_directoryList, BodyLabel(Http.BaseAddress?.AbsoluteUri ?? "未配置服务地址"));
            });
        }
    }

    private static async Task<DashboardSnapshot> LoadDashboardAsync()
    {
        var healthTask = GetJsonAsync<HealthResponse>("/health", MewUiJsonContext.Default.HealthResponse);
        var providersTask = GetJsonAsync<IReadOnlyList<ProviderConfigView>>("/internal/providers", MewUiJsonContext.Default.IReadOnlyListProviderConfigView);
        var tagsTask = GetJsonAsync<TagsResponse>("/api/tags", MewUiJsonContext.Default.TagsResponse);
        var directoriesTask = GetJsonAsync<IReadOnlyList<VsCodeUserDirectory>>("/internal/vscode/user-directories", MewUiJsonContext.Default.IReadOnlyListVsCodeUserDirectory);

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
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo)
            ?? throw new InvalidOperationException($"接口 {path} 返回空响应。");
    }

    private static void ApplyDashboard(DashboardSnapshot dashboard)
    {
        SetStatus($"已刷新 {DateTime.Now:HH:mm:ss}");

        var activeProvider = dashboard.Providers.FirstOrDefault(provider => provider.Active);
        var model = dashboard.Tags.Models.FirstOrDefault();
        var existingDirectories = dashboard.Directories.Where(directory => directory.Exists).ToArray();

        _healthText.Text = $"{dashboard.Health.Status} / {dashboard.Health.Mode}";
        _providerText.Text = activeProvider?.Name ?? "未启用";
        _modelText.Text = model?.Name ?? activeProvider?.Model ?? "未发现";
        _vscodeText.Text = existingDirectories.Length == 0 ? "未发现" : $"{existingDirectories.Length} 个目录";

        ReplaceChildren(_providerList, BuildProviderRows(dashboard.Providers));
        ReplaceChildren(_modelList, BuildModelRows(dashboard.Tags.Models));
        ReplaceChildren(_directoryList, BuildDirectoryRows(dashboard.Directories));
    }

    private static Element[] BuildProviderRows(IReadOnlyList<ProviderConfigView> providers)
    {
        if (providers.Count == 0)
        {
            return [BodyLabel("还没有供应商配置。")];
        }

        return providers
            .Select(provider => Row(
                provider.Active ? $"{provider.Name}  [当前]" : provider.Name,
                $"{provider.Vendor} / {Empty(provider.Model, "未设置模型")} / 密钥：{(provider.HasApiKey ? provider.ApiKeyPreview ?? "已保存" : "未保存")}"))
            .Cast<Element>()
            .ToArray();
    }

    private static Element[] BuildModelRows(IReadOnlyList<ModelInfo> models)
    {
        if (models.Count == 0)
        {
            return [BodyLabel("当前供应商未返回模型列表。")];
        }

        return models
            .Take(12)
            .Select(model => Row(model.Name, $"{model.Details?.Family ?? "provider"} / {model.Details?.ParentModel ?? model.Model}"))
            .Cast<Element>()
            .Append(BodyLabel(models.Count > 12 ? $"另有 {models.Count - 12} 个模型未显示。" : ""))
            .Where(element => element is not Label label || !string.IsNullOrWhiteSpace(label.Text))
            .ToArray();
    }

    private static Element[] BuildDirectoryRows(IReadOnlyList<VsCodeUserDirectory> directories)
    {
        if (directories.Count == 0)
        {
            return [BodyLabel("没有发现 VS Code User 配置目录。")];
        }

        return directories
            .Select(directory => Row(
                directory.Description,
                $"{(directory.Exists ? "存在" : "可创建")} / {directory.Path}"))
            .Cast<Element>()
            .ToArray();
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
}

internal sealed record DashboardSnapshot(
    HealthResponse Health,
    IReadOnlyList<ProviderConfigView> Providers,
    TagsResponse Tags,
    IReadOnlyList<VsCodeUserDirectory> Directories);

internal sealed record ProviderConfigView(
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

internal sealed record TagsResponse(IReadOnlyList<ModelInfo> Models);

internal sealed record ModelInfo(
    string Name,
    string Model,
    DateTimeOffset ModifiedAt,
    long Size,
    string Digest,
    ModelDetails? Details);

internal sealed record ModelDetails(
    [property: JsonPropertyName("parent_model")] string? ParentModel,
    string Family,
    [property: JsonPropertyName("parameter_size")] string ParameterSize,
    [property: JsonPropertyName("quantization_level")] string QuantizationLevel);

internal sealed record VsCodeUserDirectory(
    string Path,
    string Profile,
    bool Exists,
    string Description);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ProviderConfigView))]
[JsonSerializable(typeof(TagsResponse))]
[JsonSerializable(typeof(ModelInfo))]
[JsonSerializable(typeof(ModelDetails))]
[JsonSerializable(typeof(VsCodeUserDirectory))]
[JsonSerializable(typeof(IReadOnlyList<ProviderConfigView>))]
[JsonSerializable(typeof(IReadOnlyList<ModelInfo>))]
[JsonSerializable(typeof(IReadOnlyList<VsCodeUserDirectory>))]
internal sealed partial class MewUiJsonContext : JsonSerializerContext;
