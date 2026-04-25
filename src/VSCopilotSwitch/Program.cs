using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OmniHost;
using OmniHost.WebView2;
using OmniHost.Windows;
using VSCopilotSwitch.Core.Ollama;
using VSCopilotSwitch.Core.Providers;
using VSCopilotSwitch.VsCodeConfig.Models;
using VSCopilotSwitch.VsCodeConfig.Services;

var port = GetFreeTcpPort();
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

builder.Services.AddSingleton<IModelProvider, InMemoryModelProvider>();
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

webApp.MapGet("/api/tags", async (IOllamaProxyService ollama, CancellationToken cancellationToken) =>
{
    var response = await ollama.ListTagsAsync(cancellationToken);
    return Results.Ok(response);
});

webApp.MapPost("/api/chat", async (OllamaChatRequest request, IOllamaProxyService ollama, CancellationToken cancellationToken) =>
{
    var response = await ollama.ChatAsync(request, cancellationToken);
    return Results.Ok(response);
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

public sealed record ApplyVsCodeOllamaConfigRequest(
    string UserDirectory,
    ManagedOllamaConfig? Config,
    bool DryRun = true);

sealed class VSCopilotSwitchDesktopApp(string serverUrl) : IDesktopApp
{
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
}

