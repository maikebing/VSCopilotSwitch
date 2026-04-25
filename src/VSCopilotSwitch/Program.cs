using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using VSCopilotSwitch.Core.Ollama;
using VSCopilotSwitch.Core.Providers;
using VSCopilotSwitch.VsCodeConfig.Models;
using VSCopilotSwitch.VsCodeConfig.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

builder.Services.AddSingleton<IModelProvider, InMemoryModelProvider>();
builder.Services.AddSingleton<IOllamaProxyService, OllamaProxyService>();
builder.Services.AddSingleton<IVsCodeConfigLocator, VsCodeConfigLocator>();
builder.Services.AddSingleton<IVsCodeConfigService, VsCodeConfigService>();

var app = builder.Build();

app.UseDefaultFiles();
app.MapStaticAssets();

app.MapGet("/health", () => Results.Ok(new
{
    name = "VSCopilotSwitch",
    status = "ok",
    mode = app.Environment.EnvironmentName
}));

app.MapGet("/api/tags", async (IOllamaProxyService ollama, CancellationToken cancellationToken) =>
{
    var response = await ollama.ListTagsAsync(cancellationToken);
    return Results.Ok(response);
});

app.MapPost("/api/chat", async (OllamaChatRequest request, IOllamaProxyService ollama, CancellationToken cancellationToken) =>
{
    var response = await ollama.ChatAsync(request, cancellationToken);
    return Results.Ok(response);
});

app.MapGet("/internal/vscode/user-directories", (IVsCodeConfigLocator locator) =>
{
    return Results.Ok(locator.FindUserDirectories());
});

app.MapPost("/internal/vscode/apply-ollama", async (
    ApplyVsCodeOllamaConfigRequest request,
    IVsCodeConfigService service,
    CancellationToken cancellationToken) =>
{
    var config = request.Config ?? ManagedOllamaConfig.Default;
    var result = await service.ApplyOllamaConfigAsync(request.UserDirectory, config, request.DryRun, cancellationToken);
    return Results.Ok(result);
});

app.MapFallbackToFile("/index.html");

app.Run();

public sealed record ApplyVsCodeOllamaConfigRequest(
    string UserDirectory,
    ManagedOllamaConfig? Config,
    bool DryRun = true);



