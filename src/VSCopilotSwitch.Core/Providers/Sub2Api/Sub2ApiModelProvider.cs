using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VSCopilotSwitch.Core.Providers.Sub2Api;

public sealed class Sub2ApiModelProvider : IModelProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly Sub2ApiProviderOptions _options;
    private readonly Uri _baseUri;
    private readonly string _apiKey;
    private readonly string _providerName;

    public Sub2ApiModelProvider(HttpClient httpClient, Sub2ApiProviderOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _providerName = NormalizeProviderName(options.ProviderName);
        _baseUri = NormalizeBaseUrl(options.BaseUrl);
        _apiKey = NormalizeApiKey(options.ApiKey);

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
        var payload = await JsonSerializer.DeserializeAsync<OpenAiModelListResponse>(contentStream, JsonOptions, cancellationToken);
        var models = payload?.Data?
            .Select(model => model.Id?.Trim())
            .Where(modelId => !string.IsNullOrWhiteSpace(modelId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(modelId => ToProviderModel(new Sub2ApiModelOptions(modelId!)))
            .ToArray() ?? Array.Empty<ProviderModel>();

        if (models.Length == 0)
        {
            throw new ProviderException(ProviderErrorKind.Unavailable, "sub2api 未返回可用模型列表，请检查 API Key 权限或平台模型配置。");
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
        var payload = await JsonSerializer.DeserializeAsync<OpenAiChatCompletionResponse>(contentStream, JsonOptions, cancellationToken);
        ThrowIfErrorPayload(payload?.Error);

        var choice = payload?.Choices?.FirstOrDefault();
        if (choice is null)
        {
            throw new ProviderException(ProviderErrorKind.UpstreamError, "sub2api 返回了空的聊天响应。");
        }

        return new ChatResponse(
            request.Model,
            choice.Message?.Content ?? string.Empty,
            NormalizeDoneReason(choice.FinishReason));
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
                payload = JsonSerializer.Deserialize<OpenAiChatCompletionResponse>(data, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new ProviderException(ProviderErrorKind.UpstreamError, "sub2api 返回了无法解析的流式响应。", ex);
            }

            ThrowIfErrorPayload(payload?.Error);
            var choice = payload?.Choices?.FirstOrDefault();
            if (choice is null)
            {
                continue;
            }

            var content = choice.Delta?.Content ?? choice.Message?.Content;
            if (!string.IsNullOrEmpty(content))
            {
                yield return new ChatStreamChunk(request.Model, content, Done: false);
            }

            if (!string.IsNullOrWhiteSpace(choice.FinishReason))
            {
                completed = true;
                yield return new ChatStreamChunk(request.Model, string.Empty, Done: true, NormalizeDoneReason(choice.FinishReason));
                yield break;
            }
        }

        if (!completed)
        {
            yield return new ChatStreamChunk(request.Model, string.Empty, Done: true, "stop");
        }
    }

    private ProviderModel ToProviderModel(Sub2ApiModelOptions model)
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
            throw new ProviderException(ProviderErrorKind.InvalidRequest, "sub2api 请求缺少上游模型名称。");
        }

        var messages = request.Messages
            .Select(message => new OpenAiChatMessage(message.Role, message.Content))
            .ToArray();

        // sub2api 的网关入口兼容 OpenAI Chat Completions，请求体只发送通用字段，站点私有字段后续放在 Adapter 内扩展。
        return new OpenAiChatCompletionRequest(request.UpstreamModel, messages, stream);
    }

    private HttpRequestMessage CreateJsonRequest(OpenAiChatCompletionRequest upstreamRequest)
    {
        var request = CreateRequest(HttpMethod.Post, "chat/completions");
        var json = JsonSerializer.Serialize(upstreamRequest, JsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string openAiPath)
    {
        var request = new HttpRequestMessage(method, BuildOpenAiCompatibleUri(openAiPath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("VSCopilotSwitch/0.1");
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
            throw new ProviderException(ProviderErrorKind.Timeout, "sub2api 请求超时，请稍后重试。", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException(ProviderErrorKind.Unavailable, "sub2api 网络请求失败，请检查中转站地址和网络连接。", ex);
        }
    }

    private Uri BuildOpenAiCompatibleUri(string openAiPath)
    {
        var basePath = _baseUri.AbsolutePath.TrimEnd('/');
        var normalizedPath = openAiPath.TrimStart('/');
        var relativePath = basePath.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? normalizedPath
            : $"v1/{normalizedPath}";

        var builder = new UriBuilder(_baseUri)
        {
            Path = CombinePath(basePath, relativePath),
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
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
                HttpStatusCode.BadRequest => $"sub2api 拒绝了当前请求：{sanitized}",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"sub2api 鉴权失败：{sanitized}",
                HttpStatusCode.TooManyRequests => $"sub2api 额度或频率受限：{sanitized}",
                HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => $"sub2api 请求超时：{sanitized}",
                _ => $"sub2api 上游请求失败：{sanitized}"
            };
        }

        return statusCode switch
        {
            HttpStatusCode.BadRequest => "sub2api 拒绝了当前请求，请检查模型名称和消息格式。",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "sub2api 鉴权失败，请检查 API Key 或平台权限。",
            HttpStatusCode.TooManyRequests => "sub2api 额度或频率受限，请稍后重试。",
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => "sub2api 请求超时，请稍后重试。",
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway => "sub2api 服务暂不可用，请稍后重试。",
            _ => "sub2api 上游请求失败，请稍后重试。"
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
            var payload = JsonSerializer.Deserialize<OpenAiErrorEnvelope>(rawContent, JsonOptions);
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
                ? "sub2api 返回了上游错误。"
                : $"sub2api 返回了上游错误：{message}");
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
        => string.IsNullOrWhiteSpace(providerName) ? "sub2api" : providerName.Trim();

    private static Uri NormalizeBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("sub2api BaseUrl 必须是 http 或 https 绝对地址。", nameof(baseUrl));
        }

        return uri;
    }

    private static string NormalizeApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("sub2api ApiKey 不能为空。", nameof(apiKey));
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
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record OpenAiChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record OpenAiChatCompletionResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<OpenAiChatChoice>? Choices,
        [property: JsonPropertyName("error")] OpenAiError? Error);

    private sealed record OpenAiChatChoice(
        [property: JsonPropertyName("message")] OpenAiChatMessage? Message,
        [property: JsonPropertyName("delta")] OpenAiChatMessage? Delta,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record OpenAiErrorEnvelope(
        [property: JsonPropertyName("error")] OpenAiError? Error);

    private sealed record OpenAiError(
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("code")] JsonElement? Code);
}
