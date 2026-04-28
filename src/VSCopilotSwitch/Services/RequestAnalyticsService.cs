using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VSCopilotSwitch.Services;

public interface IRequestAnalyticsService
{
    Task InvokeAsync(HttpContext context, Func<Task> next);

    RequestAnalyticsSnapshot GetSnapshot(string listeningUrl);

    void Clear();
}

public sealed class RequestAnalyticsService : IRequestAnalyticsService
{
    private const int MaxEntries = 500;
    private const int MaxBodyCaptureBytes = 16 * 1024;
    private readonly ConcurrentQueue<RequestLogEntry> _entries = new();
    private static readonly Regex ApiKeyPattern = new(@"sk-[A-Za-z0-9_\-]{8,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BearerPattern = new(@"Bearer\s+[A-Za-z0-9._~+\-/]+=*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (ShouldSkip(path))
        {
            await next();
            return;
        }

        var requestBody = await CaptureRequestBodyAsync(context.Request);
        var model = TryReadModel(path, requestBody.RawText);
        var requestHeaders = CaptureHeaders(context.Request.Headers);
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var originalResponseBody = context.Response.Body;
        await using var responseCapture = new CapturingResponseBodyStream(originalResponseBody, MaxBodyCaptureBytes);
        context.Response.Body = responseCapture;

        try
        {
            await next();
        }
        finally
        {
            context.Response.Body = originalResponseBody;
            stopwatch.Stop();
            Enqueue(new RequestLogEntry(
                startedAt,
                context.Request.Method,
                path,
                model,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                EstimateInputTokens(context),
                0,
                0m,
                SanitizeUserAgent(context.Request.Headers.UserAgent.ToString()),
                requestHeaders,
                requestBody.SanitizedText,
                CaptureHeaders(context.Response.Headers),
                BuildBodyText(responseCapture.CapturedBytes, context.Response.ContentType, responseCapture.Truncated)));
        }
    }

    public RequestAnalyticsSnapshot GetSnapshot(string listeningUrl)
    {
        var entries = _entries.ToArray().OrderByDescending(entry => entry.Timestamp).ToArray();
        var totalRequests = entries.Length;
        var totalInputTokens = entries.Sum(entry => entry.InputTokens);
        var totalOutputTokens = entries.Sum(entry => entry.OutputTokens);
        var totalCost = entries.Sum(entry => entry.Cost);
        var averageLatency = totalRequests == 0 ? 0 : entries.Average(entry => entry.DurationMilliseconds) / 1000d;

        return new RequestAnalyticsSnapshot(
            new RequestAnalyticsSummary(
                totalRequests,
                totalInputTokens + totalOutputTokens,
                totalInputTokens,
                totalOutputTokens,
                totalCost,
                averageLatency),
            new ListenerStatus(
                listeningUrl,
                TryGetPort(listeningUrl),
                "运行中"),
            entries.Take(200).ToArray());
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }

    private void Enqueue(RequestLogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _))
        {
        }
    }

    private static bool ShouldSkip(string path)
        => path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/internal/analytics", StringComparison.OrdinalIgnoreCase);

    private static async Task<CapturedBody> CaptureRequestBodyAsync(HttpRequest request)
    {
        if (request.Body is null || request.ContentLength is null or 0)
        {
            return new CapturedBody(null, null);
        }

        if (!IsTextLikeContentType(request.ContentType))
        {
            return new CapturedBody(null, "(非文本请求体已省略)");
        }

        try
        {
            request.EnableBuffering();
            var bytes = await ReadAtMostAsync(request.Body, MaxBodyCaptureBytes + 1);
            if (request.Body.CanSeek)
            {
                request.Body.Position = 0;
            }

            var truncated = bytes.Length > MaxBodyCaptureBytes;
            var capturedBytes = truncated ? bytes[..MaxBodyCaptureBytes] : bytes;
            var rawText = Encoding.UTF8.GetString(capturedBytes);
            var sanitized = SanitizeBody(rawText, request.ContentType, truncated);
            return new CapturedBody(rawText, sanitized);
        }
        catch
        {
            if (request.Body.CanSeek)
            {
                request.Body.Position = 0;
            }

            return new CapturedBody(null, "(请求体读取失败，已跳过)");
        }
    }

    private static async Task<byte[]> ReadAtMostAsync(Stream stream, int maxBytes)
    {
        var buffer = new byte[8192];
        using var output = new MemoryStream();
        while (output.Length < maxBytes)
        {
            var remaining = maxBytes - (int)output.Length;
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)));
            if (read == 0)
            {
                break;
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static string? TryReadModel(string path, string? body)
    {
        if (string.IsNullOrWhiteSpace(body)
            || (!string.Equals(path, "/api/chat", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(path, "/api/show", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("model", out var modelElement)
                ? modelElement.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static int EstimateInputTokens(HttpContext context)
    {
        var length = context.Request.ContentLength ?? 0;
        if (length <= 0)
        {
            return 0;
        }

        return (int)Math.Max(1, Math.Ceiling(length / 4d));
    }

    private static string SanitizeUserAgent(string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "-";
        }

        return userAgent.Length <= 180 ? userAgent : userAgent[..180] + "...";
    }

    private static IReadOnlyDictionary<string, string> CaptureHeaders(IHeaderDictionary headers)
        => headers.ToDictionary(
            header => header.Key,
            header => SanitizeHeaderValue(header.Key, string.Join(", ", header.Value.ToArray())),
            StringComparer.OrdinalIgnoreCase);

    private static string SanitizeHeaderValue(string name, string value)
        => IsSensitiveName(name) ? "[已脱敏]" : RedactSecrets(value);

    private static string? BuildBodyText(byte[] bytes, string? contentType, bool truncated)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        if (!IsTextLikeContentType(contentType))
        {
            return "(非文本响应体已省略)";
        }

        var text = Encoding.UTF8.GetString(bytes);
        return SanitizeBody(text, contentType, truncated);
    }

    private static string SanitizeBody(string text, string? contentType, bool truncated)
    {
        var sanitized = IsJsonContentType(contentType) ? SanitizeJsonBody(text) : RedactSecrets(text);
        return truncated ? $"{sanitized}\n...(已截断，仅显示前 {MaxBodyCaptureBytes} 字节)" : sanitized;
    }

    private static string SanitizeJsonBody(string text)
    {
        try
        {
            var node = JsonNode.Parse(text);
            RedactJsonNode(node);
            return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? string.Empty;
        }
        catch
        {
            return RedactSecrets(text);
        }
    }

    private static void RedactJsonNode(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (IsSensitiveName(property.Key))
                {
                    jsonObject[property.Key] = "[已脱敏]";
                    continue;
                }

                RedactJsonNode(property.Value);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var child in jsonArray)
            {
                RedactJsonNode(child);
            }
        }
        else if (node is JsonValue value && value.TryGetValue<string>(out var stringValue))
        {
            var redacted = RedactSecrets(stringValue);
            if (!string.Equals(redacted, stringValue, StringComparison.Ordinal))
            {
                value.ReplaceWith(redacted);
            }
        }
    }

    private static bool IsSensitiveName(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return normalized.Contains("authorization", StringComparison.Ordinal)
            || normalized.Contains("api-key", StringComparison.Ordinal)
            || normalized.Contains("apikey", StringComparison.Ordinal)
            || normalized.Contains("api_key", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("cookie", StringComparison.Ordinal);
    }

    private static string RedactSecrets(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var withoutApiKeys = ApiKeyPattern.Replace(value, "[已脱敏密钥]");
        return BearerPattern.Replace(withoutApiKeys, "Bearer [已脱敏]");
    }

    private static bool IsTextLikeContentType(string? contentType)
        => string.IsNullOrWhiteSpace(contentType)
            || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("x-ndjson", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("event-stream", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("form-urlencoded", StringComparison.OrdinalIgnoreCase);

    private static bool IsJsonContentType(string? contentType)
        => !string.IsNullOrWhiteSpace(contentType)
            && contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            && !contentType.Contains("x-ndjson", StringComparison.OrdinalIgnoreCase);

    private static int TryGetPort(string listeningUrl)
        => Uri.TryCreate(listeningUrl, UriKind.Absolute, out var uri) ? uri.Port : 0;

    private sealed record CapturedBody(string? RawText, string? SanitizedText);

    private sealed class CapturingResponseBodyStream : Stream
    {
        private readonly Stream _inner;
        private readonly MemoryStream _capture = new();
        private readonly int _limit;

        public CapturingResponseBodyStream(Stream inner, int limit)
        {
            _inner = inner;
            _limit = limit;
        }

        public byte[] CapturedBytes => _capture.ToArray();

        public bool Truncated { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => _inner.CanWrite;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            Capture(buffer.AsSpan(offset, count));
            _inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Capture(buffer);
            _inner.Write(buffer);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Capture(buffer.AsSpan(offset, count));
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Capture(buffer.Span);
            return _inner.WriteAsync(buffer, cancellationToken);
        }

        private void Capture(ReadOnlySpan<byte> buffer)
        {
            var remaining = _limit - (int)_capture.Length;
            if (remaining <= 0)
            {
                Truncated = true;
                return;
            }

            var toWrite = Math.Min(remaining, buffer.Length);
            _capture.Write(buffer[..toWrite]);
            if (toWrite < buffer.Length)
            {
                Truncated = true;
            }
        }
    }
}

public sealed record RequestAnalyticsSnapshot(
    RequestAnalyticsSummary Summary,
    ListenerStatus Listener,
    IReadOnlyList<RequestLogEntry> Requests);

public sealed record RequestAnalyticsSummary(
    int TotalRequests,
    int TotalTokens,
    int InputTokens,
    int OutputTokens,
    decimal TotalCost,
    double AverageLatencySeconds);

public sealed record ListenerStatus(
    string Url,
    int Port,
    string Status);

public sealed record RequestLogEntry(
    DateTimeOffset Timestamp,
    string Method,
    string Path,
    string? Model,
    int StatusCode,
    long DurationMilliseconds,
    int InputTokens,
    int OutputTokens,
    decimal Cost,
    string UserAgent,
    IReadOnlyDictionary<string, string> RequestHeaders,
    string? RequestBody,
    IReadOnlyDictionary<string, string> ResponseHeaders,
    string? ResponseBody);
