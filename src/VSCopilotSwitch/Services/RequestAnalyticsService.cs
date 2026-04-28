using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace VSCopilotSwitch.Services;

public interface IRequestAnalyticsService
{
    Task InvokeAsync(HttpContext context, Func<Task> next);

    RequestAnalyticsSnapshot GetSnapshot(string listeningUrl, bool ollamaPortAvailable);

    void Clear();
}

public sealed class RequestAnalyticsService : IRequestAnalyticsService
{
    private const int MaxEntries = 500;
    private readonly ConcurrentQueue<RequestLogEntry> _entries = new();

    public async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (ShouldSkip(path))
        {
            await next();
            return;
        }

        var model = await TryReadModelAsync(context);
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await next();
        }
        finally
        {
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
                SanitizeUserAgent(context.Request.Headers.UserAgent.ToString())));
        }
    }

    public RequestAnalyticsSnapshot GetSnapshot(string listeningUrl, bool ollamaPortAvailable)
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
                "运行中",
                ollamaPortAvailable),
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

    private static async Task<string?> TryReadModelAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method)
            || !string.Equals(context.Request.Path.Value, "/api/chat", StringComparison.OrdinalIgnoreCase)
            || context.Request.Body is null)
        {
            return null;
        }

        try
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("model", out var modelElement)
                ? modelElement.GetString()
                : null;
        }
        catch
        {
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Position = 0;
            }

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

    private static int TryGetPort(string listeningUrl)
        => Uri.TryCreate(listeningUrl, UriKind.Absolute, out var uri) ? uri.Port : 0;
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
    string Status,
    bool OllamaPortAvailable);

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
    string UserAgent);
