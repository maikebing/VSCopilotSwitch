using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OmniHost;
using OmniHost.NativeWebView2;
using OmniHost.Windows;
using VSCopilotSwitch.Core.Ollama;
using VSCopilotSwitch.Core.Providers;
using VSCopilotSwitch.Services;
using VSCopilotSwitch.VsCodeConfig.Models;
using VSCopilotSwitch.VsCodeConfig.Services;

var configuredServerUrl = ResolveServerUrl();
var builder = OmniApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls(configuredServerUrl);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, VSCopilotSwitchJsonContext.Default);
});

builder.Services.AddSingleton<IProviderConfigService, ProviderConfigService>();
builder.Services.AddSingleton<IModelProvider, ActiveProviderModelProvider>();
builder.Services.AddSingleton<ProviderConnectionTester>();
builder.Services.AddSingleton<IRequestAnalyticsService, RequestAnalyticsService>();
builder.Services.AddSingleton<ITrayMenuService, TrayMenuService>();
builder.Services.AddSingleton<IOllamaProxyService>(serviceProvider =>
    new OllamaProxyService(serviceProvider.GetServices<IModelProvider>()));
builder.Services.AddSingleton<IVsCodeConfigLocator, VsCodeConfigLocator>();
builder.Services.AddSingleton<IVsCodeConfigService, VsCodeConfigService>();

var app = builder
    .ConfigureDesktop((options, webApp) =>
    {
        var serverUrl = ResolveStartedServerUrl(webApp, configuredServerUrl);
        options.Title = "VSCopilotSwitch";
        options.StartUrl = serverUrl;
        options.Width = 1280;
        options.Height = 820;
        options.EnableDevTools = webApp.Environment.IsDevelopment();
        // 使用系统原生标题栏，避免自绘 Chrome 与当前页面视觉割裂，并自动跟随操作系统主题。
        options.WindowStyle = OmniWindowStyle.Normal;
        options.BuiltInTitleBarStyle = OmniBuiltInTitleBarStyle.None;
        options.ScrollBarMode = OmniScrollBarMode.Auto;
        options.IconPath = EnsureAppIconFile();
        options.EnableTrayIcon = true;
        options.TrayToolTip = "VSCopilotSwitch";
        options.TrayOpenText = "打开 VSCopilotSwitch";
        options.TrayExitText = "退出 VSCopilotSwitch";
        var trayMenu = webApp.Services.GetRequiredService<ITrayMenuService>();
        options.TrayToolTipProvider = trayMenu.GetToolTip;
        options.TrayMenuProvider = trayMenu.GetMenuItems;
        options.TrayCommandHandler = trayMenu.HandleCommandAsync;
        options.UserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VSCopilotSwitch",
            "WebView2");
    })
    .UseAdapter(new NativeWebView2AdapterFactory())
    .UseRuntime(new Win32Runtime())
    .UseDesktopApp(webApp => new VSCopilotSwitchDesktopApp(
        ResolveStartedServerUrl(webApp, configuredServerUrl)))
    .Build();

var webApp = app.Web;
var embeddedSpaResources = BuildEmbeddedSpaResourceMap(Assembly.GetExecutingAssembly());
var contentTypeProvider = new FileExtensionContentTypeProvider();

webApp.Use(async (context, next) =>
{
    var analytics = context.RequestServices.GetRequiredService<IRequestAnalyticsService>();
    await analytics.InvokeAsync(context, next);
});

webApp.MapGet("/health", () => Results.Ok(new
    HealthResponse("VSCopilotSwitch", "ok", webApp.Environment.EnvironmentName)));

webApp.MapGet("/internal/network/port-status", (int port = 5124) =>
{
    if (port is < 1 or > 65535)
    {
        return Results.BadRequest(new PortStatusResponse(port, false, "端口必须在 1 到 65535 之间。"));
    }

    if (port == 11434)
    {
        return Results.BadRequest(new PortStatusResponse(port, false, "11434 是 Ollama 默认端口，VSCopilotSwitch 不再使用该端口作为 VS Code Provider URL。"));
    }

    var available = IsTcpPortAvailable(port);
    var message = available
        ? $"127.0.0.1:{port} 当前可用。"
        : $"127.0.0.1:{port} 已被占用，请关闭其他代理进程，或改用其他端口。";
    return Results.Ok(new PortStatusResponse(port, available, message));
});

webApp.MapGet("/internal/analytics", (
    IRequestAnalyticsService analytics) =>
{
    return Results.Ok(analytics.GetSnapshot(configuredServerUrl));
});

webApp.MapPost("/internal/analytics/clear", (
    IRequestAnalyticsService analytics) =>
{
    analytics.Clear();
    return Results.Ok(analytics.GetSnapshot(configuredServerUrl));
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

webApp.MapPost("/api/show", async (
    OllamaShowRequest request,
    IOllamaProxyService ollama,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await ollama.ShowAsync(request, cancellationToken);
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
    CancellationToken cancellationToken) =>
{
    try
    {
        if (request.Stream == true)
        {
            await WriteOllamaStreamAsync(httpContext, ollama.ChatStreamAsync(request, cancellationToken), cancellationToken);
            return;
        }

        var response = await ollama.ChatAsync(request, cancellationToken);
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            response,
            VSCopilotSwitchJsonContext.Default.OllamaChatResponse,
            cancellationToken);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        await WriteOllamaErrorAsync(httpContext, ex, cancellationToken);
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
        return Results.BadRequest(new ErrorMessageResponse(ex.Message));
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

webApp.MapGet("/internal/providers/export", async (
    IProviderConfigService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.ExportAsync(cancellationToken));
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
    try
    {
        var config = request.Config ?? await BuildRuntimeManagedOllamaConfigAsync(configuredServerUrl, ollama, cancellationToken);
        var result = await service.ApplyOllamaConfigAsync(request.UserDirectory, config, request.DryRun, cancellationToken);
        return Results.Ok(result);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex) when (IsVsCodeConfigClientError(ex))
    {
        return Results.BadRequest(new ErrorMessageResponse(ex.Message));
    }
});

webApp.MapPost("/internal/vscode/ollama-status", async (
    VsCodeUserDirectoryRequest request,
    IVsCodeConfigService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.GetOllamaConfigStatusAsync(request.UserDirectory, cancellationToken);
        return Results.Ok(result);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex) when (IsVsCodeConfigClientError(ex))
    {
        return Results.BadRequest(new ErrorMessageResponse(ex.Message));
    }
});

webApp.MapPost("/internal/vscode/remove-ollama", async (
    RemoveVsCodeOllamaConfigRequest request,
    IVsCodeConfigService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.RemoveOllamaConfigAsync(request.UserDirectory, request.DryRun, cancellationToken);
        return Results.Ok(result);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex) when (IsVsCodeConfigClientError(ex))
    {
        return Results.BadRequest(new ErrorMessageResponse(ex.Message));
    }
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

webApp.MapGet("/{**path}", async context =>
{
    await ServeEmbeddedSpaResourceAsync(context, embeddedSpaResources, contentTypeProvider);
});

await app.RunAsync();

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
        if (uri.Port == 11434)
        {
            throw new InvalidOperationException("11434 是 Ollama 默认端口，VSCopilotSwitch 请使用 http://127.0.0.1:5124 或其他非 11434 端口。");
        }

        return NormalizePublicBaseUrl(uri.AbsoluteUri);
    }

    return "http://127.0.0.1:5124";
}

static string ResolveStartedServerUrl(WebApplication webApp, string fallbackUrl)
{
    return webApp.Services
        .GetRequiredService<IServer>()
        .Features
        .Get<IServerAddressesFeature>()?
        .Addresses
        .SingleOrDefault() ?? fallbackUrl;
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
    return new OllamaErrorResult(statusCode, response);
}

static async Task WriteOllamaStreamAsync(
    HttpContext httpContext,
    IAsyncEnumerable<OllamaChatResponse> stream,
    CancellationToken cancellationToken)
{
    httpContext.Response.StatusCode = StatusCodes.Status200OK;
    httpContext.Response.ContentType = "application/x-ndjson; charset=utf-8";

    try
    {
        await foreach (var chunk in stream.WithCancellation(cancellationToken))
        {
            await JsonSerializer.SerializeAsync(
                httpContext.Response.Body,
                chunk,
                VSCopilotSwitchJsonContext.Default.OllamaChatResponse,
                cancellationToken);
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
        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            response,
            VSCopilotSwitchJsonContext.Default.OllamaErrorResponse,
            cancellationToken);
        await httpContext.Response.WriteAsync("\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }
}

static async Task WriteOllamaErrorAsync(
    HttpContext httpContext,
    Exception exception,
    CancellationToken cancellationToken)
{
    if (httpContext.Response.HasStarted)
    {
        return;
    }

    var (statusCode, response) = MapOllamaException(exception);
    httpContext.Response.StatusCode = statusCode;
    httpContext.Response.ContentType = "application/json; charset=utf-8";
    await JsonSerializer.SerializeAsync(
        httpContext.Response.Body,
        response,
        VSCopilotSwitchJsonContext.Default.OllamaErrorResponse,
        cancellationToken);
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

static bool IsVsCodeConfigClientError(Exception exception)
    => exception is ArgumentException
        or InvalidOperationException
        or IOException
        or UnauthorizedAccessException
        or System.Security.SecurityException;

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
        Host = string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ? "127.0.0.1" : uri.Host,
        Path = string.Empty,
        Query = string.Empty,
        Fragment = string.Empty
    };

    return builder.Uri.AbsoluteUri.TrimEnd('/');
}

static IReadOnlyDictionary<string, string> BuildEmbeddedSpaResourceMap(Assembly assembly)
{
    const string prefix = "Spa";
    return assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase)
                       || name.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
        .ToDictionary(
            name => "/" + name[prefix.Length..].TrimStart('\\', '/').Replace('\\', '/'),
            name => name,
            StringComparer.OrdinalIgnoreCase);
}

static string? EnsureAppIconFile()
{
    var assembly = Assembly.GetExecutingAssembly();
    var iconResourceName = assembly
        .GetManifestResourceNames()
        .FirstOrDefault(name => name.EndsWith("favicon.ico", StringComparison.OrdinalIgnoreCase));
    if (iconResourceName is null)
    {
        return null;
    }

    var iconPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VSCopilotSwitch",
        "Assets",
        "VSCopilotSwitch.ico");
    Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);

    using var resourceStream = assembly.GetManifestResourceStream(iconResourceName);
    if (resourceStream is null)
    {
        return null;
    }

    var shouldWrite = !File.Exists(iconPath) || new FileInfo(iconPath).Length != resourceStream.Length;
    if (!shouldWrite)
    {
        return iconPath;
    }

    // Win32 托盘和窗口图标需要文件路径；发布包仍只内嵌资源，运行时幂等提取到用户本地缓存。
    using var fileStream = File.Create(iconPath);
    resourceStream.CopyTo(fileStream);
    return iconPath;
}

static async Task ServeEmbeddedSpaResourceAsync(
    HttpContext context,
    IReadOnlyDictionary<string, string> resources,
    FileExtensionContentTypeProvider contentTypeProvider)
{
    var requestPath = context.Request.Path.Value;
    var resourcePath = string.IsNullOrWhiteSpace(requestPath) || requestPath == "/"
        ? "/index.html"
        : requestPath;

    if (!resources.TryGetValue(resourcePath, out var resourceName))
    {
        resourcePath = Path.HasExtension(resourcePath) ? resourcePath : "/index.html";
        if (!resources.TryGetValue(resourcePath, out resourceName))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
    }

    var assembly = Assembly.GetExecutingAssembly();
    await using var stream = assembly.GetManifestResourceStream(resourceName);
    if (stream is null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.ContentType = contentTypeProvider.TryGetContentType(resourcePath, out var contentType)
        ? contentType
        : "application/octet-stream";
    context.Response.ContentLength = stream.Length;

    if (resourcePath.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
    }

    await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
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

sealed class OllamaErrorResult(int statusCode, OllamaErrorResponse response) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            response,
            VSCopilotSwitchJsonContext.Default.OllamaErrorResponse,
            httpContext.RequestAborted);
    }
}

sealed class VSCopilotSwitchDesktopApp(string serverUrl) : IWindowAwareDesktopApp
{
    public Task OnStartAsync(IWebViewAdapter adapter, CancellationToken cancellationToken = default)
    {
        // 暴露最小宿主信息，前端后续可据此判断是否运行在 OmniHost 桌面壳内。
        adapter.JsBridge.RegisterHandler("host.info", _ => Task.FromResult(JsonSerializer.Serialize(
            new HostInfoResponse(
            serverUrl,
            "windows-win32-webview2"),
            VSCopilotSwitchJsonContext.Default.HostInfoResponse)));
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

        return Task.CompletedTask;
    }

    public Task OnWindowClosingAsync(OmniWindowContext window, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    private static Task<string> OpenExternalUrlAsync(string payload)
    {
        var request = JsonSerializer.Deserialize(
            payload,
            VSCopilotSwitchJsonContext.Default.OpenExternalUrlRequest);
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

        return Task.FromResult(JsonSerializer.Serialize(
            new OpenExternalUrlResult(true),
            VSCopilotSwitchJsonContext.Default.OpenExternalUrlResult));
    }
}

public sealed record HealthResponse(string Name, string Status, string Mode);

public sealed record ErrorMessageResponse(string Error);

public sealed record HostInfoResponse(string ServerUrl, string Platform);

public sealed record OpenExternalUrlRequest(string Url);

public sealed record OpenExternalUrlResult(bool Opened);
