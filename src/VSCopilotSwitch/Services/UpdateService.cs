using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace VSCopilotSwitch.Services;

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    Task<UpdateDownloadResult> DownloadLatestAsync(
        UpdateDownloadRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class UpdateOptions
{
    public bool Enabled { get; set; } = true;

    public bool AutoDownload { get; set; } = true;

    public int CheckIntervalHours { get; set; } = 6;

    public bool IncludePrerelease { get; set; }

    public string? CurrentVersionOverride { get; set; }

    public string? CacheDirectory { get; set; }

    public List<string> AssetNameHints { get; set; } = new()
    {
        "VSCopilotSwitch",
        "win-x64",
        "aot"
    };

    public List<UpdateSourceOptions> Sources { get; set; } = new();
}

public sealed class UpdateSourceOptions
{
    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = "GitHub";

    public string Repository { get; set; } = string.Empty;

    public string? ApiUrl { get; set; }
}

public sealed class UpdateService : IUpdateService
{
    private static readonly string[] PreferredAssetExtensions = [".exe", ".zip", ".msi"];
    private readonly HttpClient _httpClient;
    private readonly UpdateOptions _options;
    private readonly Func<string> _currentVersionProvider;

    public UpdateService(HttpClient httpClient, IOptions<UpdateOptions> options)
        : this(httpClient, options.Value, ResolveCurrentVersion)
    {
    }

    public UpdateService(HttpClient httpClient, UpdateOptions options, Func<string>? currentVersionProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _currentVersionProvider = currentVersionProvider ?? ResolveCurrentVersion;
        ConfigureDefaultHeaders(_httpClient);
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = CleanVersionText(_options.CurrentVersionOverride) ?? _currentVersionProvider();
        var checkedAt = DateTimeOffset.UtcNow;
        if (!_options.Enabled)
        {
            return new UpdateCheckResult(
                currentVersion,
                checkedAt,
                UpdateAvailable: false,
                LatestRelease: null,
                Sources: [new UpdateSourceCheckResult("更新策略", false, "自动更新已禁用。", null)]);
        }

        var sourceResults = new List<UpdateSourceCheckResult>();
        foreach (var source in ResolveSources())
        {
            cancellationToken.ThrowIfCancellationRequested();
            sourceResults.Add(await CheckSourceAsync(source, cancellationToken));
        }

        var latest = sourceResults
            .Where(result => result.Success && result.Release is not null)
            .Select(result => result.Release!)
            .OrderByDescending(release => ParseComparableVersion(release.Version), VersionComparer.Instance)
            .ThenByDescending(release => release.PublishedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
        var updateAvailable = latest is not null && IsNewerThan(latest.Version, currentVersion);

        return new UpdateCheckResult(
            currentVersion,
            checkedAt,
            updateAvailable,
            latest,
            sourceResults);
    }

    public async Task<UpdateDownloadResult> DownloadLatestAsync(
        UpdateDownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        var check = await CheckAsync(cancellationToken);
        var release = SelectDownloadRelease(check, request);
        if (release is null)
        {
            return new UpdateDownloadResult(false, "当前没有可下载的新版本。", null, 0, check.LatestRelease);
        }

        if (release.Asset is null)
        {
            return new UpdateDownloadResult(false, "已发现新版本，但 Release 中没有匹配 Windows 单文件发布包。", null, 0, release);
        }

        if (!Uri.TryCreate(release.Asset.DownloadUrl, UriKind.Absolute, out var downloadUri)
            || downloadUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("更新下载地址无效。");
        }

        var fileName = Path.GetFileName(downloadUri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = SanitizeFileName(release.Asset.Name);
        }

        var targetDirectory = Path.Combine(ResolveCacheDirectory(), SanitizeFileName(release.TagName));
        Directory.CreateDirectory(targetDirectory);
        var targetPath = Path.Combine(targetDirectory, fileName);
        if (File.Exists(targetPath)
            && release.Asset.SizeBytes > 0
            && new FileInfo(targetPath).Length == release.Asset.SizeBytes)
        {
            return new UpdateDownloadResult(false, "更新包已存在，无需重复下载。", targetPath, release.Asset.SizeBytes, release);
        }

        using var response = await _httpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var tempPath = $"{targetPath}.download";
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = File.Create(tempPath))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        await VerifySha256Async(tempPath, release.Asset, cancellationToken);
        File.Move(tempPath, targetPath, overwrite: true);

        var size = new FileInfo(targetPath).Length;
        return new UpdateDownloadResult(true, "更新包已下载到本地缓存，重启后可按发布包说明替换。", targetPath, size, release);
    }

    private async Task<UpdateSourceCheckResult> CheckSourceAsync(
        UpdateSourceOptions source,
        CancellationToken cancellationToken)
    {
        try
        {
            var apiUrl = ResolveApiUrl(source);
            using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateSourceCheckResult(
                    DisplaySourceName(source),
                    false,
                    $"读取 Release 失败：HTTP {(int)response.StatusCode}",
                    null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var releaseElement = SelectReleaseElement(document.RootElement, _options.IncludePrerelease);
            if (releaseElement.ValueKind is JsonValueKind.Undefined)
            {
                return new UpdateSourceCheckResult(DisplaySourceName(source), false, "没有找到可用 Release。", null);
            }

            var release = ParseRelease(source, releaseElement);
            return new UpdateSourceCheckResult(DisplaySourceName(source), true, "已读取 Release。", release);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateSourceCheckResult(DisplaySourceName(source), false, $"读取 Release 异常：{ex.Message}", null);
        }
    }

    private UpdateReleaseInfo ParseRelease(UpdateSourceOptions source, JsonElement release)
    {
        var tagName = FirstString(release, "tag_name", "tagName", "tag") ?? string.Empty;
        var version = CleanVersionText(tagName) ?? tagName;
        var assets = ReadAssets(release).ToArray();
        var asset = SelectAsset(assets, _options.AssetNameHints);

        return new UpdateReleaseInfo(
            DisplaySourceName(source),
            tagName,
            version,
            FirstString(release, "name", "title") ?? tagName,
            FirstString(release, "body", "description") ?? string.Empty,
            FirstString(release, "html_url", "htmlUrl", "url") ?? string.Empty,
            FirstDate(release, "published_at", "publishedAt", "created_at", "createdAt"),
            FirstBool(release, "prerelease", "pre_release") ?? false,
            asset);
    }

    private IEnumerable<UpdateAssetInfo> ReadAssets(JsonElement release)
    {
        foreach (var propertyName in new[] { "assets" })
        {
            if (!release.TryGetProperty(propertyName, out var assets) || assets.ValueKind is not JsonValueKind.Array)
            {
                continue;
            }

            foreach (var asset in assets.EnumerateArray())
            {
                var name = FirstString(asset, "name", "filename", "file_name") ?? string.Empty;
                var downloadUrl = FirstString(asset, "browser_download_url", "download_url", "downloadUrl", "url") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    continue;
                }

                yield return new UpdateAssetInfo(
                    name,
                    downloadUrl,
                    FirstLong(asset, "size", "size_bytes", "sizeBytes") ?? 0,
                    FirstString(asset, "content_type", "contentType"),
                    FirstString(asset, "digest", "sha256"));
            }
        }
    }

    private static UpdateAssetInfo? SelectAsset(IReadOnlyList<UpdateAssetInfo> assets, IReadOnlyList<string> hints)
        => assets
            .Select(asset => new
            {
                Asset = asset,
                Score = ScoreAsset(asset, hints)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Asset.SizeBytes)
            .Select(item => item.Asset)
            .FirstOrDefault();

    private static int ScoreAsset(UpdateAssetInfo asset, IReadOnlyList<string> hints)
    {
        var name = asset.Name;
        var extension = Path.GetExtension(name);
        var score = PreferredAssetExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ? 20 : 0;
        foreach (var hint in hints.Where(static hint => !string.IsNullOrWhiteSpace(hint)))
        {
            if (name.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                score += 5;
            }
        }

        if (name.Contains("win", StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
        }

        if (name.Contains("x64", StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
        }

        return score;
    }

    private static UpdateReleaseInfo? SelectDownloadRelease(UpdateCheckResult check, UpdateDownloadRequest request)
    {
        if (check.LatestRelease is null)
        {
            return null;
        }

        if (!check.UpdateAvailable)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.SourceName) && string.IsNullOrWhiteSpace(request.TagName))
        {
            return check.LatestRelease;
        }

        return check.Sources
            .Where(source => source.Release is not null)
            .Select(source => source.Release!)
            .FirstOrDefault(release =>
                (string.IsNullOrWhiteSpace(request.SourceName)
                 || string.Equals(release.SourceName, request.SourceName, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(request.TagName)
                    || string.Equals(release.TagName, request.TagName, StringComparison.OrdinalIgnoreCase)));
    }

    private IReadOnlyList<UpdateSourceOptions> ResolveSources()
    {
        var configuredGitHubSources = _options.Sources
            .Where(source => source.Kind.Equals("GitHub", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (configuredGitHubSources.Length > 0)
        {
            return configuredGitHubSources;
        }

        return
        [
            new UpdateSourceOptions
            {
                Name = "GitHub",
                Kind = "GitHub",
                Repository = "maikebing/VSCopilotSwitch"
            }
        ];
    }

    private string ResolveApiUrl(UpdateSourceOptions source)
    {
        if (!string.IsNullOrWhiteSpace(source.ApiUrl))
        {
            return source.ApiUrl;
        }

        if (string.IsNullOrWhiteSpace(source.Repository))
        {
            throw new InvalidOperationException($"更新源 {DisplaySourceName(source)} 未配置 Repository。");
        }

        if (!source.Kind.Equals("GitHub", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"更新源 {DisplaySourceName(source)} 不受支持；当前仅支持 GitHub Release。");
        }

        return $"https://api.github.com/repos/{source.Repository}/releases/latest";
    }

    private string ResolveCacheDirectory()
        => !string.IsNullOrWhiteSpace(_options.CacheDirectory)
            ? Path.GetFullPath(_options.CacheDirectory)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VSCopilotSwitch",
                "Updates");

    private static async Task VerifySha256Async(
        string path,
        UpdateAssetInfo asset,
        CancellationToken cancellationToken)
    {
        var expected = NormalizeSha256(asset.Sha256);
        if (string.IsNullOrWhiteSpace(expected))
        {
            return;
        }

        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidOperationException("更新包 SHA256 校验失败，已删除下载文件。");
        }
    }

    private static string? NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["sha256:".Length..];
        }

        return trimmed.Length == 64 && trimmed.All(Uri.IsHexDigit) ? trimmed.ToLowerInvariant() : null;
    }

    private static JsonElement SelectReleaseElement(JsonElement root, bool includePrerelease)
    {
        if (root.ValueKind is JsonValueKind.Object)
        {
            return root;
        }

        if (root.ValueKind is not JsonValueKind.Array)
        {
            return default;
        }

        return root.EnumerateArray()
            .Where(item => includePrerelease || FirstBool(item, "prerelease", "pre_release") != true)
            .OrderByDescending(item => FirstDate(item, "published_at", "publishedAt", "created_at", "createdAt") ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static bool? FirstBool(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }
        }

        return null;
    }

    private static long? FirstLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number))
            {
                return number;
            }
        }

        return null;
    }

    private static DateTimeOffset? FirstDate(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var value = FirstString(element, name);
            if (DateTimeOffset.TryParse(value, out var date))
            {
                return date;
            }
        }

        return null;
    }

    private static bool IsNewerThan(string version, string currentVersion)
    {
        var next = ParseComparableVersion(version);
        var current = ParseComparableVersion(currentVersion);
        return next is not null && current is not null && next.CompareTo(current) > 0;
    }

    private static Version? ParseComparableVersion(string? value)
    {
        var cleaned = CleanVersionText(value);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        var core = cleaned.Split('-', '+')[0];
        var parts = core.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 4 || parts.Any(part => !int.TryParse(part, out _)))
        {
            return null;
        }

        var normalized = parts.Concat(Enumerable.Repeat("0", 4 - parts.Length)).ToArray();
        return Version.TryParse(string.Join('.', normalized), out var version) ? version : null;
    }

    private static string? CleanVersionText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith('v') || trimmed.StartsWith('V') ? trimmed[1..] : trimmed;
    }

    private static string ResolveCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return CleanVersionText(informational?.Split('+')[0]) ?? "0.0.0";
    }

    private static string DisplaySourceName(UpdateSourceOptions source)
        => string.IsNullOrWhiteSpace(source.Name) ? source.Kind : source.Name.Trim();

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "VSCopilotSwitch-update.bin" : safe;
    }

    private static void ConfigureDefaultHeaders(HttpClient httpClient)
    {
        if (!httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VSCopilotSwitch", "0.1"));
        }

        if (!httpClient.DefaultRequestHeaders.Accept.Any())
        {
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    private sealed class VersionComparer : IComparer<Version?>
    {
        public static VersionComparer Instance { get; } = new();

        public int Compare(Version? x, Version? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            return x.CompareTo(y);
        }
    }
}

public sealed record UpdateCheckResult(
    string CurrentVersion,
    DateTimeOffset CheckedAt,
    bool UpdateAvailable,
    UpdateReleaseInfo? LatestRelease,
    IReadOnlyList<UpdateSourceCheckResult> Sources);

public sealed record UpdateSourceCheckResult(
    string SourceName,
    bool Success,
    string Message,
    UpdateReleaseInfo? Release);

public sealed record UpdateReleaseInfo(
    string SourceName,
    string TagName,
    string Version,
    string Name,
    string Body,
    string PageUrl,
    DateTimeOffset? PublishedAt,
    bool Prerelease,
    UpdateAssetInfo? Asset);

public sealed record UpdateAssetInfo(
    string Name,
    string DownloadUrl,
    long SizeBytes,
    string? ContentType,
    string? Sha256);

public sealed record UpdateDownloadRequest(string? SourceName = null, string? TagName = null);

public sealed record UpdateDownloadResult(
    bool Downloaded,
    string Message,
    string? FilePath,
    long SizeBytes,
    UpdateReleaseInfo? Release);

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(UpdateCheckResult))]
[JsonSerializable(typeof(UpdateSourceCheckResult))]
[JsonSerializable(typeof(UpdateReleaseInfo))]
[JsonSerializable(typeof(UpdateAssetInfo))]
[JsonSerializable(typeof(IReadOnlyList<UpdateSourceCheckResult>))]
internal sealed partial class UpdateServiceJsonContext : JsonSerializerContext;
