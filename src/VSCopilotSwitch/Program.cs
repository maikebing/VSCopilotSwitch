using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OmniHost;
using OmniHost.WebView2;
using OmniHost.Windows;
using VSCopilotSwitch.Core.Ollama;
using VSCopilotSwitch.Core.Providers;
using VSCopilotSwitch.Services;
using VSCopilotSwitch.VsCodeConfig.Models;
using VSCopilotSwitch.VsCodeConfig.Services;
using Application = System.Windows.Forms.Application;

var configuredServerUrl = ResolveServerUrl();
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(configuredServerUrl);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddSingleton<IProviderConfigService, ProviderConfigService>();
builder.Services.AddSingleton<IModelProvider, ActiveProviderModelProvider>();
builder.Services.AddSingleton<ProviderConnectionTester>();
builder.Services.AddSingleton<IRequestAnalyticsService, RequestAnalyticsService>();
builder.Services.AddSingleton<IOllamaProxyService>(serviceProvider =>
    new OllamaProxyService(serviceProvider.GetServices<IModelProvider>()));
builder.Services.AddSingleton<IVsCodeConfigLocator, VsCodeConfigLocator>();
builder.Services.AddSingleton<IVsCodeConfigService, VsCodeConfigService>();

var webApp = builder.Build();

webApp.UseDefaultFiles();
webApp.MapStaticAssets();
webApp.Use(async (context, next) =>
{
    var analytics = context.RequestServices.GetRequiredService<IRequestAnalyticsService>();
    await analytics.InvokeAsync(context, next);
});

webApp.MapGet("/health", () => Results.Ok(new
{
    name = "VSCopilotSwitch",
    status = "ok",
    mode = webApp.Environment.EnvironmentName
}));

webApp.MapGet("/internal/network/port-status", (int port = 11434) =>
{
    if (port is < 1 or > 65535)
    {
        return Results.BadRequest(new PortStatusResponse(port, false, "端口必须在 1 到 65535 之间。"));
    }

    var available = IsTcpPortAvailable(port);
    var message = available
        ? $"127.0.0.1:{port} 当前可用。"
        : $"127.0.0.1:{port} 已被占用，请关闭其他 Ollama 或代理进程，或改用其他端口。";
    return Results.Ok(new PortStatusResponse(port, available, message));
});

webApp.MapGet("/internal/analytics", (
    IRequestAnalyticsService analytics) =>
{
    return Results.Ok(analytics.GetSnapshot(configuredServerUrl, IsTcpPortAvailable(11434)));
});

webApp.MapPost("/internal/analytics/clear", (
    IRequestAnalyticsService analytics) =>
{
    analytics.Clear();
    return Results.Ok(analytics.GetSnapshot(configuredServerUrl, IsTcpPortAvailable(11434)));
});

webApp.MapMethods("/api/version", new[] { HttpMethods.Get, HttpMethods.Head }, () =>
{
    return Results.Ok(new OllamaVersionResponse("0.6.8"));
});

webApp.MapGet("/api/tags", async (IOllamaProxyService ollama, CancellationToken cancellationToken) =>
{
    try
    {
        var response = await ollama.ListTagsAsync(cancellationToken);
        return Results.Ok(response);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        return ToOllamaErrorResult(ex);
    }
});

webApp.MapPost("/api/chat", async (
    OllamaChatRequest request,
    IOllamaProxyService ollama,
    HttpContext httpContext,
    IOptions<JsonOptions> jsonOptions,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (request.Stream == true)
        {
            await WriteOllamaStreamAsync(httpContext, ollama.ChatStreamAsync(request, cancellationToken), jsonOptions.Value.SerializerOptions, cancellationToken);
            return;
        }

        var response = await ollama.ChatAsync(request, cancellationToken);
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, response, jsonOptions.Value.SerializerOptions, cancellationToken);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        await WriteOllamaErrorAsync(httpContext, ex, jsonOptions.Value.SerializerOptions, cancellationToken);
    }
});

webApp.MapGet("/internal/vscode/user-directories", (IVsCodeConfigLocator locator) =>
{
    return Results.Ok(locator.FindUserDirectories());
});

webApp.MapGet("/internal/providers", async (
    IProviderConfigService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.ListAsync(cancellationToken));
});

webApp.MapPost("/internal/providers", async (
    SaveProviderConfigRequest request,
    IProviderConfigService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.SaveAsync(request, cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

webApp.MapPost("/internal/providers/test-connection", async (
    TestProviderConnectionRequest request,
    IProviderConfigService configService,
    ProviderConnectionTester connectionTester,
    CancellationToken cancellationToken) =>
{
    var config = await configService.BuildConnectionTestConfigAsync(request, cancellationToken);
    var result = await connectionTester.TestAsync(config, cancellationToken);
    return Results.Ok(result);
});

webApp.MapPost("/internal/providers/reorder", async (
    ReorderProvidersRequest request,
    IProviderConfigService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.ReorderAsync(request, cancellationToken));
});

webApp.MapPost("/internal/providers/{providerId}/activate", async (
    string providerId,
    IProviderConfigService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.ActivateAsync(providerId, cancellationToken));
});

webApp.MapDelete("/internal/providers/{providerId}", async (
    string providerId,
    IProviderConfigService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.DeleteAsync(providerId, cancellationToken));
});

webApp.MapPost("/internal/vscode/apply-ollama", async (
    ApplyVsCodeOllamaConfigRequest request,
    IOllamaProxyService ollama,
    IVsCodeConfigService service,
    CancellationToken cancellationToken) =>
{
    var config = request.Config ?? await BuildRuntimeManagedOllamaConfigAsync(configuredServerUrl, ollama, cancellationToken);
    var result = await service.ApplyOllamaConfigAsync(request.UserDirectory, config, request.DryRun, cancellationToken);
    return Results.Ok(result);
});

webApp.MapPost("/internal/vscode/ollama-status", async (
    VsCodeUserDirectoryRequest request,
    IVsCodeConfigService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.GetOllamaConfigStatusAsync(request.UserDirectory, cancellationToken);
    return Results.Ok(result);
});

webApp.MapPost("/internal/vscode/remove-ollama", async (
    RemoveVsCodeOllamaConfigRequest request,
    IVsCodeConfigService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.RemoveOllamaConfigAsync(request.UserDirectory, request.DryRun, cancellationToken);
    return Results.Ok(result);
});

webApp.MapPost("/internal/vscode/backups", (
    ListVsCodeConfigBackupsRequest request,
    IVsCodeConfigService service) =>
{
    return Results.Ok(service.ListBackups(request.UserDirectory));
});

webApp.MapPost("/internal/vscode/restore-backup", async (
    RestoreVsCodeConfigBackupRequest request,
    IVsCodeConfigService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.RestoreBackupAsync(request.UserDirectory, request.BackupPath, cancellationToken);
    return Results.Ok(result);
});

webApp.MapFallbackToFile("/index.html");

await webApp.StartAsync();

var serverUrl = webApp.Services
    .GetRequiredService<IServer>()
    .Features
    .Get<IServerAddressesFeature>()?
    .Addresses
    .SingleOrDefault() ?? configuredServerUrl;

var desktopApp = OmniApp.CreateBuilder(args)
    .Configure(options =>
    {
        options.Title = "VSCopilotSwitch";
        options.StartUrl = serverUrl;
        options.Width = 1280;
        options.Height = 820;
        options.EnableDevTools = webApp.Environment.IsDevelopment();
        // 使用系统原生标题栏，避免自绘 Chrome 与当前页面视觉割裂，并自动跟随操作系统主题。
        options.WindowStyle = OmniWindowStyle.Normal;
        options.BuiltInTitleBarStyle = OmniBuiltInTitleBarStyle.None;
        options.ScrollBarMode = OmniScrollBarMode.Auto;
        options.UserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VSCopilotSwitch",
            "WebView2");
    })
    .UseAdapter(new WebView2AdapterFactory())
    .UseRuntime(new Win32Runtime())
    .UseDesktopApp(new VSCopilotSwitchDesktopApp(serverUrl))
    .Build();

try
{
    await desktopApp.RunAsync();
}
finally
{
    await webApp.StopAsync();
    await webApp.DisposeAsync();
}

static int GetFreeTcpPort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

static string ResolveServerUrl()
{
    var configuredUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    var configuredUrl = configuredUrls?
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault();

    if (Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && uri.Port is >= 1 and <= 65535)
    {
        return uri.AbsoluteUri.TrimEnd('/');
    }

    var port = GetFreeTcpPort();
    return $"http://127.0.0.1:{port}";
}

static bool IsTcpPortAvailable(int targetPort)
{
    try
    {
        using var listener = new TcpListener(IPAddress.Loopback, targetPort);
        listener.Start();
        return true;
    }
    catch (SocketException)
    {
        return false;
    }
}

static IResult ToOllamaErrorResult(Exception exception)
{
    var (statusCode, response) = MapOllamaException(exception);
    return Results.Json(response, statusCode: statusCode);
}

static async Task WriteOllamaStreamAsync(
    HttpContext httpContext,
    IAsyncEnumerable<OllamaChatResponse> stream,
    JsonSerializerOptions jsonOptions,
    CancellationToken cancellationToken)
{
    httpContext.Response.StatusCode = StatusCodes.Status200OK;
    httpContext.Response.ContentType = "application/x-ndjson; charset=utf-8";

    try
    {
        await foreach (var chunk in stream.WithCancellation(cancellationToken))
        {
            await JsonSerializer.SerializeAsync(httpContext.Response.Body, chunk, jsonOptions, cancellationToken);
            await httpContext.Response.WriteAsync("\n", cancellationToken);
            await httpContext.Response.Body.FlushAsync(cancellationToken);
        }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex) when (httpContext.Response.HasStarted)
    {
        var (_, response) = MapOllamaException(ex);
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, response, jsonOptions, cancellationToken);
        await httpContext.Response.WriteAsync("\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }
}

static async Task WriteOllamaErrorAsync(
    HttpContext httpContext,
    Exception exception,
    JsonSerializerOptions jsonOptions,
    CancellationToken cancellationToken)
{
    if (httpContext.Response.HasStarted)
    {
        return;
    }

    var (statusCode, response) = MapOllamaException(exception);
    httpContext.Response.StatusCode = statusCode;
    httpContext.Response.ContentType = "application/json; charset=utf-8";
    await JsonSerializer.SerializeAsync(httpContext.Response.Body, response, jsonOptions, cancellationToken);
}

static (int StatusCode, OllamaErrorResponse Response) MapOllamaException(Exception exception)
{
    if (exception is OllamaProxyException proxyException)
    {
        var statusCode = proxyException.Kind switch
        {
            OllamaProxyErrorKind.InvalidRequest => StatusCodes.Status400BadRequest,
            OllamaProxyErrorKind.ModelNotFound => StatusCodes.Status404NotFound,
            OllamaProxyErrorKind.AmbiguousModel => StatusCodes.Status409Conflict,
            OllamaProxyErrorKind.ProviderUnauthorized => StatusCodes.Status401Unauthorized,
            OllamaProxyErrorKind.ProviderRateLimited => StatusCodes.Status429TooManyRequests,
            OllamaProxyErrorKind.ProviderTimeout => StatusCodes.Status504GatewayTimeout,
            OllamaProxyErrorKind.ProviderUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status502BadGateway
        };

        return (statusCode, new OllamaErrorResponse(proxyException.PublicMessage, proxyException.Code));
    }

    return (StatusCodes.Status500InternalServerError, new OllamaErrorResponse("请求处理失败，请稍后重试。", "internal_error"));
}

static async Task<ManagedOllamaConfig> BuildRuntimeManagedOllamaConfigAsync(
    string baseUrl,
    IOllamaProxyService ollama,
    CancellationToken cancellationToken)
{
    var tags = await ollama.ListTagsAsync(cancellationToken);
    var models = tags.Models
        .Select(model =>
        {
            var upstreamModel = string.IsNullOrWhiteSpace(model.Details.ParentModel)
                ? model.Model
                : model.Details.ParentModel;
            var cleanModelName = string.IsNullOrWhiteSpace(upstreamModel)
                ? model.Model
                : upstreamModel;
            var vsCodeModelName = ToVsCodeModelName(cleanModelName);

            return new ManagedOllamaModel(vsCodeModelName, vsCodeModelName, vsCodeModelName);
        })
        .Where(model => !string.IsNullOrWhiteSpace(model.Id))
        .DistinctBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    return new ManagedOllamaConfig(
        NormalizePublicBaseUrl(baseUrl),
        models.Length > 0 ? models : ManagedOllamaConfig.Default.Models);
}

static string ToVsCodeModelName(string upstreamModel)
{
    const string suffix = "@vscc";
    var trimmed = upstreamModel.Trim();
    return trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
        ? trimmed
        : $"{trimmed}{suffix}";
}

static string NormalizePublicBaseUrl(string baseUrl)
{
    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
    {
        return baseUrl.TrimEnd('/');
    }

    var builder = new UriBuilder(uri)
    {
        Path = string.Empty,
        Query = string.Empty,
        Fragment = string.Empty
    };

    return builder.Uri.AbsoluteUri.TrimEnd('/');
}

public sealed record PortStatusResponse(int Port, bool Available, string Message);

public sealed record OllamaVersionResponse(
    [property: JsonPropertyName("version")] string Version);

public sealed record ApplyVsCodeOllamaConfigRequest(
    string UserDirectory,
    ManagedOllamaConfig? Config,
    bool DryRun = true);

public sealed record VsCodeUserDirectoryRequest(string UserDirectory);

public sealed record RemoveVsCodeOllamaConfigRequest(string UserDirectory, bool DryRun = true);

public sealed record ListVsCodeConfigBackupsRequest(string UserDirectory);

public sealed record RestoreVsCodeConfigBackupRequest(string UserDirectory, string BackupPath);

sealed class VSCopilotSwitchDesktopApp(string serverUrl) : IWindowAwareDesktopApp, IDisposable
{
    private NotifyIcon? _trayIcon;
    private IOmniWindowManager? _windowManager;
    private string? _mainWindowId;

    public Task OnStartAsync(IWebViewAdapter adapter, CancellationToken cancellationToken = default)
    {
        // 暴露最小宿主信息，前端后续可据此判断是否运行在 OmniHost 桌面壳内。
        adapter.JsBridge.RegisterHandler("host.info", _ => Task.FromResult(JsonSerializer.Serialize(new
        {
            serverUrl,
            platform = "windows-win32-webview2"
        })));
        adapter.JsBridge.RegisterHandler("host.openExternal", OpenExternalUrlAsync);
        return Task.CompletedTask;
    }

    public Task OnClosingAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task OnWindowStartAsync(OmniWindowContext window, CancellationToken cancellationToken = default)
    {
        if (!window.IsMainWindow)
        {
            return Task.CompletedTask;
        }

        _windowManager = window.WindowManager;
        _mainWindowId = window.WindowId;
        EnsureTrayIcon();
        return Task.CompletedTask;
    }

    public Task OnWindowClosingAsync(OmniWindowContext window, CancellationToken cancellationToken = default)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        var openMenuItem = new ToolStripMenuItem("打开 VSCopilotSwitch", null, (_, _) => ActivateMainWindow());
        var providerMenuItem = new ToolStripMenuItem("当前提供商：My Codex") { Enabled = false };
        var statusMenuItem = new ToolStripMenuItem("代理状态：运行中") { Enabled = false };
        var exitMenuItem = new ToolStripMenuItem("退出并停止本地代理", null, (_, _) => ExitApplication());

        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "VSCopilotSwitch - My Codex",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        _trayIcon.ContextMenuStrip.Items.Add(openMenuItem);
        _trayIcon.ContextMenuStrip.Items.Add(providerMenuItem);
        _trayIcon.ContextMenuStrip.Items.Add(statusMenuItem);
        _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _trayIcon.ContextMenuStrip.Items.Add(exitMenuItem);
        _trayIcon.DoubleClick += (_, _) => ActivateMainWindow();
    }

    private void ActivateMainWindow()
    {
        if (_windowManager is not null && !string.IsNullOrWhiteSpace(_mainWindowId))
        {
            _windowManager.TryActivateWindow(_mainWindowId);
        }
    }

    private void ExitApplication()
    {
        if (_windowManager is not null && !string.IsNullOrWhiteSpace(_mainWindowId))
        {
            _windowManager.TryCloseWindow(_mainWindowId);
            return;
        }

        Application.Exit();
    }

    private static Task<string> OpenExternalUrlAsync(string payload)
    {
        var request = JsonSerializer.Deserialize<OpenExternalUrlRequest>(payload);
        if (request is null || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("外部链接地址无效。");
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("只允许打开 http 或 https 外部链接。");
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
        {
            UseShellExecute = true
        });

        return Task.FromResult(JsonSerializer.Serialize(new { opened = true }));
    }

    private sealed record OpenExternalUrlRequest(string Url);
}
