using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VSCopilotSwitch.Core.Ollama;
using VSCopilotSwitch.Core.Providers;
using VSCopilotSwitch.Services;
using VSCopilotSwitch.VsCodeConfig.Models;
using VSCopilotSwitch.VsCodeConfig.Services;

namespace VSCopilotSwitch;

internal sealed class VSCopilotSwitchNativeHost : IAsyncDisposable
{
    private readonly WebApplication _webApp;
    private readonly string _configuredServerUrl;

    private VSCopilotSwitchNativeHost(WebApplication webApp, string configuredServerUrl)
    {
        _webApp = webApp;
        _configuredServerUrl = configuredServerUrl;
    }

    public string ServerUrl { get; private set; } = string.Empty;

    public IServiceProvider Services => _webApp.Services;

    public static async Task<VSCopilotSwitchNativeHost> StartAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder(args);
        var configuredServerUrls = ResolveServerUrls(builder.Configuration);
        var configuredServerUrl = configuredServerUrls[0];
        var configuredHttpsServerUrl = configuredServerUrls.FirstOrDefault(IsHttpsUrl);
        LocalHttpsCertificateStatus? localHttpsCertificate;
        try
        {
            localHttpsCertificate = LocalHttpsCertificateService.EnsureTrustedForServerUrls(configuredServerUrls);
        }
        catch (Exception ex)
        {
            // 本地证书安装或信任失败不能阻断主程序启动；降级保留 HTTP 代理入口。
            StartupDiagnostics.WriteCrashLog(ex, "EnsureTrustedForServerUrls failed; HTTPS will be disabled.");
            localHttpsCertificate = null;
            configuredServerUrls = configuredServerUrls.Where(url => !IsHttpsUrl(url)).ToArray();
            configuredServerUrl = configuredServerUrls.Length > 0 ? configuredServerUrls[0] : "http://127.0.0.1:5124";
            configuredHttpsServerUrl = null;
            if (configuredServerUrls.Length == 0)
            {
                configuredServerUrls = new[] { configuredServerUrl };
            }
        }

        if (localHttpsCertificate is not null)
        {
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

        ConfigureServices(builder.Services, builder.Configuration);

        var webApp = builder.Build();
        ConfigurePipeline(webApp, configuredServerUrl, configuredHttpsServerUrl, localHttpsCertificate);

        var host = new VSCopilotSwitchNativeHost(webApp, configuredServerUrl);
        await webApp.StartAsync(cancellationToken);
        host.ServerUrl = ResolveStartedServerUrl(webApp, configuredServerUrl);
        return host;
    }

    public async ValueTask DisposeAsync()
    {
        await _webApp.StopAsync(TimeSpan.FromSeconds(5));
        await _webApp.DisposeAsync();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = null;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, VSCopilotSwitchApiJsonContext.Default);
        });

        services.AddSingleton<IProviderConfigService, ProviderConfigService>();
        services.AddSingleton<IModelProvider, ActiveProviderModelProvider>();
        services.AddSingleton<ProviderConnectionTester>();
        services.AddSingleton<ModelComparisonService>(provider => new ModelComparisonService(
            async (request, cancellationToken) =>
            {
                var configService = provider.GetRequiredService<IProviderConfigService>();
                var activeProvider = await configService.GetActiveRuntimeConfigAsync(cancellationToken)
                    ?? throw new InvalidOperationException("请先启用一个供应商后再进行模型比较。");
                return ProviderAdapterFactory.Create(new ProviderAdapterConfig(
                    activeProvider.Id,
                    activeProvider.Name,
                    activeProvider.ApiUrl,
                    request.Model,
                    activeProvider.Vendor,
                    activeProvider.ApiKey ?? string.Empty), TimeSpan.FromSeconds(30));
            },
            provider.GetRequiredService<IUsageCostEstimator>()));
        services.Configure<UsagePricingOptions>(configuration.GetSection("UsagePricing"));
        services.AddSingleton<IUsageCostEstimator, UsageCostEstimator>();
        services.AddSingleton<IRequestAnalyticsService, RequestAnalyticsService>();
        services.Configure<UpdateOptions>(configuration.GetSection("Updates"));
        services.AddSingleton(new HttpClient());
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IOllamaProxyService>(serviceProvider =>
            new OllamaProxyService(serviceProvider.GetServices<IModelProvider>()));
        services.AddSingleton<ICopilotCompatibilityProbeService, CopilotCompatibilityProbeService>();
        services.AddSingleton<IVsCodeConfigLocator, VsCodeConfigLocator>();
        services.AddSingleton<IVsCodeConfigService, VsCodeConfigService>();
    }

    private static void ConfigurePipeline(
        WebApplication webApp,
        string configuredServerUrl,
        string? configuredHttpsServerUrl,
        LocalHttpsCertificateStatus? localHttpsCertificate)
    {
        webApp.Use(async (context, next) =>
        {
            var analytics = context.RequestServices.GetRequiredService<IRequestAnalyticsService>();
            await analytics.InvokeAsync(context, next);
        });

        webApp.MapGet("/health", () => Results.Ok(new
            HealthResponse("VSCopilotSwitch", "ok", "MewUI Native")));

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

        foreach (var path in OpenAiCompatibilityPaths.ModelListPaths)
        {
            webApp.MapGet(path, async (
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
        }

        foreach (var path in OpenAiCompatibilityPaths.ModelDetailPaths)
        {
            webApp.MapGet(path, async (
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
        }

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
                    VSCopilotSwitchApiJsonContext.Default.OllamaChatResponse,
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

        foreach (var path in OpenAiCompatibilityPaths.ChatCompletionPaths)
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

        webApp.MapPost("/internal/models/compare", async (
            ModelComparisonRequest request,
            ModelComparisonService comparisonService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await comparisonService.CompareAsync(request, cancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorMessageResponse(ex.Message));
            }
        });

        webApp.MapGet("/internal/models/compare/history", (ModelComparisonService comparisonService) =>
            Results.Ok(comparisonService.GetHistory()));

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

        webApp.MapGet("/", () => Results.Ok(new
        {
            Name = "VSCopilotSwitch MewUI Native",
            Message = "本进程正在提供本地 Ollama / OpenAI-compatible API；管理界面由 MewUI 原生窗口承载。"
        }));
    }

    private static string[] ResolveServerUrls(IConfiguration configuration)
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

    private static bool IsVs2026AutoHttpsEnabled(IConfiguration configuration)
    {
        var value = FirstNonEmpty(
            Environment.GetEnvironmentVariable("VSCOPILOTSWITCH_VS2026_AUTO_HTTPS"),
            configuration["Vs2026:AutoHttps"]);
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return !value.Equals("false", StringComparison.OrdinalIgnoreCase)
               && !value.Equals("0", StringComparison.OrdinalIgnoreCase)
               && !value.Equals("no", StringComparison.OrdinalIgnoreCase);
    }

    private static string ValidateServerUrl(string configuredUrl)
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

        throw new InvalidOperationException($"监听地址无效：{configuredUrl}。请使用 http://127.0.0.1:5124 这类完整 URL。");
    }

    private static string ResolveStartedServerUrl(WebApplication webApp, string fallbackUrl)
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

    private static bool IsHttpUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

    private static bool IsHttpsUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string FormatCertificateThumbprint(string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            return "未知";
        }

        var clean = thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal);
        return string.Join(':', clean.Chunk(2).Select(chars => new string(chars)));
    }

    private static bool IsTcpPortAvailable(int targetPort)
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

    private static IResult ToOllamaErrorResult(Exception exception)
    {
        var (statusCode, response) = MapOllamaException(exception);
        return new OllamaErrorResult(statusCode, response);
    }

    private static async Task WriteOllamaStreamAsync(
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
                    VSCopilotSwitchApiJsonContext.Default.OllamaChatResponse,
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
                VSCopilotSwitchApiJsonContext.Default.OllamaErrorResponse,
                cancellationToken);
            await httpContext.Response.WriteAsync("\n", cancellationToken);
            await httpContext.Response.Body.FlushAsync(cancellationToken);
        }
    }

    private static async Task WriteOllamaErrorAsync(
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
            VSCopilotSwitchApiJsonContext.Default.OllamaErrorResponse,
            cancellationToken);
    }

    private static async Task HandleOpenAiChatCompletionAsync(
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
                VSCopilotSwitchApiJsonContext.Default.OpenAiChatCompletionResponse,
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

    private static OllamaChatRequest ToOllamaChatRequest(OpenAiChatCompletionRequest request, bool stream)
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

    private static string ExtractOpenAiMessageContent(JsonElement? content)
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

    private static string ExtractOpenAiContentPart(JsonElement part)
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

    private static IReadOnlyList<ChatTool>? ToChatTools(IReadOnlyList<OpenAiTool>? tools)
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

    private static ChatToolChoice? ToChatToolChoice(JsonElement? toolChoice)
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

    private static IReadOnlyList<ChatToolCall>? ToChatToolCalls(IReadOnlyList<OpenAiToolCall>? toolCalls)
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

    private static async Task WriteOpenAiChatCompletionStreamAsync(
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

    private static Task WriteOpenAiRoleChunkAsync(
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

    private static Task WriteOpenAiFinishChunkAsync(
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

    private static async Task WriteOpenAiServerSentEventAsync(
        HttpContext httpContext,
        OpenAiChatCompletionChunk chunk,
        CancellationToken cancellationToken)
    {
        await httpContext.Response.WriteAsync("data: ", cancellationToken);
        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            chunk,
            VSCopilotSwitchApiJsonContext.Default.OpenAiChatCompletionChunk,
            cancellationToken);
        await httpContext.Response.WriteAsync("\n\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteOpenAiErrorServerSentEventAsync(
        HttpContext httpContext,
        OpenAiErrorResponse error,
        CancellationToken cancellationToken)
    {
        await httpContext.Response.WriteAsync("data: ", cancellationToken);
        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            error,
            VSCopilotSwitchApiJsonContext.Default.OpenAiErrorResponse,
            cancellationToken);
        await httpContext.Response.WriteAsync("\n\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteOpenAiErrorAsync(
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
            VSCopilotSwitchApiJsonContext.Default.OpenAiErrorResponse,
            cancellationToken);
    }

    private static IResult ToOpenAiErrorResult(Exception exception)
    {
        var (statusCode, response) = MapOpenAiException(exception);
        return new OpenAiErrorResult(statusCode, response);
    }

    private static (int StatusCode, OpenAiErrorResponse Response) MapOpenAiException(Exception exception)
        => OpenAiErrorMapper.Map(exception);

    private static (int StatusCode, OllamaErrorResponse Response) MapOllamaException(Exception exception)
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

    private static bool IsVsCodeConfigClientError(Exception exception)
        => exception is ArgumentException
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException;

    private static async Task<ManagedOllamaConfig> BuildRuntimeManagedOllamaConfigAsync(
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

    private static string ToVsCodeModelName(string upstreamModel)
    {
        const string suffix = "@vscs";
        var trimmed = upstreamModel.Trim();
        if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"{trimmed}{suffix}";
    }

    private static string NormalizePublicBaseUrl(string baseUrl)
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

    private static string ResolveAppVersion()
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

    private sealed class OllamaErrorResult(int statusCode, OllamaErrorResponse response) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            await JsonSerializer.SerializeAsync(
                httpContext.Response.Body,
                response,
                VSCopilotSwitchApiJsonContext.Default.OllamaErrorResponse,
                httpContext.RequestAborted);
        }
    }

    private sealed class OpenAiErrorResult(int statusCode, OpenAiErrorResponse response) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            await JsonSerializer.SerializeAsync(
                httpContext.Response.Body,
                response,
                VSCopilotSwitchApiJsonContext.Default.OpenAiErrorResponse,
                httpContext.RequestAborted);
        }
    }
}

