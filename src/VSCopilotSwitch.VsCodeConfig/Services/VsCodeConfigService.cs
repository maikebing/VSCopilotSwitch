using System.Text.Json;
using System.Text.Json.Nodes;
using VSCopilotSwitch.VsCodeConfig.Models;

namespace VSCopilotSwitch.VsCodeConfig.Services;

public interface IVsCodeConfigService
{
    Task<VsCodeConfigApplyResult> ApplyOllamaConfigAsync(
        string userDirectory,
        ManagedOllamaConfig config,
        bool dryRun,
        CancellationToken cancellationToken = default);

    IReadOnlyList<VsCodeConfigBackup> ListBackups(string userDirectory);

    Task<VsCodeConfigRestoreResult> RestoreBackupAsync(
        string userDirectory,
        string backupPath,
        CancellationToken cancellationToken = default);
}

public sealed class VsCodeConfigService : IVsCodeConfigService
{
    public const string ManagedBy = "VSCopilotSwitch";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public async Task<VsCodeConfigApplyResult> ApplyOllamaConfigAsync(
        string userDirectory,
        ManagedOllamaConfig config,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userDirectory))
        {
            throw new ArgumentException("VS Code user directory is required.", nameof(userDirectory));
        }

        var fullUserDirectory = Path.GetFullPath(userDirectory);
        var changes = new List<VsCodeConfigFileChange>
        {
            await BuildSettingsChangeAsync(fullUserDirectory, config, dryRun, cancellationToken),
            await BuildChatLanguageModelsChangeAsync(fullUserDirectory, config, dryRun, cancellationToken)
        };

        if (!dryRun)
        {
            Directory.CreateDirectory(fullUserDirectory);
            foreach (var change in changes.Where(change => change.Changed))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(change.FilePath)!);
                if (change.ExistedBefore)
                {
                    File.Copy(change.FilePath, change.BackupPath!, overwrite: false);
                }

                await File.WriteAllTextAsync(change.FilePath, change.AfterContent, cancellationToken);
            }
        }

        return new VsCodeConfigApplyResult(fullUserDirectory, dryRun, changes);
    }

    public IReadOnlyList<VsCodeConfigBackup> ListBackups(string userDirectory)
    {
        var fullUserDirectory = ResolveUserDirectory(userDirectory);
        if (!Directory.Exists(fullUserDirectory))
        {
            return Array.Empty<VsCodeConfigBackup>();
        }

        return Directory.EnumerateFiles(fullUserDirectory, "*.vscopilotswitch.*.bak", SearchOption.TopDirectoryOnly)
            .Select(CreateBackupInfo)
            .Where(backup => backup is not null)
            .Cast<VsCodeConfigBackup>()
            .OrderByDescending(backup => backup.CreatedAt)
            .Take(20)
            .ToArray();
    }

    public async Task<VsCodeConfigRestoreResult> RestoreBackupAsync(
        string userDirectory,
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        var fullUserDirectory = ResolveUserDirectory(userDirectory);
        var fullBackupPath = ResolveBackupPath(fullUserDirectory, backupPath);
        var targetPath = GetTargetPathFromBackup(fullBackupPath);
        var safetyBackupPath = File.Exists(targetPath) ? CreateRestoreSafetyBackupPath(targetPath) : null;

        Directory.CreateDirectory(fullUserDirectory);
        if (safetyBackupPath is not null)
        {
            File.Copy(targetPath, safetyBackupPath, overwrite: false);
        }

        await using var source = File.OpenRead(fullBackupPath);
        await using var destination = File.Create(targetPath);
        await source.CopyToAsync(destination, cancellationToken);

        return new VsCodeConfigRestoreResult(fullUserDirectory, targetPath, fullBackupPath, safetyBackupPath, Restored: true);
    }

    private static async Task<VsCodeConfigFileChange> BuildSettingsChangeAsync(
        string userDirectory,
        ManagedOllamaConfig config,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(userDirectory, "settings.json");
        var before = await ReadOrDefaultAsync(filePath, "{}", cancellationToken);
        var root = ParseObjectOrEmpty(before);

        root["vscopilotswitch.managedBy"] = ManagedBy;
        root["vscopilotswitch.ollama.baseUrl"] = config.BaseUrl;
        root["vscopilotswitch.ollama.enabled"] = true;

        var after = ToJson(root);
        var fieldChanges = new[]
        {
            CreateFieldChange("vscopilotswitch.managedBy", before, after),
            CreateFieldChange("vscopilotswitch.ollama.baseUrl", before, after),
            CreateFieldChange("vscopilotswitch.ollama.enabled", before, after)
        };
        return CreateChange(filePath, before, after, dryRun, fieldChanges);
    }

    private static async Task<VsCodeConfigFileChange> BuildChatLanguageModelsChangeAsync(
        string userDirectory,
        ManagedOllamaConfig config,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(userDirectory, "chatLanguageModels.json");
        var before = await ReadOrDefaultAsync(filePath, "{}", cancellationToken);
        var root = ParseObjectOrEmpty(before);

        var managed = new JsonObject
        {
            ["managedBy"] = ManagedBy,
            ["provider"] = "ollama",
            ["baseUrl"] = config.BaseUrl,
            ["models"] = new JsonArray(config.Models.Select(model => new JsonObject
            {
                ["id"] = model.Id,
                ["displayName"] = model.DisplayName,
                ["providerModelId"] = model.ProviderModelId
            }).ToArray<JsonNode?>())
        };

        root["vscopilotswitch"] = managed;
        var after = ToJson(root);
        var fieldChanges = new[]
        {
            CreateFieldChange("vscopilotswitch.managedBy", before, after),
            CreateFieldChange("vscopilotswitch.provider", before, after),
            CreateFieldChange("vscopilotswitch.baseUrl", before, after),
            CreateFieldChange("vscopilotswitch.models", before, after)
        };
        return CreateChange(filePath, before, after, dryRun, fieldChanges);
    }

    private static async Task<string> ReadOrDefaultAsync(string filePath, string defaultContent, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return defaultContent;
        }

        return await File.ReadAllTextAsync(filePath, cancellationToken);
    }

    private static JsonObject ParseObjectOrEmpty(string content)
    {
        try
        {
            return JsonNode.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content) as JsonObject ?? new JsonObject();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The VS Code JSON file could not be parsed. Make a backup and fix invalid JSON before applying changes.", ex);
        }
    }

    private static VsCodeConfigFileChange CreateChange(
        string filePath,
        string before,
        string after,
        bool dryRun,
        IReadOnlyList<VsCodeConfigFieldChange> fieldChanges)
    {
        var existedBefore = File.Exists(filePath);
        var changed = !JsonEquivalent(before, after);
        var backupPath = changed && existedBefore && !dryRun ? CreateBackupPath(filePath) : null;
        return new VsCodeConfigFileChange(filePath, existedBefore, changed, backupPath, before, after, fieldChanges);
    }

    private static VsCodeConfigFieldChange CreateFieldChange(string path, string before, string after)
    {
        var beforeValue = ReadJsonPath(before, path);
        var afterValue = ReadJsonPath(after, path);
        return new VsCodeConfigFieldChange(path, beforeValue, afterValue, beforeValue != afterValue);
    }

    private static string ReadJsonPath(string content, string path)
    {
        try
        {
            var node = JsonNode.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
            if (node is JsonObject root && root.TryGetPropertyValue(path, out var directValue))
            {
                return directValue is null ? "未设置" : JsonSerializer.Serialize(directValue);
            }

            foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                node = node?[segment];
            }

            return node is null ? "未设置" : JsonSerializer.Serialize(node);
        }
        catch
        {
            return "无法解析";
        }
    }

    private static bool JsonEquivalent(string left, string right)
    {
        try
        {
            var leftNode = JsonNode.Parse(left);
            var rightNode = JsonNode.Parse(right);
            return JsonSerializer.Serialize(leftNode) == JsonSerializer.Serialize(rightNode);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }
    }

    private static string CreateBackupPath(string filePath)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        return $"{filePath}.vscopilotswitch.{timestamp}.bak";
    }

    private static VsCodeConfigBackup? CreateBackupInfo(string backupPath)
    {
        var targetPath = GetTargetPathFromBackup(backupPath);
        if (!IsManagedFileName(Path.GetFileName(targetPath)))
        {
            return null;
        }

        var fileInfo = new FileInfo(backupPath);
        return new VsCodeConfigBackup(targetPath, fileInfo.FullName, Path.GetFileName(targetPath), fileInfo.CreationTimeUtc, fileInfo.Length);
    }

    private static string ResolveUserDirectory(string userDirectory)
    {
        if (string.IsNullOrWhiteSpace(userDirectory))
        {
            throw new ArgumentException("VS Code user directory is required.", nameof(userDirectory));
        }

        return Path.GetFullPath(userDirectory);
    }

    private static string ResolveBackupPath(string userDirectory, string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            throw new ArgumentException("Backup path is required.", nameof(backupPath));
        }

        var fullBackupPath = Path.GetFullPath(backupPath);
        var fullUserDirectory = Path.GetFullPath(userDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullBackupPath.StartsWith(fullUserDirectory, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullBackupPath))
        {
            throw new InvalidOperationException("The selected backup does not belong to the selected VS Code user directory.");
        }

        var targetPath = GetTargetPathFromBackup(fullBackupPath);
        if (!IsManagedFileName(Path.GetFileName(targetPath)))
        {
            throw new InvalidOperationException("Only VSCopilotSwitch backups for settings.json and chatLanguageModels.json can be restored.");
        }

        return fullBackupPath;
    }

    private static string GetTargetPathFromBackup(string backupPath)
    {
        const string marker = ".vscopilotswitch.";
        var markerIndex = backupPath.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex <= 0 || !backupPath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected file is not a VSCopilotSwitch backup.");
        }

        return backupPath[..markerIndex];
    }

    private static bool IsManagedFileName(string fileName)
        => string.Equals(fileName, "settings.json", StringComparison.OrdinalIgnoreCase)
           || string.Equals(fileName, "chatLanguageModels.json", StringComparison.OrdinalIgnoreCase);

    private static string CreateRestoreSafetyBackupPath(string filePath)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        return $"{filePath}.vscopilotswitch.restore-safety.{timestamp}.bak";
    }

    private static string ToJson(JsonObject root) => JsonSerializer.Serialize(root, WriteOptions) + Environment.NewLine;
}
