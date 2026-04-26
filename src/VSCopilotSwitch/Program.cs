using System.Net;
using System.Net.Sockets;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OmniHost;
using OmniHost.WebView2;
using OmniHost.Windows;
using VSCopilotSwitch.Core.Ollama;
using VSCopilotSwitch.Core.Providers;
using VSCopilotSwitch.Core.Providers.Claude;
using VSCopilotSwitch.Core.Providers.DeepSeek;
using VSCopilotSwitch.Core.Providers.Moark;
using VSCopilotSwitch.Core.Providers.Nvidia;
using VSCopilotSwitch.Core.Providers.OpenAI;
using VSCopilotSwitch.Core.Providers.Sub2Api;
using VSCopilotSwitch.VsCodeConfig.Models;
using VSCopilotSwitch.VsCodeConfig.Services;
using Application = System.Windows.Forms.Application;

var port = GetFreeTcpPort();
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var configuredProviders = LoadConfiguredModelProviders(builder.Configuration);
if (configuredProviders.Count == 0)
{
    builder.Services.AddSingleton<IModelProvider, InMemoryModelProvider>();
}
else
{
    foreach (var provider in configuredProviders)
    {
        builder.Services.AddSingleton<IModelProvider>(provider);
    }
}

builder.Services.AddSingleton<IOllamaProxyService, OllamaProxyService>();
builder.Services.AddSingleton<IVsCodeConfigLocator, VsCodeConfigLocator>();
builder.Services.AddSingleton<IVsCodeConfigService, VsCodeConfigService>();

var webApp = builder.Build();

webApp.UseDefaultFiles();
webApp.MapStaticAssets();

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

webApp.MapPost("/internal/vscode/apply-ollama", async (
    ApplyVsCodeOllamaConfigRequest request,
    IVsCodeConfigService service,
    CancellationToken cancellationToken) =>
{
    var config = request.Config ?? ManagedOllamaConfig.Default;
    var result = await service.ApplyOllamaConfigAsync(request.UserDirectory, config, request.DryRun, cancellationToken);
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
    .SingleOrDefault() ?? $"http://127.0.0.1:{port}";

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

static IReadOnlyList<IModelProvider> LoadConfiguredModelProviders(IConfiguration configuration)
{
    var providers = new List<IModelProvider>();

    var sub2ApiOptions = LoadSub2ApiOptions(configuration);
    if (sub2ApiOptions is not null)
    {
        providers.Add(new Sub2ApiModelProvider(new HttpClient(), sub2ApiOptions));
    }

    var openAiOptions = LoadOpenAiOptions(configuration);
    if (openAiOptions is not null)
    {
        providers.Add(new OpenAiModelProvider(new HttpClient(), openAiOptions));
    }

    var deepSeekOptions = LoadDeepSeekOptions(configuration);
    if (deepSeekOptions is not null)
    {
        providers.Add(new DeepSeekModelProvider(new HttpClient(), deepSeekOptions));
    }

    var nvidiaNimOptions = LoadNvidiaNimOptions(configuration);
    if (nvidiaNimOptions is not null)
    {
        providers.Add(new NvidiaNimModelProvider(new HttpClient(), nvidiaNimOptions));
    }

    var moarkOptions = LoadMoarkOptions(configuration);
    if (moarkOptions is not null)
    {
        providers.Add(new MoarkModelProvider(new HttpClient(), moarkOptions));
    }

    var claudeOptions = LoadClaudeOptions(configuration);
    if (claudeOptions is not null)
    {
        providers.Add(new ClaudeModelProvider(new HttpClient(), claudeOptions));
    }

    return providers;
}

static Sub2ApiProviderOptions? LoadSub2ApiOptions(IConfiguration configuration)
{
    var section = configuration.GetSection("Providers:Sub2Api");
    var baseUrl = section["BaseUrl"];
    var apiKey = section["ApiKey"];
    if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
    {
        return null;
    }

    return new Sub2ApiProviderOptions
    {
        ProviderName = string.IsNullOrWhiteSpace(section["ProviderName"]) ? "sub2api" : section["ProviderName"]!,
        BaseUrl = baseUrl,
        ApiKey = apiKey,
        Timeout = LoadTimeout(section),
        Models = LoadSub2ApiModels(section.GetSection("Models"))
    };
}

static OpenAiProviderOptions? LoadOpenAiOptions(IConfiguration configuration)
{
    var section = configuration.GetSection("Providers:OpenAI");
    var apiKey = section["ApiKey"];
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return null;
    }

    return new OpenAiProviderOptions
    {
        ProviderName = string.IsNullOrWhiteSpace(section["ProviderName"]) ? "openai" : section["ProviderName"]!,
        BaseUrl = string.IsNullOrWhiteSpace(section["BaseUrl"]) ? "https://api.openai.com" : section["BaseUrl"]!,
        ApiKey = apiKey,
        OrganizationId = section["OrganizationId"],
        ProjectId = section["ProjectId"],
        Timeout = LoadTimeout(section),
        Models = LoadOpenAiModels(section.GetSection("Models"))
    };
}

static DeepSeekProviderOptions? LoadDeepSeekOptions(IConfiguration configuration)
{
    var section = configuration.GetSection("Providers:DeepSeek");
    var apiKey = section["ApiKey"];
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return null;
    }

    return new DeepSeekProviderOptions
    {
        ProviderName = string.IsNullOrWhiteSpace(section["ProviderName"]) ? "deepseek" : section["ProviderName"]!,
        BaseUrl = string.IsNullOrWhiteSpace(section["BaseUrl"]) ? "https://api.deepseek.com" : section["BaseUrl"]!,
        ApiKey = apiKey,
        Timeout = LoadTimeout(section),
        Models = LoadDeepSeekModels(section.GetSection("Models"))
    };
}

static NvidiaNimProviderOptions? LoadNvidiaNimOptions(IConfiguration configuration)
{
    var section = configuration.GetSection("Providers:NvidiaNim");
    var apiKey = section["ApiKey"];
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return null;
    }

    return new NvidiaNimProviderOptions
    {
        ProviderName = string.IsNullOrWhiteSpace(section["ProviderName"]) ? "nvidia-nim" : section["ProviderName"]!,
        BaseUrl = string.IsNullOrWhiteSpace(section["BaseUrl"]) ? "https://integrate.api.nvidia.com" : section["BaseUrl"]!,
        ApiKey = apiKey,
        Timeout = LoadTimeout(section),
        Models = LoadNvidiaNimModels(section.GetSection("Models"))
    };
}

static MoarkProviderOptions? LoadMoarkOptions(IConfiguration configuration)
{
    var section = configuration.GetSection("Providers:Moark");
    var apiKey = section["ApiKey"];
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return null;
    }

    return new MoarkProviderOptions
    {
        ProviderName = string.IsNullOrWhiteSpace(section["ProviderName"]) ? "moark" : section["ProviderName"]!,
        BaseUrl = string.IsNullOrWhiteSpace(section["BaseUrl"]) ? "https://moark.ai/v1" : section["BaseUrl"]!,
        ApiKey = apiKey,
        Timeout = LoadTimeout(section),
        Models = LoadMoarkModels(section.GetSection("Models"))
    };
}

static ClaudeProviderOptions? LoadClaudeOptions(IConfiguration configuration)
{
    var section = configuration.GetSection("Providers:Claude");
    var apiKey = section["ApiKey"];
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return null;
    }

    return new ClaudeProviderOptions
    {
        ProviderName = string.IsNullOrWhiteSpace(section["ProviderName"]) ? "claude" : section["ProviderName"]!,
        BaseUrl = string.IsNullOrWhiteSpace(section["BaseUrl"]) ? "https://api.anthropic.com" : section["BaseUrl"]!,
        ApiKey = apiKey,
        AnthropicVersion = string.IsNullOrWhiteSpace(section["AnthropicVersion"]) ? "2023-06-01" : section["AnthropicVersion"]!,
        MaxTokens = LoadPositiveInt(section["MaxTokens"], 4096),
        Timeout = LoadTimeout(section),
        Models = LoadClaudeModels(section.GetSection("Models"))
    };
}

static TimeSpan LoadTimeout(IConfigurationSection section)
{
    return double.TryParse(section["TimeoutSeconds"], NumberStyles.Float, CultureInfo.InvariantCulture, out var timeoutSeconds)
        && timeoutSeconds > 0
        ? TimeSpan.FromSeconds(timeoutSeconds)
        : TimeSpan.FromSeconds(120);
}

static int LoadPositiveInt(string? rawValue, int defaultValue)
{
    return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
        ? value
        : defaultValue;
}

static IReadOnlyList<Sub2ApiModelOptions> LoadSub2ApiModels(IConfigurationSection section)
{
    return section.GetChildren()
        .Select(modelSection =>
        {
            var upstreamModel = modelSection["UpstreamModel"] ?? modelSection["Model"] ?? modelSection["Id"];
            if (string.IsNullOrWhiteSpace(upstreamModel))
            {
                return null;
            }

            var aliases = modelSection.GetSection("Aliases")
                .GetChildren()
                .Select(alias => alias.Value)
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Select(alias => alias!.Trim())
                .ToArray();

            return new Sub2ApiModelOptions(
                upstreamModel.Trim(),
                modelSection["Name"],
                modelSection["DisplayName"],
                aliases.Length > 0 ? aliases : null);
        })
        .Where(model => model is not null)
        .Cast<Sub2ApiModelOptions>()
        .ToArray();
}

static IReadOnlyList<OpenAiModelOptions> LoadOpenAiModels(IConfigurationSection section)
{
    return section.GetChildren()
        .Select(modelSection =>
        {
            var upstreamModel = modelSection["UpstreamModel"] ?? modelSection["Model"] ?? modelSection["Id"];
            if (string.IsNullOrWhiteSpace(upstreamModel))
            {
                return null;
            }

            var aliases = modelSection.GetSection("Aliases")
                .GetChildren()
                .Select(alias => alias.Value)
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Select(alias => alias!.Trim())
                .ToArray();

            return new OpenAiModelOptions(
                upstreamModel.Trim(),
                modelSection["Name"],
                modelSection["DisplayName"],
                aliases.Length > 0 ? aliases : null);
        })
        .Where(model => model is not null)
        .Cast<OpenAiModelOptions>()
        .ToArray();
}

static IReadOnlyList<DeepSeekModelOptions> LoadDeepSeekModels(IConfigurationSection section)
{
    return section.GetChildren()
        .Select(modelSection =>
        {
            var upstreamModel = modelSection["UpstreamModel"] ?? modelSection["Model"] ?? modelSection["Id"];
            if (string.IsNullOrWhiteSpace(upstreamModel))
            {
                return null;
            }

            var aliases = modelSection.GetSection("Aliases")
                .GetChildren()
                .Select(alias => alias.Value)
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Select(alias => alias!.Trim())
                .ToArray();

            return new DeepSeekModelOptions(
                upstreamModel.Trim(),
                modelSection["Name"],
                modelSection["DisplayName"],
                aliases.Length > 0 ? aliases : null);
        })
        .Where(model => model is not null)
        .Cast<DeepSeekModelOptions>()
        .ToArray();
}

static IReadOnlyList<NvidiaNimModelOptions> LoadNvidiaNimModels(IConfigurationSection section)
{
    return section.GetChildren()
        .Select(modelSection =>
        {
            var upstreamModel = modelSection["UpstreamModel"] ?? modelSection["Model"] ?? modelSection["Id"];
            if (string.IsNullOrWhiteSpace(upstreamModel))
            {
                return null;
            }

            var aliases = LoadAliases(modelSection);
            return new NvidiaNimModelOptions(
                upstreamModel.Trim(),
                modelSection["Name"],
                modelSection["DisplayName"],
                aliases.Length > 0 ? aliases : null);
        })
        .Where(model => model is not null)
        .Cast<NvidiaNimModelOptions>()
        .ToArray();
}

static IReadOnlyList<MoarkModelOptions> LoadMoarkModels(IConfigurationSection section)
{
    return section.GetChildren()
        .Select(modelSection =>
        {
            var upstreamModel = modelSection["UpstreamModel"] ?? modelSection["Model"] ?? modelSection["Id"];
            if (string.IsNullOrWhiteSpace(upstreamModel))
            {
                return null;
            }

            var aliases = LoadAliases(modelSection);
            return new MoarkModelOptions(
                upstreamModel.Trim(),
                modelSection["Name"],
                modelSection["DisplayName"],
                aliases.Length > 0 ? aliases : null);
        })
        .Where(model => model is not null)
        .Cast<MoarkModelOptions>()
        .ToArray();
}

static IReadOnlyList<ClaudeModelOptions> LoadClaudeModels(IConfigurationSection section)
{
    return section.GetChildren()
        .Select(modelSection =>
        {
            var upstreamModel = modelSection["UpstreamModel"] ?? modelSection["Model"] ?? modelSection["Id"];
            if (string.IsNullOrWhiteSpace(upstreamModel))
            {
                return null;
            }

            var aliases = LoadAliases(modelSection);
            return new ClaudeModelOptions(
                upstreamModel.Trim(),
                modelSection["Name"],
                modelSection["DisplayName"],
                aliases.Length > 0 ? aliases : null);
        })
        .Where(model => model is not null)
        .Cast<ClaudeModelOptions>()
        .ToArray();
}

static string[] LoadAliases(IConfigurationSection modelSection)
{
    return modelSection.GetSection("Aliases")
        .GetChildren()
        .Select(alias => alias.Value)
        .Where(alias => !string.IsNullOrWhiteSpace(alias))
        .Select(alias => alias!.Trim())
        .ToArray();
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

public sealed record PortStatusResponse(int Port, bool Available, string Message);

public sealed record ApplyVsCodeOllamaConfigRequest(
    string UserDirectory,
    ManagedOllamaConfig? Config,
    bool DryRun = true);

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
}
