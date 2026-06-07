using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using VSCopilotSwitch.Core.Ollama;
using VSCopilotSwitch.Core.Providers;
using VSCopilotSwitch.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("SaveAsync does not return API key", SaveAsync_DoesNotReturnApiKey),
    ("ExportAsync excludes API keys by default", ExportAsync_ExcludesApiKeysByDefault),
    ("ActivateAsync keeps one active provider", ActivateAsync_KeepsOneActiveProvider),
    ("ReorderAsync is idempotent", ReorderAsync_IsIdempotent),
    ("DeleteAsync auto-selects available provider", DeleteAsync_AutoSelectsAvailableProvider),
    ("Copilot compatibility probe covers core workflow", CopilotCompatibilityProbe_CoversCoreWorkflow),
    ("ActiveProvider ListModelsAsync falls back to configured model", ActiveProviderModelProvider_ListModelsAsync_FallsBackToConfiguredModel),
    ("ActiveProvider circuit breaker opens and recovers", ActiveProviderModelProvider_CircuitBreaker_OpensAndRecovers),
    ("Request analytics extracts usage and configured cost", RequestAnalytics_ExtractsUsageAndConfiguredCost),
    ("Request analytics extracts streamed usage", RequestAnalytics_ExtractsStreamedUsage),
    ("Model comparison measures latency stream tool context and cost", ModelComparison_MeasuresLatencyStreamToolContextAndCost),
    ("Model comparison history keeps recent saved results", ModelComparisonHistory_KeepsRecentSavedResults),
    ("OpenAI response mapper emits tool calls", OpenAiResponseMapper_EmitsToolCalls),
    ("OpenAI response mapper emits reasoning content", OpenAiResponseMapper_EmitsReasoningContent),
    ("OpenAI response mapper emits tool call stream deltas", OpenAiResponseMapper_EmitsToolCallStreamDeltas),
    ("OpenAI response mapper emits reasoning stream deltas", OpenAiResponseMapper_EmitsReasoningStreamDeltas),
    ("OpenAI model mapper emits standard model list", OpenAiModelMapper_EmitsStandardModelList),
    ("OpenAI model mapper finds model by id", OpenAiModelMapper_FindsModelById),
    ("OpenAI compatibility paths include /openai/v1 aliases", OpenAiCompatibilityPaths_IncludeOpenAiPrefixAliases),
    ("OpenAI error mapper separates unavailable from rate limit", OpenAiErrorMapper_SeparatesUnavailableFromRateLimit),
    ("Local HTTPS certificate host resolver accepts loopback only", LocalHttpsCertificateHostResolver_AcceptsLoopbackOnly),
    ("Tray menu groups quick switching commands", TrayMenu_GroupsQuickSwitchingCommands),
    ("Tray menu includes window and exit commands", TrayMenu_IncludesWindowAndExitCommands),
    ("Tray menu command activates provider and reports result", TrayMenuCommand_ActivatesProviderAndReportsResult),
    ("UpdateService reads latest release from GitHub", UpdateService_ReadsLatestReleaseFromGitHub),
    ("UpdateService downloads selected asset to cache", UpdateService_DownloadsSelectedAssetToCache)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static async Task SaveAsync_DoesNotReturnApiKey()
{
    using var workspace = TestWorkspace.Create();
    var service = workspace.CreateService();
    const string secret = "sk-provider-secret-1234";

    var views = await service.SaveAsync(CreateSaveRequest("alpha", "Alpha", apiKey: secret, active: true));
    var saved = views.Single(provider => provider.Id == "alpha");
    var publicPayload = string.Join(
        "\n",
        views.Select(provider => $"{provider.Id}|{provider.Name}|{provider.ApiKeyPreview}|{provider.HasApiKey}"));

    Assert.True(saved.HasApiKey, "保存后视图应标记存在 API Key。");
    Assert.Equal("sk-...1234", saved.ApiKeyPreview ?? string.Empty, "视图只能返回脱敏后的 API Key。");
    Assert.DoesNotContain(secret, publicPayload, "保存 API 不应回传密钥原文。");
    Assert.DoesNotContain(secret, File.ReadAllText(workspace.ConfigPath), "配置文件不应保存密钥原文。");
}

static async Task ActivateAsync_KeepsOneActiveProvider()
{
    using var workspace = TestWorkspace.Create();
    var service = workspace.CreateService();

    await service.SaveAsync(CreateSaveRequest("alpha", "Alpha", active: true));
    await service.SaveAsync(CreateSaveRequest("beta", "Beta", active: false));

    var views = await service.ActivateAsync("beta");

    Assert.Equal(1, views.Count(provider => provider.Active), "任意时刻只能有一个启用供应商。");
    Assert.True(views.Single(provider => provider.Id == "beta").Active, "目标供应商应被启用。");
    Assert.True(!views.Single(provider => provider.Id == "alpha").Active, "其他供应商应被关闭。");
}

static async Task ExportAsync_ExcludesApiKeysByDefault()
{
    using var workspace = TestWorkspace.Create();
    var service = workspace.CreateService();
    const string secret = "sk-export-secret-5678";

    await service.SaveAsync(CreateSaveRequest("alpha", "Alpha", apiKey: secret, active: true));

    var exported = await service.ExportAsync();
    var exportText = System.Text.Json.JsonSerializer.Serialize(exported);

    Assert.True(!exported.IncludesSecrets, "默认导出必须标记为不包含密钥。");
    Assert.True(exported.Providers.Single(provider => provider.Id == "alpha").HasApiKey, "导出可以保留密钥存在状态。");
    Assert.DoesNotContain(secret, exportText, "默认导出不应包含 API Key 原文。");
    Assert.DoesNotContain("ApiKeyPreview", exportText, "默认导出不应包含脱敏密钥预览字段。");
    Assert.DoesNotContain("EncryptedApiKey", exportText, "默认导出不应包含加密密文字段。");
    Assert.DoesNotContain("sk-...", exportText, "默认导出不应包含脱敏密钥预览。");
}

static async Task ReorderAsync_IsIdempotent()
{
    using var workspace = TestWorkspace.Create();
    var service = workspace.CreateService();
    var order = new[] { "beta", "gamma", "alpha" };

    await service.SaveAsync(CreateSaveRequest("alpha", "Alpha", active: true));
    await service.SaveAsync(CreateSaveRequest("beta", "Beta", active: false));
    await service.SaveAsync(CreateSaveRequest("gamma", "Gamma", active: false));

    var first = await service.ReorderAsync(new ReorderProvidersRequest(order));
    var second = await service.ReorderAsync(new ReorderProvidersRequest(order));

    Assert.Equal(string.Join(",", order), string.Join(",", first.Select(provider => provider.Id)), "第一次排序结果不正确。");
    Assert.Equal(string.Join(",", first.Select(ToOrderSnapshot)), string.Join(",", second.Select(ToOrderSnapshot)), "重复排序不应产生配置漂移。");
    Assert.Equal("0,1,2", string.Join(",", second.Select(provider => provider.SortOrder)), "排序后 SortOrder 应连续归一。");
}

static async Task DeleteAsync_AutoSelectsAvailableProvider()
{
    using var workspace = TestWorkspace.Create();
    var service = workspace.CreateService();

    await service.SaveAsync(CreateSaveRequest("alpha", "Alpha", active: true));
    await service.SaveAsync(CreateSaveRequest("beta", "Beta", active: false));

    var views = await service.DeleteAsync("alpha");

    Assert.True(views.All(provider => provider.Id != "alpha"), "删除后不应再返回目标供应商。");
    Assert.Equal(1, views.Count(provider => provider.Active), "删除启用供应商后应自动选择一个可用供应商。");
    Assert.True(views.Single(provider => provider.Id == "beta").Active, "剩余供应商应自动启用。");
}

static async Task CopilotCompatibilityProbe_CoversCoreWorkflow()
{
    var probe = new CopilotCompatibilityProbeService(new OllamaProxyService(new ProbeModelProvider()));

    var result = await probe.RunAsync();

    Assert.True(result.Success, "Copilot 兼容探针应通过完整核心工作流。");
    Assert.Equal("passed", result.Steps.Single(step => step.Name == "model_selector").Status, "模型选择器探针应通过。");
    Assert.Equal("passed", result.Steps.Single(step => step.Name == "chat_completion").Status, "普通聊天探针应通过。");
    Assert.Equal("passed", result.Steps.Single(step => step.Name == "agent_tool_call").Status, "Agent 工具调用探针应通过。");
    Assert.Equal("passed", result.Steps.Single(step => step.Name == "stream_finish").Status, "流式结束探针应通过。");
}

static async Task ActiveProviderModelProvider_ListModelsAsync_FallsBackToConfiguredModel()
{
    using var workspace = TestWorkspace.Create();
    var configService = workspace.CreateService();
    await configService.SaveAsync(new SaveProviderConfigRequest(
        "broken",
        "Broken",
        string.Empty,
        "https://example.com/broken",
        "http://127.0.0.1:1",
        "gpt-4.1",
        "openai-compatible",
        "sk-fallback-secret",
        true));
    var activeProvider = new ActiveProviderModelProvider(configService);

    var models = await activeProvider.ListModelsAsync();

    Assert.Equal(1, models.Count, "模型列表失败时应降级为当前已保存模型。");
    Assert.Equal("broken/gpt-4.1", models[0].Name, "降级模型名应保留 Provider 前缀。");
    Assert.Equal("gpt-4.1", models[0].UpstreamModel, "降级模型应使用已保存的上游模型名。");
}

static async Task ActiveProviderModelProvider_CircuitBreaker_OpensAndRecovers()
{
    var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    var configService = new StubProviderConfigService([
        new ProviderConfigView(
            "flaky",
            "Flaky",
            string.Empty,
            "https://example.com/flaky",
            "https://api.example.com",
            "gpt-4.1",
            "openai-compatible",
            "F",
            true,
            true,
            "sk-...test",
            0)
    ]);
    var upstream = new FlakyModelProvider();
    var activeProvider = new ActiveProviderModelProvider(configService, _ => upstream, () => now);
    var request = new ChatRequest("gpt-4.1", [new ChatMessage("user", "ping")], stream: false);

    for (var attempt = 0; attempt < 3; attempt++)
    {
        await Assert.ThrowsAsync<ProviderException>(() => activeProvider.ChatAsync(request), "熔断打开前应透传上游失败。");
    }

    var openException = await Assert.ThrowsAsync<ProviderException>(() => activeProvider.ChatAsync(request), "达到阈值后应直接拒绝请求。");
    Assert.Equal(3, upstream.ChatAttempts, "熔断打开后不应继续调用上游。");
    Assert.Equal(ProviderErrorKind.Unavailable, openException.Kind, "熔断拒绝应映射为提供商不可用。");

    upstream.Fail = false;
    now = now.AddMinutes(2);
    var response = await activeProvider.ChatAsync(request);

    Assert.Equal("pong", response.Content, "半开探测成功后应返回上游响应。");
    Assert.Equal(4, upstream.ChatAttempts, "半开恢复应只放行一次探测请求。");

    response = await activeProvider.ChatAsync(request);
    Assert.Equal("pong", response.Content, "探测成功后熔断器应自动恢复闭合。");
    Assert.Equal(5, upstream.ChatAttempts, "闭合后应正常调用上游。");
}

static async Task RequestAnalytics_ExtractsUsageAndConfiguredCost()
{
    var analytics = CreateAnalyticsService(new UsagePriceRule
    {
        ModelPattern = "gpt-5.5",
        Label = "gpt-5.5-test",
        InputPerMillionTokens = 2m,
        OutputPerMillionTokens = 10m
    });
    var context = CreateAnalyticsContext(
        "/v1/chat/completions",
        """{"model":"gpt-5.5@vscs","messages":[{"role":"user","content":"ping"}]}""");

    await analytics.InvokeAsync(context, async () =>
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("""
            {
              "choices": [
                { "message": { "role": "assistant", "content": "pong" }, "finish_reason": "stop" }
              ],
              "usage": {
                "prompt_tokens": 1000000,
                "completion_tokens": 500000,
                "total_tokens": 1500000
              }
            }
            """);
    });

    var snapshot = analytics.GetSnapshot("http://127.0.0.1:5124");
    var entry = snapshot.Requests.Single();
    Assert.Equal(1000000, entry.InputTokens, "分析统计应优先使用上游 prompt_tokens。");
    Assert.Equal(500000, entry.OutputTokens, "分析统计应优先使用上游 completion_tokens。");
    Assert.Equal(1500000, entry.TotalTokens, "分析统计应保留上游 total_tokens。");
    Assert.Equal("provider", entry.UsageSource, "解析到上游 usage 时应标记为 provider。");
    Assert.Equal(7m, entry.Cost, "费用应按每百万输入/输出 Token 单价精算。");
    Assert.Equal("configured", entry.CostSource, "命中单价表时应标记为 configured。");
    Assert.Equal("gpt-5.5-test", entry.PricingRule ?? string.Empty, "费用来源应显示命中的单价规则。");
    Assert.Equal(7m, snapshot.Summary.TotalCost, "汇总费用应累加请求费用。");
    Assert.Equal(1, snapshot.Summary.PricedRequests, "汇总应统计已计价请求。");
}

static async Task RequestAnalytics_ExtractsStreamedUsage()
{
    var analytics = CreateAnalyticsService(new UsagePriceRule
    {
        ModelPattern = "deepseek-*",
        InputPerMillionTokens = 1m,
        OutputPerMillionTokens = 3m
    });
    var context = CreateAnalyticsContext(
        "/v1/chat/completions",
        """{"model":"deepseek-v4@vscs","stream":true,"messages":[{"role":"user","content":"ping"}]}""",
        "text/event-stream");

    await analytics.InvokeAsync(context, async () =>
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        await context.Response.WriteAsync("""
            data: {"choices":[{"delta":{"content":"pong"}}]}

            data: {"choices":[{"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":2000000,"completion_tokens":1000000,"total_tokens":3000000}}

            data: [DONE]

            """);
    });

    var entry = analytics.GetSnapshot("http://127.0.0.1:5124").Requests.Single();
    Assert.Equal(2000000, entry.InputTokens, "流式 SSE 中的 usage.prompt_tokens 应被解析。");
    Assert.Equal(1000000, entry.OutputTokens, "流式 SSE 中的 usage.completion_tokens 应被解析。");
    Assert.Equal("provider", entry.UsageSource, "流式解析到上游 usage 时应标记为 provider。");
    Assert.Equal(5m, entry.Cost, "流式费用应按配置单价精算。");
}

static async Task OpenAiCompatibilityPaths_IncludeOpenAiPrefixAliases()
{
    Assert.True(
        OpenAiCompatibilityPaths.ModelListPaths.Contains("/openai/v1/models", StringComparer.OrdinalIgnoreCase),
        "OpenAI-compatible 模型列表入口应兼容 /openai/v1/models。");
    Assert.True(
        OpenAiCompatibilityPaths.ModelDetailPaths.Contains("/openai/v1/models/{modelId}", StringComparer.OrdinalIgnoreCase),
        "OpenAI-compatible 单模型入口应兼容 /openai/v1/models/{modelId}。");
    Assert.True(
        OpenAiCompatibilityPaths.ChatCompletionPaths.Contains("/openai/v1/chat/completions", StringComparer.OrdinalIgnoreCase),
        "OpenAI-compatible 聊天入口应兼容 /openai/v1/chat/completions。");
    Assert.True(
        OpenAiCompatibilityPaths.IsChatCompletionPath("/openai/v1/chat/completions"),
        "请求分析和路由识别应把 /openai/v1/chat/completions 当作聊天入口。");

    var analytics = CreateAnalyticsService();
    var context = CreateAnalyticsContext(
        "/openai/v1/chat/completions",
        """{"model":"gpt-5.5@vscs","messages":[{"role":"user","content":"ping"}]}""");

    await analytics.InvokeAsync(context, async () =>
    {
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("""{"error":{"message":"method not allowed"}}""");
    });

    var entry = analytics.GetSnapshot("http://127.0.0.1:5124").Requests.Single();
    Assert.Equal("/openai/v1/chat/completions", entry.Path, "分析统计应保留真实命中的 /openai/v1 路径。");
    Assert.Equal("gpt-5.5@vscs", entry.Model ?? string.Empty, "分析统计应从 /openai/v1 聊天请求体中解析模型名。");
}

static Task OpenAiResponseMapper_EmitsToolCalls()
{
    var toolCall = new ChatToolCall(
        "call_lookup",
        "function",
        new ChatFunctionCall("lookup", """{"query":"pong"}"""));
    var completion = OpenAiChatCompletionMapper.CreateResponse(
        "chatcmpl-test",
        1,
        "gpt-5.5@vscs",
        new OllamaChatResponse(
            "gpt-5.5@vscs",
            DateTimeOffset.UnixEpoch,
            new OllamaChatMessage("assistant", string.Empty, new[] { toolCall }),
            "tool_calls",
            true,
            new ChatUsage(10, 3, 13)));

    var choice = completion.Choices[0];
    Assert.Equal("tool_calls", choice.FinishReason, "工具调用响应应使用 OpenAI-compatible finish_reason。");
    Assert.True(choice.Message.Content is null, "只有工具调用且无文本时，message.content 应保持 null。");
    Assert.Equal("call_lookup", choice.Message.ToolCalls?[0].Id ?? string.Empty, "工具调用 id 应回传给 Copilot。");
    Assert.Equal("lookup", choice.Message.ToolCalls?[0].Function?.Name ?? string.Empty, "工具调用函数名应回传给 Copilot。");
    Assert.Equal(13, completion.Usage?.TotalTokens ?? 0, "usage 应回传给 Copilot。");

    using var document = JsonDocument.Parse(SerializeOpenAi(completion));
    var message = document.RootElement.GetProperty("choices")[0].GetProperty("message");
    Assert.Equal(JsonValueKind.Null.ToString(), message.GetProperty("content").ValueKind.ToString(), "序列化后的 message.content 应显式为 null。");
    Assert.Equal("call_lookup", message.GetProperty("tool_calls")[0].GetProperty("id").GetString() ?? string.Empty, "序列化后的 tool_calls 应保留 id。");
    return Task.CompletedTask;
}

static Task OpenAiResponseMapper_EmitsReasoningContent()
{
    var completion = OpenAiChatCompletionMapper.CreateResponse(
        "chatcmpl-test",
        1,
        "deepseek-v4@vscs",
        new OllamaChatResponse(
            "deepseek-v4@vscs",
            DateTimeOffset.UnixEpoch,
            new OllamaChatMessage("assistant", string.Empty, ReasoningContent: "先分析调用路径。"),
            "stop",
            true));

    var choice = completion.Choices[0];
    Assert.True(choice.Message.Content is null, "只有 reasoning_content 且无文本时，message.content 应保持 null。");
    Assert.Equal("先分析调用路径。", choice.Message.ReasoningContent ?? string.Empty, "reasoning_content 应回传给 Copilot。");

    using var document = JsonDocument.Parse(SerializeOpenAi(completion));
    var message = document.RootElement.GetProperty("choices")[0].GetProperty("message");
    Assert.Equal(JsonValueKind.Null.ToString(), message.GetProperty("content").ValueKind.ToString(), "序列化后的 reasoning-only message.content 应显式为 null。");
    Assert.Equal("先分析调用路径。", message.GetProperty("reasoning_content").GetString() ?? string.Empty, "序列化后的 reasoning_content 应保留。");
    return Task.CompletedTask;
}

static Task OpenAiResponseMapper_EmitsToolCallStreamDeltas()
{
    var argumentsDelta = new ChatToolCall(
        string.Empty,
        "function",
        new ChatFunctionCall(string.Empty, """{"query":"stream"}"""),
        Index: 0);
    var chunk = OpenAiChatCompletionMapper.CreateDeltaChunk(
        "chatcmpl-test",
        1,
        "gpt-5.5@vscs",
        new OllamaChatResponse(
            "gpt-5.5@vscs",
            DateTimeOffset.UnixEpoch,
            new OllamaChatMessage("assistant", string.Empty, new[] { argumentsDelta }),
            null,
            false));

    var deltaToolCall = chunk.Choices[0].Delta.ToolCalls?[0];
    Assert.True(deltaToolCall is not null, "流式工具参数分块应保留 tool_calls delta。");
    Assert.True(deltaToolCall!.Id is null, "纯 arguments delta 不应伪造 tool_call id。");
    Assert.True(deltaToolCall.Type is null, "纯 arguments delta 不应重复输出 type。");
    Assert.True(deltaToolCall.Function?.Name is null, "纯 arguments delta 不应伪造函数名。");
    Assert.Equal("""{"query":"stream"}""", deltaToolCall.Function?.Arguments ?? string.Empty, "流式工具参数 delta 应原样回传。");

    using var document = JsonDocument.Parse(SerializeOpenAi(chunk));
    var delta = document.RootElement.GetProperty("choices")[0].GetProperty("delta");
    Assert.True(!delta.TryGetProperty("content", out _), "流式工具参数 delta 不应输出空 content 字段。");
    var serializedToolCall = delta.GetProperty("tool_calls")[0];
    Assert.True(!serializedToolCall.TryGetProperty("id", out _), "序列化后的纯 arguments delta 不应包含 id。");
    Assert.True(!serializedToolCall.TryGetProperty("type", out _), "序列化后的纯 arguments delta 不应包含 type。");
    Assert.Equal("""{"query":"stream"}""", serializedToolCall.GetProperty("function").GetProperty("arguments").GetString() ?? string.Empty, "序列化后的 arguments delta 应保留。");
    return Task.CompletedTask;
}

static Task OpenAiResponseMapper_EmitsReasoningStreamDeltas()
{
    var chunk = OpenAiChatCompletionMapper.CreateDeltaChunk(
        "chatcmpl-test",
        1,
        "deepseek-reasoner@vscs",
        new OllamaChatResponse(
            "deepseek-reasoner@vscs",
            DateTimeOffset.UnixEpoch,
            new OllamaChatMessage("assistant", string.Empty, ReasoningContent: "先思考。"),
            null,
            false));

    var delta = chunk.Choices[0].Delta;
    Assert.True(delta.Content is null, "只有 reasoning_content 的流式 delta 不应输出空字符串 content。");
    Assert.Equal("先思考。", delta.ReasoningContent ?? string.Empty, "流式 reasoning_content 应回传给 OpenAI-compatible 客户端。");

    using var document = JsonDocument.Parse(SerializeOpenAi(chunk));
    var serializedDelta = document.RootElement.GetProperty("choices")[0].GetProperty("delta");
    Assert.True(!serializedDelta.TryGetProperty("content", out _), "序列化后的 reasoning-only delta 不应输出空 content 字段。");
    Assert.Equal("先思考。", serializedDelta.GetProperty("reasoning_content").GetString() ?? string.Empty, "序列化后的 reasoning_content 应保留。");
    return Task.CompletedTask;
}

static Task OpenAiModelMapper_EmitsStandardModelList()
{
    var modifiedAt = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
    var tags = new OllamaTagsResponse(new[]
    {
        new OllamaModelInfo(
            "gpt-5.5@vscs",
            "gpt-5.5@vscs",
            modifiedAt,
            0,
            "vscopilotswitch:openai:gpt-5.5",
            new OllamaModelDetails("gpt-5.5", "provider-adapter", "llama", new[] { "llama", "openai" }, "remote", "remote"),
            new[] { "completion", "chat", "tools" },
            400000,
            new Dictionary<string, object>
            {
                ["vscopilotswitch.provider"] = "openai",
                ["general.basename"] = "gpt-5.5"
            },
            SupportsToolCalling: true,
            SupportsVision: false,
            SupportsThinking: false,
            SupportsReasoning: false)
    });

    var response = OpenAiModelMapper.CreateListResponse(tags);

    Assert.Equal("list", response.Object, "OpenAI 模型列表 object 应为 list。");
    Assert.Equal(1, response.Data.Count, "模型列表应包含当前可调用模型。");
    Assert.Equal("gpt-5.5@vscs", response.Data[0].Id, "OpenAI-compatible 模型 id 应使用可直接调用的 @vscs 名称。");
    Assert.Equal("model", response.Data[0].Object, "模型条目 object 应为 model。");
    Assert.Equal(1_800_000_000, response.Data[0].Created, "模型条目 created 应来自 Ollama tags modified_at。");
    Assert.Equal("openai", response.Data[0].OwnedBy, "模型条目 owned_by 应优先使用 Provider 名称。");
    return Task.CompletedTask;
}

static Task OpenAiModelMapper_FindsModelById()
{
    var tags = new OllamaTagsResponse(new[]
    {
        new OllamaModelInfo(
            "gpt-5.5@vscs",
            "gpt-5.5@vscs",
            DateTimeOffset.FromUnixTimeSeconds(1_800_000_000),
            0,
            "vscopilotswitch:openai:gpt-5.5",
            new OllamaModelDetails("gpt-5.5", "provider-adapter", "llama", new[] { "llama", "openai" }, "remote", "remote"),
            new[] { "completion", "chat", "tools" },
            400000,
            new Dictionary<string, object>
            {
                ["vscopilotswitch.provider"] = "openai"
            },
            SupportsToolCalling: true,
            SupportsVision: false,
            SupportsThinking: false,
            SupportsReasoning: false)
    });

    var found = OpenAiModelMapper.FindModel(tags, "GPT-5.5@VSCS");
    var missing = OpenAiModelMapper.FindModel(tags, "missing@vscs");

    Assert.True(found is not null, "VS2026 Azure BYOM 校验 /v1/models/{id} 时应能按模型 id 忽略大小写匹配。 ");
    Assert.Equal("gpt-5.5@vscs", found!.Id, "返回的模型 id 应保持公开可调用名称。 ");
    Assert.True(missing is null, "未知模型应返回空，HTTP 层会映射为 404。 ");
    return Task.CompletedTask;
}

static Task OpenAiErrorMapper_SeparatesUnavailableFromRateLimit()
{
    var (unavailableStatusCode, unavailableResponse) = OpenAiErrorMapper.Map(new OllamaProxyException(
        OllamaProxyErrorKind.ProviderUnavailable,
        "provider_unavailable",
        "sub2api 网络请求失败，请检查 API 地址和网络连接。"));
    var (rateLimitedStatusCode, rateLimitedResponse) = OpenAiErrorMapper.Map(new OllamaProxyException(
        OllamaProxyErrorKind.ProviderRateLimited,
        "provider_rate_limited",
        "sub2api 请求过于频繁，请稍后重试。"));

    Assert.Equal(StatusCodes.Status502BadGateway, unavailableStatusCode, "OpenAI-compatible 出口不应把 Provider 网络不可用返回为 503，避免 Copilot 误判成限流。");
    Assert.Equal("api_error", unavailableResponse.Error.Type, "Provider 网络不可用应按上游 API 故障暴露。");
    Assert.Equal("provider_unavailable", unavailableResponse.Error.Code, "Provider 网络不可用应保留精确错误码。");
    Assert.Equal(StatusCodes.Status429TooManyRequests, rateLimitedStatusCode, "真实 Provider 限流仍应返回 429。");
    Assert.Equal("rate_limit_error", rateLimitedResponse.Error.Type, "真实 Provider 限流仍应使用 OpenAI rate_limit_error。");
    Assert.Equal("provider_rate_limited", rateLimitedResponse.Error.Code, "真实 Provider 限流应保留精确错误码。");
    return Task.CompletedTask;
}

static Task LocalHttpsCertificateHostResolver_AcceptsLoopbackOnly()
{
    var hosts = LocalHttpsCertificateService.ResolveLoopbackHttpsHosts(new[]
    {
        "http://127.0.0.1:5124",
        "https://localhost:5443",
        "https://127.0.0.1:5444"
    });

    Assert.Equal("127.0.0.1,localhost", string.Join(",", hosts), "本地证书只应收集 HTTPS 回环地址。");

    try
    {
        LocalHttpsCertificateService.ResolveLoopbackHttpsHosts(new[] { "https://example.com:5443" });
        throw new InvalidOperationException("非回环 HTTPS 地址不应允许自动证书。");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("回环地址", StringComparison.Ordinal))
    {
    }

    return Task.CompletedTask;
}


static async Task TrayMenu_GroupsQuickSwitchingCommands()
{
    await Task.CompletedTask;
    var configService = new StubProviderConfigService([
        CreateProviderView("alpha", "Alpha", "gpt-5.5", active: true),
        CreateProviderView("beta", "Beta", "claude-sonnet-4", active: false),
        CreateProviderView("draft", "Draft", string.Empty, active: false)
    ]);
    var tray = new TrayMenuService(configService);

    var menu = tray.GetMenuItems();

    Assert.True(menu.Any(item => item.Text == "快速切换" && !item.Enabled), "托盘菜单应包含快速切换分组标题。");
    Assert.True(menu.Any(item => item.CommandId == "activate-provider:alpha" && item.Checked && !item.Enabled), "当前供应商应勾选且不可重复启用。");
    Assert.True(menu.Any(item => item.CommandId == "activate-provider:beta" && item.Enabled && !item.Checked), "可用供应商应提供启用命令。");
    Assert.True(menu.Any(item => item.CommandId == "activate-provider:draft" && !item.Enabled && item.Text.Contains("缺少模型")), "配置不完整的供应商应保留但禁用并说明原因。");
    Assert.True(menu.Any(item => item.CommandId == "refresh-dashboard" && item.Enabled), "托盘菜单应提供刷新主界面命令。");
    Assert.True(menu.Any(item => item.CommandId == "open-providers" && item.Enabled), "托盘菜单应提供打开供应商页命令。");
}

static async Task TrayMenu_IncludesWindowAndExitCommands()
{
    await Task.CompletedTask;
    var configService = new StubProviderConfigService([
        CreateProviderView("alpha", "Alpha", "gpt-5.5", active: true)
    ]);
    var tray = new TrayMenuService(configService);

    var menu = tray.GetMenuItems();

    Assert.True(menu.Any(item => item.CommandId == TrayMenuService.ShowWindowCommand && item.Enabled), "托盘菜单应支持打开或聚焦主窗口。");
    Assert.True(menu.Any(item => item.CommandId == TrayMenuService.ExitCommand && item.Enabled), "托盘菜单应支持退出程序。");
}

static async Task TrayMenuCommand_ActivatesProviderAndReportsResult()
{
    var configService = new StubProviderConfigService([
        CreateProviderView("alpha", "Alpha", "gpt-5.5", active: true),
        CreateProviderView("beta", "Beta", "gpt-5.5", active: false)
    ]);
    var tray = new TrayMenuService(configService);

    var result = await tray.HandleCommandAsync("activate-provider:beta", CancellationToken.None);

    var providers = await configService.ListAsync();
    Assert.True(result.Handled, "启用供应商命令应返回已处理。");
    Assert.Equal("beta", result.ActiveProviderId ?? string.Empty, "命令结果应返回启用后的供应商 ID。");
    Assert.True(result.Message.Contains("Beta"), "命令结果应返回面向托盘状态的提示。");
    Assert.True(providers.Single(provider => provider.Id == "beta").Active, "托盘命令应切换活动供应商。");
    Assert.True(!providers.Single(provider => provider.Id == "alpha").Active, "切换后原供应商应关闭。");
}

static async Task UpdateService_ReadsLatestReleaseFromGitHub()
{
    using var workspace = TestWorkspace.Create();
    var service = workspace.CreateUpdateService(new Dictionary<string, string>
    {
        ["https://example.test/github"] = """
            {
              "tag_name": "v1.3.0",
              "name": "GitHub 1.3.0",
              "published_at": "2026-02-01T00:00:00Z",
              "assets": [
                {
                  "name": "VSCopilotSwitch-win-x64-aot.zip",
                  "browser_download_url": "https://example.test/download/github.zip",
                  "size": 2048
                }
              ]
            }
            """
    });

    var result = await service.CheckAsync();

    Assert.True(result.UpdateAvailable, "发现更高版本时应标记可更新。");
    Assert.Equal("1.3.0", result.LatestRelease?.Version ?? string.Empty, "应读取 GitHub Release 最新版本。");
    Assert.Equal("GitHub", result.LatestRelease?.SourceName ?? string.Empty, "自动更新来源应为 GitHub。");
    Assert.Equal("https://example.test/download/github.zip", result.LatestRelease?.Asset?.DownloadUrl ?? string.Empty, "应选择 GitHub Release 资产下载地址。");
}

static async Task UpdateService_DownloadsSelectedAssetToCache()
{
    using var workspace = TestWorkspace.Create();
    var payload = "fake update package";
    var service = workspace.CreateUpdateService(new Dictionary<string, string>
    {
        ["https://example.test/github"] = """
            {
              "tag_name": "v1.2.0",
              "name": "GitHub 1.2.0",
              "published_at": "2026-01-01T00:00:00Z",
              "assets": [
                {
                  "name": "VSCopilotSwitch-win-x64-aot.zip",
                  "browser_download_url": "https://example.test/download/update.zip",
                  "size": 19
                }
              ]
            }
            """,
        ["https://example.test/download/update.zip"] = payload
    });

    var result = await service.DownloadLatestAsync(new UpdateDownloadRequest());

    Assert.True(result.Downloaded, "发现新版本资产时应下载到本地缓存。");
    Assert.True(File.Exists(result.FilePath), "下载结果应返回实际存在的文件。");
    Assert.Equal(payload, File.ReadAllText(result.FilePath!), "下载文件内容应来自 Release 资产。");
}


static async Task ModelComparison_MeasuresLatencyStreamToolContextAndCost()
{
    var service = new ModelComparisonService(new ProbeComparisonModelProvider(), new UsageCostEstimator(Options.Create(new UsagePricingOptions
    {
        Currency = "USD",
        Models =
        [
            new UsagePriceRule
            {
                ModelPattern = "gpt-4.1",
                Label = "gpt-4.1-test",
                InputPerMillionTokens = 1m,
                OutputPerMillionTokens = 3m
            }
        ]
    })));

    var result = await service.CompareAsync(new ModelComparisonRequest("probe", "gpt-4.1", RunToolProbe: true));

    Assert.Equal("probe", result.ProviderName, "比较结果应保留供应商名。");
    Assert.Equal("gpt-4.1", result.Model, "比较结果应保留模型名。");
    Assert.True(result.Success, "探针全部通过时比较应成功。");
    Assert.True(result.LatencyMs >= 0, "应记录普通响应延迟。");
    Assert.True(result.FirstTokenMs >= 0, "应记录首 token 延迟。");
    Assert.True(result.StreamFinished, "应记录流式结束是否完成。");
    Assert.True(result.ToolProbePassed == true, "支持工具调用时工具探针应通过。");
    Assert.Equal(128000, result.ContextLength ?? 0, "应读取模型上下文声明。");
    Assert.Equal("USD", result.Currency, "费用估算应保留币种。");
    Assert.Equal("configured", result.CostSource, "命中价格规则时应标记 configured。");
    Assert.Equal("gpt-4.1-test", result.PricingRule ?? string.Empty, "应返回命中的价格规则。");
    Assert.True(result.EstimatedCost > 0m, "应按 usage 估算费用。");
    Assert.True(result.Steps.Count >= 4, "应保留分步探针结果。");
}

static async Task ModelComparisonHistory_KeepsRecentSavedResults()
{
    var service = new ModelComparisonService(new ProbeComparisonModelProvider(), new UsageCostEstimator(Options.Create(new UsagePricingOptions())));

    var first = await service.CompareAsync(new ModelComparisonRequest("probe", "gpt-4.1", SaveResult: true));
    var second = await service.CompareAsync(new ModelComparisonRequest("probe", "gpt-4.1-mini", SaveResult: true));

    var history = service.GetHistory();

    Assert.Equal(2, history.Count, "保存结果后历史应包含两条记录。");
    Assert.Equal(second.Id, history[0].Id, "最新结果应排在最前。");
    Assert.Equal(first.Id, history[1].Id, "旧结果应保留在后。");
}


static ProviderConfigView CreateProviderView(string id, string name, string model, bool active)
    => new(
        id,
        name,
        string.Empty,
        $"https://example.com/{id}",
        $"https://api.example.com/{id}",
        model,
        "openai-compatible",
        name[..1],
        active,
        true,
        "sk-...0000",
        id == "alpha" ? 0 : id == "beta" ? 1 : 2);

static SaveProviderConfigRequest CreateSaveRequest(
    string id,
    string name,
    string model = "gpt-5.5",
    string apiKey = "sk-test-0000",
    bool active = false)
    => new(
        id,
        name,
        string.Empty,
        $"https://example.com/{id}",
        $"https://api.example.com/{id}",
        model,
        "openai-compatible",
        apiKey,
        active);

static string ToOrderSnapshot(ProviderConfigView provider)
    => $"{provider.Id}:{provider.SortOrder}:{provider.Active}";

static string SerializeOpenAi<T>(T value)
    => value switch
    {
        OpenAiChatCompletionResponse response => JsonSerializer.Serialize(response, TestJsonContext.Default.OpenAiChatCompletionResponse),
        OpenAiChatCompletionChunk chunk => JsonSerializer.Serialize(chunk, TestJsonContext.Default.OpenAiChatCompletionChunk),
        _ => JsonSerializer.Serialize(value)
    };

static RequestAnalyticsService CreateAnalyticsService(params UsagePriceRule[] rules)
    => new(new UsageCostEstimator(Options.Create(new UsagePricingOptions
    {
        Currency = "USD",
        Models = rules.ToList()
    })));

static DefaultHttpContext CreateAnalyticsContext(
    string path,
    string body,
    string responseContentType = "application/json")
{
    var context = new DefaultHttpContext();
    var bytes = Encoding.UTF8.GetBytes(body);
    context.Request.Method = "POST";
    context.Request.Path = path;
    context.Request.ContentType = "application/json";
    context.Request.ContentLength = bytes.Length;
    context.Request.Body = new MemoryStream(bytes);
    context.Request.Headers.UserAgent = "AnalyticsTests/1.0";
    context.Response.Body = new MemoryStream();
    context.Response.ContentType = responseContentType;
    return context;
}

internal sealed class TestWorkspace : IDisposable
{
    private TestWorkspace(string root)
    {
        Root = root;
        ConfigPath = Path.Combine(root, "providers.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(ConfigPath, """
            {
              "Version": 1,
              "Providers": []
            }
            """);
    }

    public string Root { get; }

    public string ConfigPath { get; }

    public static TestWorkspace Create()
        => new(Path.Combine(Path.GetTempPath(), "VSCopilotSwitch.Services.Tests", Guid.NewGuid().ToString("N")));

    public ProviderConfigService CreateService()
        => new(ConfigPath);

    public UpdateService CreateUpdateService(IReadOnlyDictionary<string, string> responses)
    {
        var handler = new StubHttpMessageHandler(responses);
        return new UpdateService(
            new HttpClient(handler),
            new UpdateOptions
            {
                CacheDirectory = Path.Combine(Root, "updates"),
                CurrentVersionOverride = "1.0.0",
                Sources =
                [
                    new UpdateSourceOptions
                    {
                        Name = "GitHub",
                        Kind = "GitHub",
                        ApiUrl = "https://example.test/github"
                    }
                ]
            },
            () => "1.0.0");
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}


internal sealed class StubProviderConfigService : IProviderConfigService
{
    private List<ProviderConfigView> _providers;

    public StubProviderConfigService(IEnumerable<ProviderConfigView> providers)
    {
        _providers = providers.ToList();
    }

    public Task<IReadOnlyList<ProviderConfigView>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ProviderConfigView>>(_providers);

    public Task<ProviderRuntimeConfig?> GetActiveRuntimeConfigAsync(CancellationToken cancellationToken = default)
    {
        var active = _providers.FirstOrDefault(provider => provider.Active);
        return Task.FromResult(active is null ? null : new ProviderRuntimeConfig(active.Id, active.Name, active.ApiUrl, active.Model, active.Vendor, "sk-test"));
    }

    public Task<ProviderAdapterConfig> BuildConnectionTestConfigAsync(TestProviderConnectionRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new ProviderAdapterConfig(request.Id ?? "test", request.Name ?? "Test", request.ApiUrl ?? "https://api.example.com", request.Model ?? "gpt-5.5", request.Vendor ?? "openai-compatible", request.ApiKey ?? "sk-test"));

    public Task<IReadOnlyList<ProviderConfigView>> SaveAsync(SaveProviderConfigRequest request, CancellationToken cancellationToken = default)
        => ListAsync(cancellationToken);

    public Task<IReadOnlyList<ProviderConfigView>> DeleteAsync(string providerId, CancellationToken cancellationToken = default)
        => ListAsync(cancellationToken);

    public Task<IReadOnlyList<ProviderConfigView>> ActivateAsync(string providerId, CancellationToken cancellationToken = default)
    {
        _providers = _providers
            .Select(provider => provider with { Active = string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase) })
            .ToList();
        return ListAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ProviderConfigView>> ReorderAsync(ReorderProvidersRequest request, CancellationToken cancellationToken = default)
        => ListAsync(cancellationToken);

    public Task<ProviderConfigExportDocument> ExportAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new ProviderConfigExportDocument(1, DateTimeOffset.UtcNow, false, []));
}

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly IReadOnlyDictionary<string, string> _responses;

    public StubHttpMessageHandler(IReadOnlyDictionary<string, string> responses)
    {
        _responses = responses;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.AbsoluteUri ?? string.Empty;
        if (!_responses.TryGetValue(url, out var content))
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }

        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(content)
        });
    }
}

internal sealed class ProbeModelProvider : IModelProvider
{
    private readonly ProviderModel _model = new(
        "probe/gpt-4.1",
        "openai",
        "gpt-4.1",
        "GPT 4.1",
        Capabilities: new ProviderModelCapabilities(SupportsTools: true, SupportsVision: true));

    public string Name => "probe";

    public Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ProviderModel>>(new[] { _model });

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Tools is { Count: > 0 })
        {
            var toolCall = new ChatToolCall(
                "call_probe_lookup",
                "function",
                new ChatFunctionCall("lookup", """{"query":"ping"}"""));
            return Task.FromResult(new ChatResponse(request.Model, string.Empty, "tool_calls", new[] { toolCall }));
        }

        return Task.FromResult(new ChatResponse(request.Model, "pong"));
    }

    public async IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new ChatStreamChunk(request.Model, "pong", Done: false);
        yield return new ChatStreamChunk(request.Model, string.Empty, Done: true, "stop");
    }
}


internal sealed class FlakyModelProvider : IModelProvider
{
    public bool Fail { get; set; } = true;

    public int ChatAttempts { get; private set; }

    public string Name => "flaky";

    public Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ProviderModel>>([
            new ProviderModel("flaky/gpt-4.1", "flaky", "gpt-4.1", "GPT 4.1", ["gpt-4.1"])
        ]);

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        ChatAttempts++;
        if (Fail)
        {
            throw new ProviderException(ProviderErrorKind.Unavailable, "flaky unavailable");
        }

        return Task.FromResult(new ChatResponse(request.Model, "pong"));
    }

    public async IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (Fail)
        {
            throw new ProviderException(ProviderErrorKind.Unavailable, "flaky unavailable");
        }

        yield return new ChatStreamChunk(request.Model, "pong", Done: true, "stop");
    }
}

internal sealed class ProbeComparisonModelProvider : IModelProvider
{
    public string Name => "probe";

    public Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ProviderModel>>([
            new ProviderModel(
                "probe/gpt-4.1",
                "probe",
                "gpt-4.1",
                "GPT 4.1",
                Capabilities: new ProviderModelCapabilities(SupportsTools: true, SupportsVision: true, ContextLength: 128000)),
            new ProviderModel(
                "probe/gpt-4.1-mini",
                "probe",
                "gpt-4.1-mini",
                "GPT 4.1 Mini",
                Capabilities: new ProviderModelCapabilities(SupportsTools: true, ContextLength: 64000))
        ]);

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Tools is { Count: > 0 })
        {
            var toolCall = new ChatToolCall(
                "call_compare_lookup",
                "function",
                new ChatFunctionCall("lookup", "{}"));
            return Task.FromResult(new ChatResponse(request.Model, string.Empty, "tool_calls", [toolCall], new ChatUsage(1000, 100, 1100)));
        }

        return Task.FromResult(new ChatResponse(request.Model, "pong", Usage: new ChatUsage(1000, 250, 1250)));
    }

    public async IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new ChatStreamChunk(request.Model, "p", Done: false);
        yield return new ChatStreamChunk(request.Model, "ong", Done: false);
        yield return new ChatStreamChunk(request.Model, string.Empty, Done: true, "stop", Usage: new ChatUsage(900, 120, 1020));
    }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OpenAiChatCompletionResponse))]
[JsonSerializable(typeof(OpenAiChatCompletionChunk))]
internal sealed partial class TestJsonContext : JsonSerializerContext;

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Equal(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{message} 预期：{expected}，实际：{actual}");
        }
    }

    public static void Equal(int expected, int actual, string message)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"{message} 预期：{expected}，实际：{actual}");
        }
    }

    public static void Equal(decimal expected, decimal actual, string message)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"{message} 预期：{expected}，实际：{actual}");
        }
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action, string message)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException ex)
        {
            return ex;
        }

        throw new InvalidOperationException(message);
    }

    public static void Equal(ProviderErrorKind expected, ProviderErrorKind actual, string message)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"{message} 预期：{expected}，实际：{actual}");
        }
    }

    public static void DoesNotContain(string unexpectedSubstring, string actual, string message)
    {
        if (actual.Contains(unexpectedSubstring, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}
