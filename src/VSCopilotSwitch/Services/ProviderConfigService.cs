using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VSCopilotSwitch.Services;

public interface IProviderConfigService
{
    Task<IReadOnlyList<ProviderConfigView>> ListAsync(CancellationToken cancellationToken = default);

    Task<ProviderRuntimeConfig?> GetActiveRuntimeConfigAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderConfigView>> SaveAsync(
        SaveProviderConfigRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderConfigView>> DeleteAsync(string providerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderConfigView>> ActivateAsync(string providerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderConfigView>> ReorderAsync(
        ReorderProvidersRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ProviderConfigService : IProviderConfigService
{
    private const int CurrentVersion = 1;
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ProviderConfigService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _filePath = Path.Combine(appData, "VSCopilotSwitch", "providers.json");
    }

    public async Task<IReadOnlyList<ProviderConfigView>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentAsync(cancellationToken);
            return ToViews(document.Providers);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ProviderRuntimeConfig?> GetActiveRuntimeConfigAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentAsync(cancellationToken);
            var provider = document.Providers
                .OrderBy(item => item.SortOrder)
                .FirstOrDefault(item => item.Active);

            if (provider is null)
            {
                return null;
            }

            return new ProviderRuntimeConfig(
                provider.Id,
                provider.Name,
                provider.ApiUrl,
                provider.Model,
                provider.Vendor,
                UnprotectSecret(provider.EncryptedApiKey));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ProviderConfigView>> SaveAsync(
        SaveProviderConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateProviderRequest(request);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentAsync(cancellationToken);
            var existing = document.Providers.FirstOrDefault(provider => string.Equals(provider.Id, request.Id, StringComparison.OrdinalIgnoreCase));
            var id = string.IsNullOrWhiteSpace(request.Id) ? CreateProviderId(request.Name) : request.Id.Trim();
            var encryptedApiKey = !string.IsNullOrWhiteSpace(request.ApiKey)
                ? ProtectSecret(request.ApiKey)
                : existing?.EncryptedApiKey;

            var provider = new ProviderConfig(
                id,
                request.Name.Trim(),
                request.Remark?.Trim() ?? string.Empty,
                request.Url.Trim(),
                request.ApiUrl.Trim(),
                request.Model.Trim(),
                request.Vendor,
                CreateAvatar(request.Name),
                request.Active || existing?.Active == true,
                existing?.SortOrder ?? NextSortOrder(document.Providers),
                encryptedApiKey);

            document.Providers.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (provider.Active)
            {
                document.Providers = document.Providers
                    .Select(item => item with { Active = false })
                    .ToList();
            }

            document.Providers.Add(provider);
            NormalizeSortOrder(document.Providers);
            await SaveDocumentAsync(document, cancellationToken);
            return ToViews(document.Providers);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ProviderConfigView>> DeleteAsync(string providerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentAsync(cancellationToken);
            var removed = document.Providers.RemoveAll(provider => string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed && document.Providers.Count > 0 && document.Providers.All(provider => !provider.Active))
            {
                document.Providers[0] = document.Providers[0] with { Active = true };
            }

            NormalizeSortOrder(document.Providers);
            await SaveDocumentAsync(document, cancellationToken);
            return ToViews(document.Providers);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ProviderConfigView>> ActivateAsync(string providerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentAsync(cancellationToken);
            if (document.Providers.All(provider => !string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("供应商不存在，无法启用。");
            }

            document.Providers = document.Providers
                .Select(provider => provider with { Active = string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase) })
                .ToList();
            await SaveDocumentAsync(document, cancellationToken);
            return ToViews(document.Providers);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ProviderConfigView>> ReorderAsync(
        ReorderProvidersRequest request,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentAsync(cancellationToken);
            var byId = document.Providers.ToDictionary(provider => provider.Id, StringComparer.OrdinalIgnoreCase);
            var ordered = new List<ProviderConfig>();

            foreach (var id in request.ProviderIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (byId.Remove(id, out var provider))
                {
                    ordered.Add(provider);
                }
            }

            ordered.AddRange(byId.Values.OrderBy(provider => provider.SortOrder));
            document.Providers = ordered;
            NormalizeSortOrder(document.Providers);
            await SaveDocumentAsync(document, cancellationToken);
            return ToViews(document.Providers);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ProviderConfigDocument> LoadDocumentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new ProviderConfigDocument(CurrentVersion, CreateDefaultProviders());
        }

        await using var stream = File.OpenRead(_filePath);
        var document = await JsonSerializer.DeserializeAsync<ProviderConfigDocument>(stream, JsonOptions, cancellationToken)
            ?? new ProviderConfigDocument(CurrentVersion, new List<ProviderConfig>());
        document.Providers ??= new List<ProviderConfig>();
        NormalizeSortOrder(document.Providers);
        return document;
    }

    private async Task SaveDocumentAsync(ProviderConfigDocument document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tempPath = $"{_filePath}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }

    private static List<ProviderConfig> CreateDefaultProviders()
        => new()
        {
            new ProviderConfig(
                "my-codex",
                "My Codex",
                string.Empty,
                "https://how88.top",
                "https://how88.top",
                "gpt-5.5",
                "codex",
                "MC",
                Active: true,
                SortOrder: 0,
                EncryptedApiKey: null)
        };

    private static IReadOnlyList<ProviderConfigView> ToViews(IEnumerable<ProviderConfig> providers)
        => providers
            .OrderBy(provider => provider.SortOrder)
            .Select(provider => new ProviderConfigView(
                provider.Id,
                provider.Name,
                provider.Remark,
                provider.Url,
                provider.ApiUrl,
                provider.Model,
                provider.Vendor,
                provider.Avatar,
                provider.Active,
                provider.EncryptedApiKey is not null,
                MaskSecret(provider.EncryptedApiKey),
                provider.SortOrder))
            .ToArray();

    private static void ValidateProviderRequest(SaveProviderConfigRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("供应商名称不能为空。");
        }

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var website)
            || website.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("供应商官网链接必须是 http 或 https URL。");
        }

        if (!Uri.TryCreate(request.ApiUrl, UriKind.Absolute, out var apiUrl)
            || apiUrl.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("API 请求地址必须是 http 或 https URL。");
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new InvalidOperationException("模型名称不能为空。");
        }
    }

    private static string CreateProviderId(string name)
    {
        var safe = new string(name.Trim().ToLowerInvariant().Select(ch =>
            char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        safe = string.Join('-', safe.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(safe) ? $"provider-{Guid.NewGuid():N}" : $"{safe}-{Guid.NewGuid():N}"[..Math.Min(safe.Length + 33, 64)];
    }

    private static string CreateAvatar(string name)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "?";
        }

        var chars = trimmed.EnumerateRunes().Take(2).Select(rune => rune.ToString());
        return string.Concat(chars).ToUpperInvariant();
    }

    private static int NextSortOrder(IEnumerable<ProviderConfig> providers)
        => providers.Any() ? providers.Max(provider => provider.SortOrder) + 1 : 0;

    private static void NormalizeSortOrder(List<ProviderConfig> providers)
    {
        var ordered = providers.OrderBy(provider => provider.SortOrder).ToArray();
        providers.Clear();
        for (var index = 0; index < ordered.Length; index++)
        {
            providers.Add(ordered[index] with { SortOrder = index });
        }
    }

    private static string ProtectSecret(string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string? UnprotectSecret(string? protectedSecret)
    {
        if (string.IsNullOrWhiteSpace(protectedSecret))
        {
            return null;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedSecret);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static string? MaskSecret(string? protectedSecret)
    {
        var secret = UnprotectSecret(protectedSecret);
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var suffix = secret.Length <= 4 ? secret : secret[^4..];
        var prefix = secret.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) ? "sk-" : string.Empty;
        return $"{prefix}...{suffix}";
    }

    private sealed class ProviderConfigDocument
    {
        public ProviderConfigDocument(int version, List<ProviderConfig> providers)
        {
            Version = version;
            Providers = providers;
        }

        public int Version { get; set; }

        public List<ProviderConfig> Providers { get; set; }
    }

    private sealed record ProviderConfig(
        string Id,
        string Name,
        string Remark,
        string Url,
        string ApiUrl,
        string Model,
        string Vendor,
        string Avatar,
        bool Active,
        int SortOrder,
        string? EncryptedApiKey);
}

public sealed record ProviderConfigView(
    string Id,
    string Name,
    string Remark,
    string Url,
    string ApiUrl,
    string Model,
    string Vendor,
    string Avatar,
    bool Active,
    bool HasApiKey,
    string? ApiKeyPreview,
    int SortOrder);

public sealed record ProviderRuntimeConfig(
    string Id,
    string Name,
    string ApiUrl,
    string Model,
    string Vendor,
    string? ApiKey);

public sealed record SaveProviderConfigRequest(
    string? Id,
    string Name,
    string? Remark,
    string Url,
    string ApiUrl,
    string Model,
    string Vendor,
    string? ApiKey,
    bool Active);

public sealed record ReorderProvidersRequest(IReadOnlyList<string> ProviderIds);
