using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using VSCopilotSwitch.Core.Ollama;
using VSCopilotSwitch.Core.Providers;
using VSCopilotSwitch.Core.Providers.Sub2Api;

var tests = new (string Name, Func<Task> Run)[]
{
    ("ChatAsync routes aliases to upstream model", ChatAsync_RoutesAliasesToUpstreamModel),
    ("ChatStreamAsync emits chunks and final done", ChatStreamAsync_EmitsChunksAndFinalDone),
    ("ChatAsync rejects unknown model", ChatAsync_RejectsUnknownModel),
    ("ChatAsync rejects ambiguous alias", ChatAsync_RejectsAmbiguousAlias),
    ("ChatAsync maps provider errors", ChatAsync_MapsProviderErrors),
    ("Sub2Api ListModelsAsync fetches OpenAI-compatible models", Sub2Api_ListModelsAsync_FetchesOpenAiCompatibleModels),
    ("Sub2Api ChatAsync sends upstream model", Sub2Api_ChatAsync_SendsUpstreamModel),
    ("Sub2Api ChatStreamAsync parses SSE chunks", Sub2Api_ChatStreamAsync_ParsesSseChunks),
    ("Sub2Api maps HTTP errors and redacts API key", Sub2Api_MapsHttpErrorsAndRedactsApiKey)
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

static async Task ChatAsync_RoutesAliasesToUpstreamModel()
{
    var provider = new RecordingProvider(
        "alpha",
        new ProviderModel("alpha/default", "alpha", "upstream/gpt", "Alpha Default", new[] { "fast" }),
        "ok");
    var service = new OllamaProxyService(new[] { provider });

    var response = await service.ChatAsync(new OllamaChatRequest(
        "fast",
        new[] { new OllamaChatMessage("user", "hello") },
        false));

    Assert.Equal("fast", response.Model, "Ollama 响应应保留用户请求的模型别名。");
    Assert.NotNull(provider.LastRequest, "Provider 应收到路由后的请求。");
    var lastRequest = provider.LastRequest!;
    Assert.Equal("alpha/default", lastRequest.Model, "Provider 请求应使用内部规范模型名。");
    Assert.Equal("alpha", lastRequest.Provider, "Provider 名称应来自路由模型。");
    Assert.Equal("upstream/gpt", lastRequest.UpstreamModel, "Provider 请求应携带上游模型名。");
}

static async Task ChatStreamAsync_EmitsChunksAndFinalDone()
{
    var provider = new RecordingProvider(
        "alpha",
        new ProviderModel("alpha/default", "alpha", "upstream/gpt", "Alpha Default"),
        "ignored",
        new[]
        {
            new ChatStreamChunk("alpha/default", "hel", Done: false),
            new ChatStreamChunk("alpha/default", "lo", Done: false)
        });
    var service = new OllamaProxyService(new[] { provider });

    var chunks = new List<OllamaChatResponse>();
    await foreach (var chunk in service.ChatStreamAsync(new OllamaChatRequest(
        "alpha/default",
        new[] { new OllamaChatMessage("user", "hello") },
        true)))
    {
        chunks.Add(chunk);
    }

    Assert.Equal(3, chunks.Count, "流式响应应包含两个内容分块和一个结束分块。");
    Assert.Equal("hel", chunks[0].Message.Content, "第一个分块内容不正确。");
    Assert.False(chunks[0].Done, "内容分块不应标记 done。");
    Assert.True(chunks[^1].Done, "最后一个分块应标记 done。");
    Assert.Equal("stop", chunks[^1].DoneReason ?? string.Empty, "结束原因应透传。");
}

static async Task ChatAsync_RejectsUnknownModel()
{
    var provider = new RecordingProvider(
        "alpha",
        new ProviderModel("alpha/default", "alpha", "upstream/gpt", "Alpha Default"),
        "ok");
    var service = new OllamaProxyService(new[] { provider });

    var exception = await Assert.ThrowsAsync<OllamaProxyException>(() => service.ChatAsync(new OllamaChatRequest(
        "missing",
        Array.Empty<OllamaChatMessage>(),
        false)));

    Assert.Equal(OllamaProxyErrorKind.ModelNotFound, exception.Kind, "未知模型应映射为 ModelNotFound。");
    Assert.Equal("model_not_found", exception.Code, "未知模型错误码不正确。");
}

static async Task ChatAsync_RejectsAmbiguousAlias()
{
    var providers = new IModelProvider[]
    {
        new RecordingProvider("alpha", new ProviderModel("alpha/default", "alpha", "a", "Alpha", new[] { "fast" }), "ok"),
        new RecordingProvider("beta", new ProviderModel("beta/default", "beta", "b", "Beta", new[] { "fast" }), "ok")
    };
    var service = new OllamaProxyService(providers);

    var exception = await Assert.ThrowsAsync<OllamaProxyException>(() => service.ChatAsync(new OllamaChatRequest(
        "fast",
        Array.Empty<OllamaChatMessage>(),
        false)));

    Assert.Equal(OllamaProxyErrorKind.AmbiguousModel, exception.Kind, "重复别名应要求用户使用完整模型名。");
    Assert.Equal("ambiguous_model_alias", exception.Code, "重复别名错误码不正确。");
}

static async Task ChatAsync_MapsProviderErrors()
{
    var provider = new RecordingProvider(
        "alpha",
        new ProviderModel("alpha/default", "alpha", "upstream/gpt", "Alpha Default"),
        "ok",
        chatException: new ProviderException(ProviderErrorKind.Unauthorized, "提供商鉴权失败，请检查密钥。"));
    var service = new OllamaProxyService(new[] { provider });

    var exception = await Assert.ThrowsAsync<OllamaProxyException>(() => service.ChatAsync(new OllamaChatRequest(
        "alpha/default",
        Array.Empty<OllamaChatMessage>(),
        false)));

    Assert.Equal(OllamaProxyErrorKind.ProviderUnauthorized, exception.Kind, "Provider 鉴权错误应映射为 ProviderUnauthorized。");
    Assert.Equal("provider_unauthorized", exception.Code, "Provider 鉴权错误码不正确。");
    Assert.Equal("提供商鉴权失败，请检查密钥。", exception.PublicMessage, "错误消息应使用 Provider 提供的脱敏说明。");
}

static async Task Sub2Api_ListModelsAsync_FetchesOpenAiCompatibleModels()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "object": "list",
          "data": [
            { "id": "gpt-4.1-mini", "object": "model" },
            { "id": "claude-sonnet-4.5", "object": "model" }
          ]
        }
        """);
    var provider = CreateSub2ApiProvider(handler);

    var models = await provider.ListModelsAsync();

    Assert.Equal(2, models.Count, "sub2api 应从 /v1/models 映射模型列表。");
    Assert.Equal("sub2api/gpt-4.1-mini", models[0].Name, "模型名应带 Provider 前缀，避免和其他供应商冲突。");
    Assert.Equal("gpt-4.1-mini", models[0].UpstreamModel, "上游模型名应保持原样。");
    Assert.Equal("/gateway/v1/models", handler.Requests[0].PathAndQuery, "BaseUrl 不带 /v1 时应自动补齐 OpenAI 兼容路径。");
    Assert.Equal("Bearer sk-test-secret", handler.Requests[0].Authorization, "sub2api 请求应使用 Bearer API Key 鉴权。");
}

static async Task Sub2Api_ChatAsync_SendsUpstreamModel()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.OK, """
        {
          "choices": [
            {
              "message": { "role": "assistant", "content": "pong" },
              "finish_reason": "stop"
            }
          ]
        }
        """);
    var provider = CreateSub2ApiProvider(handler);

    var response = await provider.ChatAsync(new ChatRequest(
        "sub2api/gpt",
        new[] { new ChatMessage("user", "ping") },
        false,
        "sub2api",
        "gpt-4.1"));

    Assert.Equal("pong", response.Content, "非流式响应应提取 choices[0].message.content。");
    var body = JsonDocument.Parse(handler.Requests[0].Body ?? "{}").RootElement;
    Assert.Equal("/gateway/v1/chat/completions", handler.Requests[0].PathAndQuery, "聊天请求应发送到 /v1/chat/completions。");
    Assert.Equal("gpt-4.1", body.GetProperty("model").GetString() ?? string.Empty, "请求体应使用路由后的上游模型名。");
    Assert.False(body.GetProperty("stream").GetBoolean(), "非流式请求应显式发送 stream=false。");
}

static async Task Sub2Api_ChatStreamAsync_ParsesSseChunks()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueText(HttpStatusCode.OK, """
        data: {"choices":[{"delta":{"content":"hel"}}]}

        data: {"choices":[{"delta":{"content":"lo"}}]}

        data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

        data: [DONE]

        """, "text/event-stream");
    var provider = CreateSub2ApiProvider(handler);

    var chunks = new List<ChatStreamChunk>();
    await foreach (var chunk in provider.ChatStreamAsync(new ChatRequest(
        "sub2api/gpt",
        new[] { new ChatMessage("user", "ping") },
        true,
        "sub2api",
        "gpt-4.1")))
    {
        chunks.Add(chunk);
    }

    Assert.Equal(3, chunks.Count, "流式响应应包含两个内容分块和一个结束分块。");
    Assert.Equal("hel", chunks[0].Content, "第一个 SSE delta 解析不正确。");
    Assert.Equal("lo", chunks[1].Content, "第二个 SSE delta 解析不正确。");
    Assert.True(chunks[^1].Done, "finish_reason 应转换为 done 分块。");
}

static async Task Sub2Api_MapsHttpErrorsAndRedactsApiKey()
{
    var handler = new RecordingHttpMessageHandler();
    handler.EnqueueJson(HttpStatusCode.Unauthorized, """
        {
          "error": {
            "type": "authentication_error",
            "message": "invalid key sk-test-secret"
          }
        }
        """);
    var provider = CreateSub2ApiProvider(handler);

    var exception = await Assert.ThrowsAsync<ProviderException>(() => provider.ListModelsAsync());

    Assert.Equal(ProviderErrorKind.Unauthorized, exception.Kind, "401/403 应映射为 Provider 鉴权错误。");
    Assert.DoesNotContain("sk-test-secret", exception.PublicMessage, "公开错误消息不得包含 API Key 原文。");
    Assert.Contains("[已脱敏密钥]", exception.PublicMessage, "错误消息应保留可理解的脱敏提示。");
}

static Sub2ApiModelProvider CreateSub2ApiProvider(RecordingHttpMessageHandler handler)
{
    return new Sub2ApiModelProvider(
        new HttpClient(handler),
        new Sub2ApiProviderOptions
        {
            BaseUrl = "https://sub2api.example/gateway",
            ApiKey = "sk-test-secret",
            Timeout = TimeSpan.FromSeconds(5)
        });
}

internal sealed class RecordingProvider : IModelProvider
{
    private readonly IReadOnlyList<ProviderModel> _models;
    private readonly string _content;
    private readonly IReadOnlyList<ChatStreamChunk>? _streamChunks;
    private readonly Exception? _chatException;

    public RecordingProvider(
        string name,
        ProviderModel model,
        string content,
        IReadOnlyList<ChatStreamChunk>? streamChunks = null,
        Exception? chatException = null)
    {
        Name = name;
        _models = new[] { model };
        _content = content;
        _streamChunks = streamChunks;
        _chatException = chatException;
    }

    public string Name { get; }

    public ChatRequest? LastRequest { get; private set; }

    public Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_models);

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        if (_chatException is not null)
        {
            throw _chatException;
        }

        return Task.FromResult(new ChatResponse(request.Model, _content));
    }

    public async IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        foreach (var chunk in _streamChunks ?? new[] { new ChatStreamChunk(request.Model, _content, Done: false), new ChatStreamChunk(request.Model, string.Empty, Done: true, "stop") })
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return chunk;
        }
    }
}

internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<RecordedHttpRequest, HttpResponseMessage>> _responses = new();

    public List<RecordedHttpRequest> Requests { get; } = new();

    public void EnqueueJson(HttpStatusCode statusCode, string json)
        => EnqueueText(statusCode, json, "application/json");

    public void EnqueueText(HttpStatusCode statusCode, string text, string mediaType)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(text, Encoding.UTF8, mediaType)
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No fake HTTP response was queued.");
        }

        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedHttpRequest(
            request.Method.Method,
            request.RequestUri?.PathAndQuery ?? string.Empty,
            request.Headers.Authorization?.ToString(),
            body);
        Requests.Add(recorded);
        return _responses.Dequeue()(recorded);
    }
}

internal sealed record RecordedHttpRequest(
    string Method,
    string PathAndQuery,
    string? Authorization,
    string? Body);

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void False(bool condition, string message)
        => True(!condition, message);

    public static void NotNull(object? value, string message)
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}; Actual: {actual}");
        }
    }

    public static void Contains(string expectedSubstring, string actual, string message)
    {
        if (!actual.Contains(expectedSubstring, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{message} Expected substring: {expectedSubstring}; Actual: {actual}");
        }
    }

    public static void DoesNotContain(string unexpectedSubstring, string actual, string message)
    {
        if (actual.Contains(unexpectedSubstring, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{message} Unexpected substring: {unexpectedSubstring}; Actual: {actual}");
        }
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
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

        throw new InvalidOperationException($"Expected exception {typeof(TException).Name} was not thrown.");
    }
}
