using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
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

var builder = OmniApplication.CreateSlimBuilder(args);
var configuredServerUrls = ResolveServerUrls(builder.Configuration);
var configuredServerUrl = configuredServerUrls[0];
var configuredHttpsServerUrl = configuredServerUrls.FirstOrDefault(IsHttpsUrl);
var localHttpsCertificate = LocalHttpsCertificateService.EnsureTrustedForServerUrls(configuredServerUrls);
if (localHttpsCertificate is not null)
{
    // CreateSlimBuilder 不会自动启用 https:// URL 绑定所需的 Kestrel HTTPS 配置服务。
    builder.WebHost.UseKestrelHttpsConfiguration();
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ConfigureHttpsDefaults(httpsOptions =>
        {
            httpsOptions.ServerCertificate = localHttpsCertificate.Certificate;
        });
    });
}

builder.WebHost.UseUrls(configuredServerUrls);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, VSCopilotSwitchJsonContext.Default);
});

builder.Services.AddSingleton<IProviderConfigService, ProviderConfigService>();
builder.Services.AddSingleton<IModelProvider, ActiveProviderModelProvider>();
builder.Services.AddSingleton<ProviderConnectionTester>();
builder.Services.Configure<UsagePricingOptions>(builder.Configuration.GetSection("UsagePricing"));
builder.Services.AddSingleton<IUsageCostEstimator, UsageCostEstimator>();
builder.Services.AddSingleton<IRequestAnalyticsService, RequestAnalyticsService>();
builder.Services.AddSingleton<ITrayMenuService, TrayMenuService>();
builder.Services.Configure<UpdateOptions>(builder.Configuration.GetSection("Updates"));
builder.Services.AddSingleton(new HttpClient());
builder.Services.AddSingleton<IUpdateService, UpdateService>();
builder.Services.AddHostedService<UpdateBackgroundService>();
builder.Services.AddSingleton<IOllamaProxyService>(serviceProvider =>
    new OllamaProxyService(serviceProvider.GetServices<IModelProvider>()));
builder.Services.AddSingleton<ICopilotCompatibilityProbeService, CopilotCompatibilityProbeService>();
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
        options.HideMainWindowOnClose = true;
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

webApp.MapGet("/internal/about", () => Results.Ok(new AboutInfoResponse(
    "VSCopilotSwitch",
    ResolveAppVersion(),
    "https://github.com/maikebing/VSCopilotSwitch",
    "/VSCopilotSwitch.png")));

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

webApp.MapPost("/internal/copilot/probe", async (
    ICopilotCompatibilityProbeService probe,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await probe.RunAsync(cancellationToken));
});

webApp.MapGet("/internal/updates/check", async (
    IUpdateService updates,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await updates.CheckAsync(cancellationToken));
});

webApp.MapPost("/internal/updates/download-latest", async (
    UpdateDownloadRequest request,
    IUpdateService updates,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await updates.DownloadLatestAsync(request, cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new ErrorMessageResponse(ex.Message));
    }
    catch (HttpRequestException ex)
    {
        return Results.BadRequest(new ErrorMessageResponse($"下载更新失败：{ex.Message}"));
    }
    catch (IOException ex)
    {
        return Results.BadRequest(new ErrorMessageResponse($"写入更新缓存失败：{ex.Message}"));
    }
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

webApp.MapGet("/v1/models", async (
    IOllamaProxyService ollama,
    CancellationToken cancellationToken) =>
{
    try
    {
        var tags = await ollama.ListTagsAsync(cancellationToken);
        return Results.Ok(OpenAiModelMapper.CreateListResponse(tags));
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        return ToOpenAiErrorResult(ex);
    }
});

webApp.MapGet("/v1/models/{modelId}", async (
    string modelId,
    IOllamaProxyService ollama,
    CancellationToken cancellationToken) =>
{
    try
    {
        var tags = await ollama.ListTagsAsync(cancellationToken);
        var model = OpenAiModelMapper.FindModel(tags, modelId);
        return model is null
            ? Results.NotFound(new OpenAiErrorResponse(new OpenAiErrorBody($"模型 {modelId} 不存在。", "not_found_error", "model_not_found")))
            : Results.Ok(model);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        return ToOpenAiErrorResult(ex);
    }
});

webApp.MapGet("/internal/vs2026/byom", async (
    IOllamaProxyService ollama,
    CancellationToken cancellationToken) =>
{
    var tags = await ollama.ListTagsAsync(cancellationToken);
    var model = tags.Models.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Name));
    var modelId = model?.Name ?? "gpt-5.5@vscs";
    var httpsBaseUrl = configuredHttpsServerUrl is null ? null : NormalizePublicBaseUrl(configuredHttpsServerUrl);
    var endpoint = httpsBaseUrl is null ? null : $"{httpsBaseUrl}/v1";

    return Results.Ok(new Vs2026ByomInfoResponse(
        endpoint,
        modelId,
        "vscs-local",
        configuredHttpsServerUrl is not null,
        configuredHttpsServerUrl is null
            ? "未启用 HTTPS 监听。发布版默认会尝试启用 https://127.0.0.1:5443；如果端口被占用，可设置 VSCOPILOTSWITCH_HTTPS_URL 指向其他本机回环端口。"
            : $"可在 VS2026 Manage Models 中选择 Azure，填入该 HTTPS /v1 地址和模型 ID。本地 HTTPS 证书已写入当前用户证书库，指纹 {FormatCertificateThumbprint(localHttpsCertificate?.Thumbprint)}。"));
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

var openAiChatCompletionPaths = new[]
{
    "/v1/chat/completions",
    "/chat/completions",
    "/v1/v1/chat/completions",
    "/api/v1/chat/completions"
};
foreach (var path in openAiChatCompletionPaths)
{
    webApp.MapPost(path, HandleOpenAiChatCompletionAsync);
}

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

static string[] ResolveServerUrls(IConfiguration configuration)
{
    var configuredUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    var urls = configuredUrls?
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList() ?? new List<string>();
    if (urls.Count == 0)
    {
        urls.Add("http://127.0.0.1:5124");
    }

    var vs2026HttpsUrl = FirstNonEmpty(
        Environment.GetEnvironmentVariable("VSCOPILOTSWITCH_HTTPS_URL"),
        configuration["Vs2026:HttpsUrl"]);
    if (string.IsNullOrWhiteSpace(vs2026HttpsUrl) && IsVs2026AutoHttpsEnabled(configuration) && IsTcpPortAvailable(5443))
    {
        vs2026HttpsUrl = "https://127.0.0.1:5443";
    }

    if (!string.IsNullOrWhiteSpace(vs2026HttpsUrl))
    {
        urls.Add(vs2026HttpsUrl);
    }

    return urls
        .Select(ValidateServerUrl)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static bool IsVs2026AutoHttpsEnabled(IConfiguration configuration)
{
    var value = FirstNonEmpty(
        Environment.GetEnvironmentVariable("VSCOPILOTSWITCH_VS2026_AUTO_HTTPS"),
        configuration["Vs2026:AutoHttps"]);
    if (string.IsNullOrWhiteSpace(value))
    {
        return true;
    }

    return bool.TryParse(value, out var enabled)
        ? enabled
        : throw new InvalidOperationException("Vs2026:AutoHttps 必须是 true 或 false。");
}

static string ValidateServerUrl(string configuredUrl)
{
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

    throw new InvalidOperationException($"监听地址无效：{configuredUrl}。请使用 http://127.0.0.1:5124 或 https://127.0.0.1:5443 这类完整 URL。");
}

static string? FirstNonEmpty(params string?[] values)
    => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

static bool IsHttpsUrl(string value)
    => Uri.TryCreate(value, UriKind.Absolute, out var uri)
       && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

static string ResolveStartedServerUrl(WebApplication webApp, string fallbackUrl)
{
    var addresses = webApp.Services
        .GetRequiredService<IServer>()
        .Features
        .Get<IServerAddressesFeature>()?
        .Addresses;
    if (addresses is null || addresses.Count == 0)
    {
        return fallbackUrl;
    }

    return addresses.FirstOrDefault(IsHttpUrl)
        ?? addresses.FirstOrDefault(IsHttpsUrl)
        ?? addresses.FirstOrDefault()
        ?? fallbackUrl;
}

static bool IsHttpUrl(string value)
    => Uri.TryCreate(value, UriKind.Absolute, out var uri)
       && string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

static string FormatCertificateThumbprint(string? thumbprint)
{
    if (string.IsNullOrWhiteSpace(thumbprint))
    {
        return "未知";
    }

    var trimmed = thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal);
    return trimmed.Length <= 12
        ? trimmed
        : $"{trimmed[..6]}...{trimmed[^6..]}";
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

static async Task HandleOpenAiChatCompletionAsync(
    OpenAiChatCompletionRequest request,
    IOllamaProxyService ollama,
    HttpContext httpContext,
    CancellationToken cancellationToken)
{
    try
    {
        var ollamaRequest = ToOllamaChatRequest(request, request.Stream == true);
        if (request.Stream == true)
        {
            await WriteOpenAiChatCompletionStreamAsync(
                httpContext,
                request.Model,
                ollama.ChatStreamAsync(ollamaRequest, cancellationToken),
                cancellationToken);
            return;
        }

        var response = await ollama.ChatAsync(ollamaRequest, cancellationToken);
        var completion = OpenAiChatCompletionMapper.CreateResponse(
            $"chatcmpl-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            request.Model,
            response);

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            completion,
            VSCopilotSwitchJsonContext.Default.OpenAiChatCompletionResponse,
            cancellationToken);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        await WriteOpenAiErrorAsync(httpContext, ex, cancellationToken);
    }
}

static OllamaChatRequest ToOllamaChatRequest(OpenAiChatCompletionRequest request, bool stream)
{
    var messages = request.Messages?
        .Select(message => new OllamaChatMessage(
            string.IsNullOrWhiteSpace(message.Role) ? "user" : message.Role,
            ExtractOpenAiMessageContent(message.Content),
            ToChatToolCalls(message.ToolCalls),
            message.ToolCallId,
            message.Name,
            message.ReasoningContent,
            message.Thinking))
        .ToArray() ?? Array.Empty<OllamaChatMessage>();

    return new OllamaChatRequest(
        request.Model,
        messages,
        stream,
        ToChatTools(request.Tools),
        ToChatToolChoice(request.ToolChoice),
        request.ReasoningEffort,
        request.Thinking,
        request.Think);
}

static string ExtractOpenAiMessageContent(JsonElement? content)
{
    if (content is null)
    {
        return string.Empty;
    }

    var value = content.Value;
    return value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Array => string.Join(
            "\n",
            value.EnumerateArray()
                .Select(ExtractOpenAiContentPart)
                .Where(part => !string.IsNullOrWhiteSpace(part))),
        JsonValueKind.Object => ExtractOpenAiContentPart(value),
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => value.ToString()
    };
}

static string ExtractOpenAiContentPart(JsonElement part)
{
    if (part.ValueKind == JsonValueKind.String)
    {
        return part.GetString() ?? string.Empty;
    }

    if (part.ValueKind != JsonValueKind.Object)
    {
        return string.Empty;
    }

    if (part.TryGetProperty("text", out var textElement))
    {
        return textElement.ValueKind == JsonValueKind.String
            ? textElement.GetString() ?? string.Empty
            : textElement.ToString();
    }

    if (part.TryGetProperty("content", out var contentElement))
    {
        return ExtractOpenAiMessageContent(contentElement);
    }

    return string.Empty;
}

static IReadOnlyList<ChatTool>? ToChatTools(IReadOnlyList<OpenAiTool>? tools)
{
    var mapped = tools?
        .Where(tool => tool.Function is not null && !string.IsNullOrWhiteSpace(tool.Function.Name))
        .Select(tool => new ChatTool(
            string.IsNullOrWhiteSpace(tool.Type) ? "function" : tool.Type,
            new ChatFunctionTool(
                tool.Function.Name,
                tool.Function.Description,
                tool.Function.Parameters)))
        .ToArray();

    return mapped is { Length: > 0 } ? mapped : null;
}

static ChatToolChoice? ToChatToolChoice(JsonElement? toolChoice)
{
    if (toolChoice is null)
    {
        return null;
    }

    var value = toolChoice.Value;
    if (value.ValueKind == JsonValueKind.String)
    {
        var type = value.GetString();
        return string.IsNullOrWhiteSpace(type) ? null : new ChatToolChoice(type);
    }

    if (value.ValueKind != JsonValueKind.Object)
    {
        return null;
    }

    var objectType = value.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
        ? typeElement.GetString()
        : null;
    string? functionName = null;
    if (value.TryGetProperty("function", out var functionElement)
        && functionElement.ValueKind == JsonValueKind.Object
        && functionElement.TryGetProperty("name", out var nameElement)
        && nameElement.ValueKind == JsonValueKind.String)
    {
        functionName = nameElement.GetString();
    }

    return string.IsNullOrWhiteSpace(objectType) && string.IsNullOrWhiteSpace(functionName)
        ? null
        : new ChatToolChoice(string.IsNullOrWhiteSpace(objectType) ? "function" : objectType, functionName);
}

static IReadOnlyList<ChatToolCall>? ToChatToolCalls(IReadOnlyList<OpenAiToolCall>? toolCalls)
{
    var mapped = toolCalls?
        .Where(toolCall => toolCall.Function is not null)
        .Select(toolCall => new ChatToolCall(
            string.IsNullOrWhiteSpace(toolCall.Id) ? string.Empty : toolCall.Id,
            string.IsNullOrWhiteSpace(toolCall.Type) ? "function" : toolCall.Type,
            new ChatFunctionCall(
                toolCall.Function!.Name ?? string.Empty,
                toolCall.Function.Arguments ?? string.Empty),
            toolCall.Index))
        .ToArray();

    return mapped is { Length: > 0 } ? mapped : null;
}

static async Task WriteOpenAiChatCompletionStreamAsync(
    HttpContext httpContext,
    string requestedModel,
    IAsyncEnumerable<OllamaChatResponse> stream,
    CancellationToken cancellationToken)
{
    var id = $"chatcmpl-{Guid.NewGuid():N}";
    var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var finished = false;
    var roleSent = false;
    httpContext.Response.StatusCode = StatusCodes.Status200OK;
    httpContext.Response.ContentType = "text/event-stream; charset=utf-8";
    httpContext.Response.Headers.CacheControl = "no-cache";
    httpContext.Response.Headers["X-Accel-Buffering"] = "no";

    try
    {
        await foreach (var chunk in stream.WithCancellation(cancellationToken))
        {
            if (!roleSent)
            {
                await WriteOpenAiRoleChunkAsync(httpContext, id, created, requestedModel, cancellationToken);
                roleSent = true;
            }

            var toolCalls = OpenAiChatCompletionMapper.ToToolCalls(chunk.Message.ToolCalls);
            var hasReasoningContent = !string.IsNullOrWhiteSpace(chunk.Message.ReasoningContent)
                || !string.IsNullOrWhiteSpace(chunk.Message.Thinking);
            if (!string.IsNullOrEmpty(chunk.Message.Content)
                || toolCalls is not null
                || hasReasoningContent)
            {
                await WriteOpenAiServerSentEventAsync(
                    httpContext,
                    OpenAiChatCompletionMapper.CreateDeltaChunk(
                        id,
                        created,
                        requestedModel,
                        chunk),
                    cancellationToken);
            }

            if (chunk.Done)
            {
                finished = true;
                await WriteOpenAiFinishChunkAsync(httpContext, id, created, requestedModel, chunk.DoneReason, chunk.Usage, cancellationToken);
                break;
            }
        }

        if (!finished)
        {
            if (!roleSent)
            {
                await WriteOpenAiRoleChunkAsync(httpContext, id, created, requestedModel, cancellationToken);
            }

            await WriteOpenAiFinishChunkAsync(httpContext, id, created, requestedModel, "stop", null, cancellationToken);
        }

        await httpContext.Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex) when (httpContext.Response.HasStarted)
    {
        var (_, error) = MapOpenAiException(ex);
        await WriteOpenAiErrorServerSentEventAsync(httpContext, error, cancellationToken);
        await httpContext.Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }
}

static Task WriteOpenAiRoleChunkAsync(
    HttpContext httpContext,
    string id,
    long created,
    string requestedModel,
    CancellationToken cancellationToken)
    => WriteOpenAiServerSentEventAsync(
        httpContext,
        OpenAiChatCompletionMapper.CreateRoleChunk(
            id,
            created,
            requestedModel),
        cancellationToken);

static Task WriteOpenAiFinishChunkAsync(
    HttpContext httpContext,
    string id,
    long created,
    string requestedModel,
    string? doneReason,
    ChatUsage? usage,
    CancellationToken cancellationToken)
    => WriteOpenAiServerSentEventAsync(
        httpContext,
        OpenAiChatCompletionMapper.CreateFinishChunk(
            id,
            created,
            requestedModel,
            doneReason,
            usage),
        cancellationToken);

static async Task WriteOpenAiServerSentEventAsync(
    HttpContext httpContext,
    OpenAiChatCompletionChunk chunk,
    CancellationToken cancellationToken)
{
    await httpContext.Response.WriteAsync("data: ", cancellationToken);
    await JsonSerializer.SerializeAsync(
        httpContext.Response.Body,
        chunk,
        VSCopilotSwitchJsonContext.Default.OpenAiChatCompletionChunk,
        cancellationToken);
    await httpContext.Response.WriteAsync("\n\n", cancellationToken);
    await httpContext.Response.Body.FlushAsync(cancellationToken);
}

static async Task WriteOpenAiErrorServerSentEventAsync(
    HttpContext httpContext,
    OpenAiErrorResponse error,
    CancellationToken cancellationToken)
{
    await httpContext.Response.WriteAsync("data: ", cancellationToken);
    await JsonSerializer.SerializeAsync(
        httpContext.Response.Body,
        error,
        VSCopilotSwitchJsonContext.Default.OpenAiErrorResponse,
        cancellationToken);
    await httpContext.Response.WriteAsync("\n\n", cancellationToken);
    await httpContext.Response.Body.FlushAsync(cancellationToken);
}

static async Task WriteOpenAiErrorAsync(
    HttpContext httpContext,
    Exception exception,
    CancellationToken cancellationToken)
{
    if (httpContext.Response.HasStarted)
    {
        return;
    }

    var (statusCode, response) = MapOpenAiException(exception);
    httpContext.Response.StatusCode = statusCode;
    httpContext.Response.ContentType = "application/json; charset=utf-8";
    await JsonSerializer.SerializeAsync(
        httpContext.Response.Body,
        response,
        VSCopilotSwitchJsonContext.Default.OpenAiErrorResponse,
        cancellationToken);
}

static IResult ToOpenAiErrorResult(Exception exception)
{
    var (statusCode, response) = MapOpenAiException(exception);
    return new OpenAiErrorResult(statusCode, response);
}

static (int StatusCode, OpenAiErrorResponse Response) MapOpenAiException(Exception exception)
    => OpenAiErrorMapper.Map(exception);

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
    const string suffix = "@vscs";
    var trimmed = upstreamModel.Trim();
    if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
    {
        return trimmed;
    }

    return $"{trimmed}{suffix}";
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

static string ResolveAppVersion()
{
    var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
    var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    var version = informational?.Split('+')[0].Trim();

    if (!string.IsNullOrWhiteSpace(version))
    {
        return version;
    }

    return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}

public sealed record AboutInfoResponse(
    string Title,
    string Version,
    string GitHubUrl,
    string EnterpriseWeChatQrPath);

public sealed record PortStatusResponse(int Port, bool Available, string Message);

public sealed record OllamaVersionResponse(
    [property: JsonPropertyName("version")] string Version);

public sealed record OpenAiModelListResponse(
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("data")] IReadOnlyList<OpenAiModelInfo> Data);

public sealed record OpenAiModelInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("owned_by")] string OwnedBy);

public sealed record Vs2026ByomInfoResponse(
    string? Endpoint,
    string ModelId,
    string ApiKeyPlaceholder,
    bool HttpsEnabled,
    string Message);

public sealed record ApplyVsCodeOllamaConfigRequest(
    string UserDirectory,
    ManagedOllamaConfig? Config,
    bool DryRun = true);

public sealed record VsCodeUserDirectoryRequest(string UserDirectory);

public sealed record RemoveVsCodeOllamaConfigRequest(string UserDirectory, bool DryRun = true);

public sealed record ListVsCodeConfigBackupsRequest(string UserDirectory);

public sealed record RestoreVsCodeConfigBackupRequest(string UserDirectory, string BackupPath);

public sealed record OpenAiChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<OpenAiChatRequestMessage>? Messages,
    [property: JsonPropertyName("stream")] bool? Stream,
    [property: JsonPropertyName("tools")] IReadOnlyList<OpenAiTool>? Tools = null,
    [property: JsonPropertyName("tool_choice")] JsonElement? ToolChoice = null,
    [property: JsonPropertyName("reasoning_effort")] string? ReasoningEffort = null,
    [property: JsonPropertyName("thinking")] JsonElement? Thinking = null,
    [property: JsonPropertyName("think")] JsonElement? Think = null);

public sealed record OpenAiChatRequestMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] JsonElement? Content,
    [property: JsonPropertyName("tool_calls")] IReadOnlyList<OpenAiToolCall>? ToolCalls = null,
    [property: JsonPropertyName("tool_call_id")] string? ToolCallId = null,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("reasoning_content")] string? ReasoningContent = null,
    [property: JsonPropertyName("thinking")] string? Thinking = null);

public sealed record OpenAiChatCompletionResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("choices")] IReadOnlyList<OpenAiChatCompletionChoice> Choices,
    [property: JsonPropertyName("usage")] OpenAiUsage? Usage = null);

public sealed record OpenAiChatCompletionChoice(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("message")] OpenAiChatCompletionMessage Message,
    [property: JsonPropertyName("finish_reason")] string FinishReason);

public sealed record OpenAiChatCompletionMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("tool_calls")] IReadOnlyList<OpenAiToolCall>? ToolCalls = null,
    [property: JsonPropertyName("reasoning_content")] string? ReasoningContent = null);

public sealed record OpenAiChatCompletionChunk(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("choices")] IReadOnlyList<OpenAiChatCompletionChunkChoice> Choices,
    [property: JsonPropertyName("usage")] OpenAiUsage? Usage = null);

public sealed record OpenAiChatCompletionChunkChoice(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("delta")] OpenAiChatCompletionDelta Delta,
    [property: JsonPropertyName("finish_reason")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    string? FinishReason);

public sealed record OpenAiChatCompletionDelta(
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("tool_calls")] IReadOnlyList<OpenAiToolCall>? ToolCalls = null,
    [property: JsonPropertyName("reasoning_content")] string? ReasoningContent = null);

public sealed record OpenAiTool(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] OpenAiToolFunction Function);

public sealed record OpenAiToolFunction(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("parameters")] JsonElement? Parameters);

public sealed record OpenAiToolCall(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("function")] OpenAiFunctionCall? Function,
    [property: JsonPropertyName("index")] int? Index = null);

public sealed record OpenAiFunctionCall(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("arguments")] string? Arguments);

public sealed record OpenAiUsage(
    [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
    [property: JsonPropertyName("total_tokens")] int? TotalTokens);

public sealed record OpenAiErrorResponse(
    [property: JsonPropertyName("error")] OpenAiErrorBody Error);

public sealed record OpenAiErrorBody(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("code")] string Code);

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

sealed class OpenAiErrorResult(int statusCode, OpenAiErrorResponse response) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            response,
            VSCopilotSwitchJsonContext.Default.OpenAiErrorResponse,
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
