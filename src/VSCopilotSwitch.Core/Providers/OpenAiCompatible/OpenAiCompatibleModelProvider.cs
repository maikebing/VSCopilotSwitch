using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VSCopilotSwitch.Core.Providers.OpenAiCompatible;

public sealed partial class OpenAiCompatibleModelProvider : IModelProvider
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiCompatibleProviderOptions _options;
    private readonly Uri _baseUri;
    private readonly string _apiKey;
    private readonly string _providerName;
    private readonly string _publicProviderName;

    public OpenAiCompatibleModelProvider(HttpClient httpClient, OpenAiCompatibleProviderOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _providerName = NormalizeProviderName(options.ProviderName);
        _publicProviderName = NormalizePublicProviderName(options.PublicProviderName, _providerName);
        _baseUri = NormalizeBaseUrl(options.BaseUrl);
        _apiKey = NormalizeApiKey(options.ApiKey, _publicProviderName);

        if (options.Timeout > TimeSpan.Zero)
        {
            _httpClient.Timeout = options.Timeout;
        }
    }

    public string Name => _providerName;

    public async Task<IReadOnlyList<ProviderModel>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        if (_options.Models.Count > 0)
        {
            return _options.Models
                .Select(ToProviderModel)
                .Where(model => !string.IsNullOrWhiteSpace(model.UpstreamModel))
                .ToArray();
        }

        using var request = CreateRequest(HttpMethod.Get, "models");
        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync(response, cancellationToken);
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync(
            contentStream,
            OpenAiProviderJsonContext.Default.OpenAiModelListResponse,
            cancellationToken);
        var models = payload?.Data?
            .Select(model => model.Id?.Trim())
            .Where(modelId => !string.IsNullOrWhiteSpace(modelId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(modelId => ToProviderModel(new OpenAiCompatibleModelOptions(modelId!)))
            .ToArray() ?? Array.Empty<ProviderModel>();

        if (models.Length == 0)
        {
            throw new ProviderException(ProviderErrorKind.Unavailable, $"{_publicProviderName} 未返回可用模型列表，请检查 API Key 权限或平台模型配置。");
        }

        return models;
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var upstreamRequest = CreateChatRequest(request, stream: false);
        using var httpRequest = CreateJsonRequest(upstreamRequest);
        using var response = await SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync(response, cancellationToken);
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync(
            contentStream,
            OpenAiProviderJsonContext.Default.OpenAiChatCompletionResponse,
            cancellationToken);
        ThrowIfErrorPayload(payload?.Error);

        var choice = payload?.Choices?.FirstOrDefault();
        if (choice is null)
        {
            throw new ProviderException(ProviderErrorKind.UpstreamError, $"{_publicProviderName} 返回了空的聊天响应。");
        }

        return new ChatResponse(
            request.Model,
            choice.Message?.Content ?? string.Empty,
            NormalizeDoneReason(choice.FinishReason),
            ToChatToolCalls(choice.Message?.ToolCalls),
            ToChatUsage(payload?.Usage),
            ToReasoningContent(choice.Message));
    }

    public async IAsyncEnumerable<ChatStreamChunk> ChatStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var upstreamRequest = CreateChatRequest(request, stream: true);
        using var httpRequest = CreateJsonRequest(upstreamRequest);
        using var response = await SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync(response, cancellationToken);
        }

        var completed = false;
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(contentStream, Encoding.UTF8);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            var data = NormalizeSseDataLine(line);
            if (string.IsNullOrWhiteSpace(data))
            {
                continue;
            }

            if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                completed = true;
                yield return new ChatStreamChunk(request.Model, string.Empty, Done: true, "stop");
                yield break;
            }

            OpenAiChatCompletionResponse? payload;
            try
            {
                payload = JsonSerializer.Deserialize(
                    data,
                    OpenAiProviderJsonContext.Default.OpenAiChatCompletionResponse);
            }
            catch (JsonException ex)
            {
                throw new ProviderException(ProviderErrorKind.UpstreamError, $"{_publicProviderName} 返回了无法解析的流式响应。", ex);
            }

            ThrowIfErrorPayload(payload?.Error);
            var choice = payload?.Choices?.FirstOrDefault();
            if (choice is null)
            {
                continue;
            }

            var content = choice.Delta?.Content ?? choice.Message?.Content;
            var reasoningContent = ToReasoningContent(choice.Delta) ?? ToReasoningContent(choice.Message);
            var toolCalls = ToChatToolCalls(choice.Delta?.ToolCalls ?? choice.Message?.ToolCalls);
            if (!string.IsNullOrEmpty(content)
                || !string.IsNullOrEmpty(reasoningContent)
                || toolCalls is not null
                || !string.IsNullOrWhiteSpace(choice.Delta?.Role))
            {
                yield return new ChatStreamChunk(
                    request.Model,
                    content ?? string.Empty,
                    Done: false,
                    Delta: new ChatDelta(choice.Delta?.Role, content, toolCalls, reasoningContent));
            }

            if (!string.IsNullOrWhiteSpace(choice.FinishReason))
            {
                completed = true;
                yield return new ChatStreamChunk(
                    request.Model,
                    string.Empty,
                    Done: true,
                    NormalizeDoneReason(choice.FinishReason),
                    Usage: ToChatUsage(payload?.Usage));
                yield break;
            }
        }

        if (!completed)
        {
            yield return new ChatStreamChunk(request.Model, string.Empty, Done: true, "stop");
        }
    }

    private ProviderModel ToProviderModel(OpenAiCompatibleModelOptions model)
    {
        var upstreamModel = model.UpstreamModel.Trim();
        var modelName = string.IsNullOrWhiteSpace(model.Name)
            ? $"{_providerName}/{upstreamModel}"
            : model.Name.Trim();
        var displayName = string.IsNullOrWhiteSpace(model.DisplayName)
            ? upstreamModel
            : model.DisplayName.Trim();

        return new ProviderModel(
            modelName,
            _providerName,
            upstreamModel,
            displayName,
            NormalizeAliases(model.Aliases));
    }

    private OpenAiChatCompletionRequest CreateChatRequest(ChatRequest request, bool stream)
    {
        if (string.IsNullOrWhiteSpace(request.UpstreamModel))
        {
            throw new ProviderException(ProviderErrorKind.InvalidRequest, $"{_publicProviderName} 请求缺少上游模型名称。");
        }

        var messages = request.Messages
            .Select(message => new OpenAiChatMessage(
                message.Role,
                ToOpenAiMessageContent(message),
                ToOpenAiToolCalls(message.ToolCalls),
                message.ToolCallId,
                message.Name,
                ToOpenAiReasoningContent(message)))
            .ToArray();

        // OpenAI-compatible Provider 只发送通用 Chat Completions 字段，供应商私有字段应由各 Adapter wrapper 显式扩展。
        return new OpenAiChatCompletionRequest(
            request.UpstreamModel,
            messages,
            stream,
            ToOpenAiTools(request.Tools),
            ToOpenAiToolChoice(request.ToolChoice),
            ToOpenAiReasoningEffort(request),
            ToOpenAiCompatibleThinking(request));
    }

    private static string? ToOpenAiMessageContent(ChatMessage message)
        => string.IsNullOrEmpty(message.Content) && message.ToolCalls is { Count: > 0 }
            ? null
            : message.Content;

    private static string? ToOpenAiReasoningContent(ChatMessage message)
        => string.IsNullOrWhiteSpace(message.ReasoningContent) ? null : message.ReasoningContent;

    private static string? ToOpenAiReasoningEffort(ChatRequest request)
        => !string.IsNullOrWhiteSpace(request.ReasoningEffort)
            ? request.ReasoningEffort
            : request.Think is { ValueKind: JsonValueKind.String } think ? think.GetString() : null;

    private static string? ToReasoningContent(OpenAiChatMessage? message)
        => string.IsNullOrWhiteSpace(message?.ReasoningContent)
            ? string.IsNullOrWhiteSpace(message?.Thinking) ? null : message.Thinking
            : message.ReasoningContent;

    private static JsonElement? ToOpenAiCompatibleThinking(ChatRequest request)
    {
        if (request.Thinking is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } thinking)
        {
            return thinking;
        }

        if (request.Think is { ValueKind: not JsonValueKind.False and not JsonValueKind.Null and not JsonValueKind.Undefined } think)
        {
            return think;
        }

        return null;
    }

    private static IReadOnlyList<OpenAiTool>? ToOpenAiTools(IReadOnlyList<ChatTool>? tools)
    {
        var mapped = tools?
            .Where(tool => tool.Function is not null && !string.IsNullOrWhiteSpace(tool.Function.Name))
            .Select(tool => new OpenAiTool(
                string.IsNullOrWhiteSpace(tool.Type) ? "function" : tool.Type,
                new OpenAiToolFunction(
                    tool.Function.Name,
                    tool.Function.Description,
                    tool.Function.Parameters?.Clone())))
            .ToArray();

        return mapped is { Length: > 0 } ? mapped : null;
    }

    private static JsonElement? ToOpenAiToolChoice(ChatToolChoice? toolChoice)
    {
        if (toolChoice is null || string.IsNullOrWhiteSpace(toolChoice.Type))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(toolChoice.FunctionName))
        {
            return JsonSerializer.SerializeToElement(
                toolChoice.Type.Trim(),
                OpenAiProviderJsonContext.Default.String);
        }

        return JsonSerializer.SerializeToElement(
            new OpenAiToolChoiceObject("function", new OpenAiToolChoiceFunction(toolChoice.FunctionName.Trim())),
            OpenAiProviderJsonContext.Default.OpenAiToolChoiceObject);
    }

    private static IReadOnlyList<OpenAiToolCall>? ToOpenAiToolCalls(IReadOnlyList<ChatToolCall>? toolCalls)
    {
        var mapped = toolCalls?
            .Select(toolCall => new OpenAiToolCall(
                toolCall.Id,
                string.IsNullOrWhiteSpace(toolCall.Type) ? "function" : toolCall.Type,
                new OpenAiFunctionCall(toolCall.Function.Name, toolCall.Function.Arguments),
                toolCall.Index))
            .ToArray();

        return mapped is { Length: > 0 } ? mapped : null;
    }

    private static IReadOnlyList<ChatToolCall>? ToChatToolCalls(IReadOnlyList<OpenAiToolCall>? toolCalls)
    {
        var mapped = toolCalls?
            .Select(toolCall => new ChatToolCall(
                toolCall.Id ?? string.Empty,
                string.IsNullOrWhiteSpace(toolCall.Type) ? "function" : toolCall.Type,
                new ChatFunctionCall(
                    toolCall.Function?.Name ?? string.Empty,
                    toolCall.Function?.Arguments ?? string.Empty),
                toolCall.Index))
            .ToArray();

        return mapped is { Length: > 0 } ? mapped : null;
    }

    private static ChatUsage? ToChatUsage(OpenAiUsage? usage)
        => usage is null
            ? null
            : new ChatUsage(usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens);

    private HttpRequestMessage CreateJsonRequest(OpenAiChatCompletionRequest upstreamRequest)
    {
        var request = CreateRequest(HttpMethod.Post, "chat/completions");
        var json = JsonSerializer.Serialize(
            upstreamRequest,
            OpenAiProviderJsonContext.Default.OpenAiChatCompletionRequest);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string openAiPath)
    {
        var request = new HttpRequestMessage(method, BuildOpenAiCompatibleUri(openAiPath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("VSCopilotSwitch/0.1");

        foreach (var (name, value) in _options.AdditionalHeaders)
        {
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
            {
                request.Headers.TryAddWithoutValidation(name.Trim(), value.Trim());
            }
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, completionOption, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderException(ProviderErrorKind.Timeout, $"{_publicProviderName} 请求超时，请稍后重试。", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException(ProviderErrorKind.Unavailable, $"{_publicProviderName} 网络请求失败，请检查 API 地址和网络连接。", ex);
        }
    }

    private Uri BuildOpenAiCompatibleUri(string openAiPath)
    {
        var basePath = _baseUri.AbsolutePath.TrimEnd('/');
        var normalizedPath = openAiPath.TrimStart('/');
        var apiPathPrefix = NormalizeApiPathPrefix(_options.ApiPathPrefix);
        var relativePath = string.IsNullOrEmpty(apiPathPrefix) || HasApiPathPrefix(basePath, apiPathPrefix)
            ? normalizedPath
            : $"{apiPathPrefix}/{normalizedPath}";

        var builder = new UriBuilder(_baseUri)
        {
            Path = CombinePath(basePath, relativePath),
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    private static string NormalizeApiPathPrefix(string? apiPathPrefix)
        => string.IsNullOrWhiteSpace(apiPathPrefix)
            ? string.Empty
            : apiPathPrefix.Trim().Trim('/');

    private static bool HasApiPathPrefix(string basePath, string apiPathPrefix)
    {
        if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(apiPathPrefix))
        {
            return false;
        }

        return basePath.EndsWith("/" + apiPathPrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(basePath.Trim('/'), apiPathPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ProviderException> CreateProviderExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var upstreamMessage = ExtractErrorMessage(rawContent);
        var message = BuildPublicErrorMessage(response.StatusCode, upstreamMessage);
        return new ProviderException(MapErrorKind(response.StatusCode), message);
    }

    private string BuildPublicErrorMessage(HttpStatusCode statusCode, string? upstreamMessage)
    {
        var sanitized = SanitizePublicMessage(upstreamMessage);
        if (!string.IsNullOrWhiteSpace(sanitized))
        {
            return statusCode switch
            {
                HttpStatusCode.BadRequest => $"{_publicProviderName} 拒绝了当前请求：{sanitized}",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"{_publicProviderName} 鉴权失败：{sanitized}",
                HttpStatusCode.TooManyRequests => $"{_publicProviderName} 额度或频率受限：{sanitized}",
                HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => $"{_publicProviderName} 请求超时：{sanitized}",
                _ => $"{_publicProviderName} 上游请求失败：{sanitized}"
            };
        }

        return statusCode switch
        {
            HttpStatusCode.BadRequest => $"{_publicProviderName} 拒绝了当前请求，请检查模型名称和消息格式。",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"{_publicProviderName} 鉴权失败，请检查 API Key 或平台权限。",
            HttpStatusCode.TooManyRequests => $"{_publicProviderName} 额度或频率受限，请稍后重试。",
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => $"{_publicProviderName} 请求超时，请稍后重试。",
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway => $"{_publicProviderName} 服务暂不可用，请稍后重试。",
            _ => $"{_publicProviderName} 上游请求失败，请稍后重试。"
        };
    }

    private static ProviderErrorKind MapErrorKind(HttpStatusCode statusCode)
        => statusCode switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.NotFound => ProviderErrorKind.InvalidRequest,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ProviderErrorKind.Unauthorized,
            HttpStatusCode.TooManyRequests => ProviderErrorKind.RateLimited,
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => ProviderErrorKind.Timeout,
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable => ProviderErrorKind.Unavailable,
            _ => ProviderErrorKind.UpstreamError
        };

    private static string? ExtractErrorMessage(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize(
                rawContent,
                OpenAiProviderJsonContext.Default.OpenAiErrorEnvelope);
            if (!string.IsNullOrWhiteSpace(payload?.Error?.Message))
            {
                return payload.Error.Message;
            }
        }
        catch (JsonException)
        {
            return rawContent;
        }

        return rawContent;
    }

    private void ThrowIfErrorPayload(OpenAiError? error)
    {
        if (error is null)
        {
            return;
        }

        var message = SanitizePublicMessage(error.Message);
        throw new ProviderException(
            ProviderErrorKind.UpstreamError,
            string.IsNullOrWhiteSpace(message)
                ? $"{_publicProviderName} 返回了上游错误。"
                : $"{_publicProviderName} 返回了上游错误：{message}");
    }

    private string? SanitizePublicMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var sanitized = message.Trim();
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            sanitized = sanitized.Replace(_apiKey, "[已脱敏密钥]", StringComparison.Ordinal);
        }

        return sanitized.Length <= 300 ? sanitized : sanitized[..300] + "...";
    }

    private static string NormalizeDoneReason(string? finishReason)
        => string.IsNullOrWhiteSpace(finishReason) ? "stop" : finishReason.Trim();

    private static IReadOnlyList<string>? NormalizeAliases(IReadOnlyList<string>? aliases)
    {
        var normalized = aliases?
            .Select(alias => alias.Trim())
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized is { Length: > 0 } ? normalized : null;
    }

    private static string NormalizeSseDataLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith(':'))
        {
            return string.Empty;
        }

        if (trimmed.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? trimmed["data:".Length..].Trim()
            : trimmed;
    }

    private static string CombinePath(string basePath, string relativePath)
    {
        var normalizedBase = basePath.TrimEnd('/');
        if (string.IsNullOrEmpty(normalizedBase))
        {
            return "/" + relativePath.TrimStart('/');
        }

        return normalizedBase + "/" + relativePath.TrimStart('/');
    }

    private static string NormalizeProviderName(string providerName)
        => string.IsNullOrWhiteSpace(providerName) ? "openai-compatible" : providerName.Trim();

    private static string NormalizePublicProviderName(string publicProviderName, string providerName)
        => string.IsNullOrWhiteSpace(publicProviderName) ? providerName : publicProviderName.Trim();

    private static Uri NormalizeBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("OpenAI-compatible BaseUrl 必须是 http 或 https 绝对地址。", nameof(baseUrl));
        }

        return uri;
    }

    private static string NormalizeApiKey(string apiKey, string publicProviderName)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException($"{publicProviderName} ApiKey 不能为空。", nameof(apiKey));
        }

        return apiKey.Trim();
    }

    private sealed record OpenAiModelListResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<OpenAiModelInfo>? Data);

    private sealed record OpenAiModelInfo(
        [property: JsonPropertyName("id")] string? Id);

    private sealed record OpenAiChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<OpenAiChatMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("tools")] IReadOnlyList<OpenAiTool>? Tools = null,
        [property: JsonPropertyName("tool_choice")] JsonElement? ToolChoice = null,
        [property: JsonPropertyName("reasoning_effort")] string? ReasoningEffort = null,
        [property: JsonPropertyName("thinking")] JsonElement? Thinking = null);

    private sealed record OpenAiChatMessage(
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<OpenAiToolCall>? ToolCalls = null,
        [property: JsonPropertyName("tool_call_id")] string? ToolCallId = null,
        [property: JsonPropertyName("name")] string? Name = null,
        [property: JsonPropertyName("reasoning_content")] string? ReasoningContent = null,
        [property: JsonPropertyName("thinking")] string? Thinking = null);

    private sealed record OpenAiChatCompletionResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<OpenAiChatChoice>? Choices,
        [property: JsonPropertyName("error")] OpenAiError? Error,
        [property: JsonPropertyName("usage")] OpenAiUsage? Usage = null);

    private sealed record OpenAiChatChoice(
        [property: JsonPropertyName("message")] OpenAiChatMessage? Message,
        [property: JsonPropertyName("delta")] OpenAiChatMessage? Delta,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record OpenAiTool(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] OpenAiToolFunction Function);

    private sealed record OpenAiToolFunction(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("parameters")] JsonElement? Parameters);

    private sealed record OpenAiToolChoiceObject(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] OpenAiToolChoiceFunction Function);

    private sealed record OpenAiToolChoiceFunction(
        [property: JsonPropertyName("name")] string Name);

    private sealed record OpenAiToolCall(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("function")] OpenAiFunctionCall? Function,
        [property: JsonPropertyName("index")] int? Index = null);

    private sealed record OpenAiFunctionCall(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("arguments")] string? Arguments);

    private sealed record OpenAiUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
        [property: JsonPropertyName("total_tokens")] int? TotalTokens);

    private sealed record OpenAiErrorEnvelope(
        [property: JsonPropertyName("error")] OpenAiError? Error);

    private sealed record OpenAiError(
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("code")] JsonElement? Code);

    [JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(OpenAiModelListResponse))]
    [JsonSerializable(typeof(OpenAiChatCompletionRequest))]
    [JsonSerializable(typeof(OpenAiChatCompletionResponse))]
    [JsonSerializable(typeof(OpenAiToolChoiceObject))]
    [JsonSerializable(typeof(OpenAiErrorEnvelope))]
    [JsonSerializable(typeof(string))]
    private sealed partial class OpenAiProviderJsonContext : JsonSerializerContext;
}
