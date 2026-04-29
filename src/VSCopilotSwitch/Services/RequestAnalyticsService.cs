using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

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
    private const int MaxUsageCaptureBytes = 128 * 1024;
    private static readonly JsonWriterOptions RedactedJsonWriterOptions = new() { Indented = true };
    private static readonly Regex PromptTokensPattern = new("\"(?:prompt_tokens|input_tokens|PromptTokens|InputTokens)\"\\s*:\\s*(\\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CompletionTokensPattern = new("\"(?:completion_tokens|output_tokens|CompletionTokens|OutputTokens)\"\\s*:\\s*(\\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TotalTokensPattern = new("\"(?:total_tokens|TotalTokens)\"\\s*:\\s*(\\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ApiKeyPattern = new(@"sk-[A-Za-z0-9_\-]{8,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BearerPattern = new(@"Bearer\s+[A-Za-z0-9._~+\-/]+=*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly ConcurrentQueue<RequestLogEntry> _entries = new();
    private readonly IUsageCostEstimator _costEstimator;

    public RequestAnalyticsService(IUsageCostEstimator costEstimator)
    {
        _costEstimator = costEstimator;
    }

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
        await using var responseCapture = new CapturingResponseBodyStream(originalResponseBody, MaxBodyCaptureBytes, MaxUsageCaptureBytes);
        context.Response.Body = responseCapture;

        try
        {
            await next();
        }
        finally
        {
            context.Response.Body = originalResponseBody;
            stopwatch.Stop();
            var usage = TryExtractUsage(responseCapture.UsageBytes);
            var inputTokens = usage?.InputTokens ?? EstimateInputTokens(context);
            var outputTokens = usage?.OutputTokens ?? EstimateOutputTokens(responseCapture.UsageBytes, context.Response.ContentType);
            var usageSource = usage is null ? "estimated" : "provider";
            var cost = _costEstimator.Estimate(model, inputTokens, outputTokens);
            Enqueue(new RequestLogEntry(
                startedAt,
                context.Request.Method,
                path,
                model,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                inputTokens,
                outputTokens,
                usage?.TotalTokens ?? inputTokens + outputTokens,
                usageSource,
                cost.Amount,
                cost.Currency,
                cost.Source,
                cost.PricingRule,
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
        var pricedRequests = entries.Count(entry => string.Equals(entry.CostSource, "configured", StringComparison.OrdinalIgnoreCase));
        var unpricedRequests = entries.Count(entry => string.Equals(entry.CostSource, "unpriced", StringComparison.OrdinalIgnoreCase));
        var averageLatency = totalRequests == 0 ? 0 : entries.Average(entry => entry.DurationMilliseconds) / 1000d;
        var currency = entries.FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.Currency))?.Currency ?? _costEstimator.Currency;

        return new RequestAnalyticsSnapshot(
            new RequestAnalyticsSummary(
                totalRequests,
                totalInputTokens + totalOutputTokens,
                totalInputTokens,
                totalOutputTokens,
                totalCost,
                currency,
                pricedRequests,
                unpricedRequests,
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
                && !string.Equals(path, "/api/show", StringComparison.OrdinalIgnoreCase)
                && !IsOpenAiChatCompletionPath(path)))
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

    private static bool IsOpenAiChatCompletionPath(string path)
        => string.Equals(path, "/v1/chat/completions", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/chat/completions", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/v1/v1/chat/completions", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/api/v1/chat/completions", StringComparison.OrdinalIgnoreCase);

    private static int EstimateInputTokens(HttpContext context)
    {
        var length = context.Request.ContentLength ?? 0;
        if (length <= 0)
        {
            return 0;
        }

        return (int)Math.Max(1, Math.Ceiling(length / 4d));
    }

    private static int EstimateOutputTokens(byte[] responseBytes, string? contentType)
    {
        if (responseBytes.Length == 0 || !IsTextLikeContentType(contentType))
        {
            return 0;
        }

        return (int)Math.Max(1, Math.Ceiling(responseBytes.Length / 4d));
    }

    private static TokenUsage? TryExtractUsage(byte[] responseBytes)
    {
        if (responseBytes.Length == 0)
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(responseBytes);
        var inputTokens = LastInt(PromptTokensPattern, text);
        var outputTokens = LastInt(CompletionTokensPattern, text);
        var totalTokens = LastInt(TotalTokensPattern, text);
        if (inputTokens is null && outputTokens is null && totalTokens is null)
        {
            return null;
        }

        var input = inputTokens ?? Math.Max(0, (totalTokens ?? 0) - (outputTokens ?? 0));
        var output = outputTokens ?? Math.Max(0, (totalTokens ?? 0) - input);
        var total = totalTokens ?? input + output;
        return new TokenUsage(input, output, total);
    }

    private static int? LastInt(Regex pattern, string text)
    {
        var matches = pattern.Matches(text);
        if (matches.Count == 0)
        {
            return null;
        }

        return int.TryParse(matches[^1].Groups[1].Value, out var value) ? value : null;
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
            return node is null ? string.Empty : WriteJsonNode(node, RedactedJsonWriterOptions);
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
                    jsonObject[property.Key] = JsonValue.Create("[已脱敏]");
                    continue;
                }

                if (TryCreateRedactedStringValue(property.Value, out var redactedValue))
                {
                    jsonObject[property.Key] = redactedValue;
                    continue;
                }

                RedactJsonNode(property.Value);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            for (var index = 0; index < jsonArray.Count; index++)
            {
                if (TryCreateRedactedStringValue(jsonArray[index], out var redactedValue))
                {
                    jsonArray[index] = redactedValue;
                    continue;
                }

                RedactJsonNode(jsonArray[index]);
            }
        }
    }

    private static bool TryCreateRedactedStringValue(JsonNode? node, out JsonValue? redactedValue)
    {
        redactedValue = null;
        if (node is not JsonValue value || !value.TryGetValue<string>(out var stringValue))
        {
            return false;
        }

        var redacted = RedactSecrets(stringValue);
        if (string.Equals(redacted, stringValue, StringComparison.Ordinal))
        {
            return false;
        }

        redactedValue = JsonValue.Create(redacted);
        return true;
    }

    private static string WriteJsonNode(JsonNode node, JsonWriterOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, options))
        {
            node.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
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

    private sealed record TokenUsage(int InputTokens, int OutputTokens, int TotalTokens);

    private sealed class CapturingResponseBodyStream : Stream
    {
        private readonly Stream _inner;
        private readonly MemoryStream _capture = new();
        private readonly MemoryStream _usageCapture = new();
        private readonly int _limit;
        private readonly int _usageLimit;

        public CapturingResponseBodyStream(Stream inner, int limit, int usageLimit)
        {
            _inner = inner;
            _limit = limit;
            _usageLimit = usageLimit;
        }

        public byte[] CapturedBytes => _capture.ToArray();

        public byte[] UsageBytes => _usageCapture.ToArray();

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
            CaptureUsage(buffer);
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

        private void CaptureUsage(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length >= _usageLimit)
            {
                _usageCapture.SetLength(0);
                _usageCapture.Write(buffer[^_usageLimit..]);
                return;
            }

            if (_usageCapture.Length + buffer.Length <= _usageLimit)
            {
                _usageCapture.Write(buffer);
                return;
            }

            var existing = _usageCapture.ToArray();
            var overflow = (int)(_usageCapture.Length + buffer.Length - _usageLimit);
            _usageCapture.SetLength(0);
            _usageCapture.Write(existing.AsSpan(overflow));
            _usageCapture.Write(buffer);
        }
    }
}

public sealed record RequestAnalyticsSnapshot(
    RequestAnalyticsSummary Summary,
    ListenerStatus Listener,
    IReadOnlyList<RequestLogEntry> Requests);

public interface IUsageCostEstimator
{
    string Currency { get; }

    UsageCostEstimate Estimate(string? model, int inputTokens, int outputTokens);
}

public sealed class UsageCostEstimator : IUsageCostEstimator
{
    private const string VsCodeModelSuffix = "@vscs";
    private readonly UsagePricingOptions _options;

    public UsageCostEstimator(IOptions<UsagePricingOptions> options)
    {
        _options = options.Value ?? new UsagePricingOptions();
    }

    public string Currency => NormalizeCurrency(_options.Currency);

    public UsageCostEstimate Estimate(string? model, int inputTokens, int outputTokens)
    {
        var rule = FindRule(model);
        var inputRate = rule?.InputPerMillionTokens ?? _options.DefaultInputPerMillionTokens;
        var outputRate = rule?.OutputPerMillionTokens ?? _options.DefaultOutputPerMillionTokens;
        if (inputRate <= 0m && outputRate <= 0m)
        {
            return new UsageCostEstimate(0m, Currency, "unpriced", null);
        }

        var amount = inputTokens * inputRate / 1_000_000m
            + outputTokens * outputRate / 1_000_000m;
        var pricingRule = rule is null
            ? "default"
            : string.IsNullOrWhiteSpace(rule.Label) ? rule.ModelPattern : rule.Label.Trim();
        return new UsageCostEstimate(decimal.Round(amount, 8, MidpointRounding.AwayFromZero), Currency, "configured", pricingRule);
    }

    private UsagePriceRule? FindRule(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        var candidates = BuildModelCandidates(model);
        return _options.Models
            .Where(rule => !string.IsNullOrWhiteSpace(rule.ModelPattern))
            .FirstOrDefault(rule => candidates.Any(candidate => WildcardMatch(candidate, rule.ModelPattern)));
    }

    private static IReadOnlyList<string> BuildModelCandidates(string model)
    {
        var normalized = StripVsCodeSuffix(model.Trim());
        var slashIndex = normalized.LastIndexOf('/');
        return slashIndex >= 0
            ? new[] { normalized, normalized[(slashIndex + 1)..] }
            : new[] { normalized };
    }

    private static string StripVsCodeSuffix(string model)
        => model.EndsWith(VsCodeModelSuffix, StringComparison.OrdinalIgnoreCase)
            ? model[..^VsCodeModelSuffix.Length]
            : model;

    private static bool WildcardMatch(string value, string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern.Trim())
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(value, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string NormalizeCurrency(string? currency)
        => string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
}

public sealed class UsagePricingOptions
{
    public string Currency { get; set; } = "USD";

    public decimal DefaultInputPerMillionTokens { get; set; }

    public decimal DefaultOutputPerMillionTokens { get; set; }

    public List<UsagePriceRule> Models { get; set; } = new();
}

public sealed class UsagePriceRule
{
    public string ModelPattern { get; set; } = string.Empty;

    public string? Label { get; set; }

    public decimal InputPerMillionTokens { get; set; }

    public decimal OutputPerMillionTokens { get; set; }
}

public sealed record UsageCostEstimate(
    decimal Amount,
    string Currency,
    string Source,
    string? PricingRule);

public sealed record RequestAnalyticsSummary(
    int TotalRequests,
    int TotalTokens,
    int InputTokens,
    int OutputTokens,
    decimal TotalCost,
    string Currency,
    int PricedRequests,
    int UnpricedRequests,
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
    int TotalTokens,
    string UsageSource,
    decimal Cost,
    string Currency,
    string CostSource,
    string? PricingRule,
    string UserAgent,
    IReadOnlyDictionary<string, string> RequestHeaders,
    string? RequestBody,
    IReadOnlyDictionary<string, string> ResponseHeaders,
    string? ResponseBody);
