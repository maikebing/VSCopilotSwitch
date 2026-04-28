using System.Text.Json;
using System.Text.Json.Nodes;
using VSCopilotSwitch.VsCodeConfig.Models;

namespace VSCopilotSwitch.VsCodeConfig.Services;

public interface IVsCodeConfigService
{
    Task<VsCodeOllamaConfigStatus> GetOllamaConfigStatusAsync(
        string userDirectory,
        CancellationToken cancellationToken = default);

    Task<VsCodeConfigApplyResult> ApplyOllamaConfigAsync(
        string userDirectory,
        ManagedOllamaConfig config,
        bool dryRun,
        CancellationToken cancellationToken = default);

    Task<VsCodeConfigApplyResult> RemoveOllamaConfigAsync(
        string userDirectory,
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
    public const string ManagedProviderName = "vscc";
    public const string OllamaVendor = "ollama";

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
        var fullUserDirectory = ResolveUserDirectory(userDirectory);
        var changes = new List<VsCodeConfigFileChange>
        {
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

    public async Task<VsCodeOllamaConfigStatus> GetOllamaConfigStatusAsync(
        string userDirectory,
        CancellationToken cancellationToken = default)
    {
        var fullUserDirectory = ResolveUserDirectory(userDirectory);
        var chatLanguageModelsPath = Path.Combine(fullUserDirectory, "chatLanguageModels.json");
        var chatLanguageModelsManaged = File.Exists(chatLanguageModelsPath)
            && HasManagedChatLanguageModelsConfig(await File.ReadAllTextAsync(chatLanguageModelsPath, cancellationToken));

        var message = chatLanguageModelsManaged
            ? "已检测到 VSCopilotSwitch 管理的 VS Code Ollama Provider。"
            : "未检测到 VSCopilotSwitch 管理的 VS Code Ollama Provider。";

        return new VsCodeOllamaConfigStatus(
            fullUserDirectory,
            chatLanguageModelsManaged,
            false,
            chatLanguageModelsManaged,
            message);
    }

    public async Task<VsCodeConfigApplyResult> RemoveOllamaConfigAsync(
        string userDirectory,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var fullUserDirectory = ResolveUserDirectory(userDirectory);
        var changes = new List<VsCodeConfigFileChange>
        {
            await BuildChatLanguageModelsRemovalChangeAsync(fullUserDirectory, dryRun, cancellationToken)
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

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        if (safetyBackupPath is not null)
        {
            File.Copy(targetPath, safetyBackupPath, overwrite: false);
        }

        await using var source = File.OpenRead(fullBackupPath);
        await using var destination = File.Create(targetPath);
        await source.CopyToAsync(destination, cancellationToken);

        return new VsCodeConfigRestoreResult(fullUserDirectory, targetPath, fullBackupPath, safetyBackupPath, Restored: true);
    }

    private static async Task<VsCodeConfigFileChange> BuildChatLanguageModelsChangeAsync(
        string userDirectory,
        ManagedOllamaConfig config,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(userDirectory, "chatLanguageModels.json");
        var before = await ReadOrDefaultAsync(filePath, "[]", cancellationToken);
        var beforeArray = ParseArrayOrEmpty(before);
        var afterArray = UpsertManagedProvider(beforeArray, config.BaseUrl);
        var after = ToJson(afterArray);

        var fieldChanges = CreateProviderFieldChanges(before, after);
        return CreateChange(filePath, before, after, dryRun, fieldChanges);
    }

    private static async Task<VsCodeConfigFileChange> BuildChatLanguageModelsRemovalChangeAsync(
        string userDirectory,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(userDirectory, "chatLanguageModels.json");
        var before = await ReadOrDefaultAsync(filePath, "[]", cancellationToken);
        var beforeArray = ParseArrayOrEmpty(before);
        var afterArray = RemoveManagedProvider(beforeArray);
        var after = ToJson(afterArray);

        var fieldChanges = CreateProviderFieldChanges(before, after);
        return CreateChange(filePath, before, after, dryRun, fieldChanges);
    }

    private static JsonArray UpsertManagedProvider(JsonArray source, string baseUrl)
    {
        var result = new JsonArray();
        var inserted = false;

        foreach (var node in source)
        {
            if (IsManagedProvider(node))
            {
                if (!inserted)
                {
                    result.Add(CreateManagedProvider(baseUrl));
                    inserted = true;
                }

                continue;
            }

            result.Add(node?.DeepClone());
        }

        if (!inserted)
        {
            result.Add(CreateManagedProvider(baseUrl));
        }

        return result;
    }

    private static JsonArray RemoveManagedProvider(JsonArray source)
    {
        var result = new JsonArray();
        foreach (var node in source)
        {
            if (!IsManagedProvider(node))
            {
                result.Add(node?.DeepClone());
            }
        }

        return result;
    }

    private static JsonObject CreateManagedProvider(string baseUrl)
        => new()
        {
            ["name"] = ManagedProviderName,
            ["vendor"] = OllamaVendor,
            ["url"] = baseUrl
        };

    private static IReadOnlyList<VsCodeConfigFieldChange> CreateProviderFieldChanges(string before, string after)
    {
        var beforeProvider = FindManagedProvider(ParseArrayOrEmpty(before));
        var afterProvider = FindManagedProvider(ParseArrayOrEmpty(after));

        return new[]
        {
            CreateFieldChange(
                $"chatLanguageModels[{ManagedProviderName}]",
                SerializeProviderForDiff(beforeProvider),
                SerializeProviderForDiff(afterProvider)),
            CreateFieldChange(
                $"chatLanguageModels[{ManagedProviderName}].url",
                SerializeValueForDiff(beforeProvider?["url"]),
                SerializeValueForDiff(afterProvider?["url"]))
        };
    }

    private static VsCodeConfigFieldChange CreateFieldChange(string path, string beforeValue, string afterValue)
        => new(path, beforeValue, afterValue, beforeValue != afterValue);

    private static bool HasManagedChatLanguageModelsConfig(string content)
        => FindManagedProvider(ParseArrayOrEmpty(content)) is not null;

    private static JsonObject? FindManagedProvider(JsonArray root)
    {
        foreach (var node in root)
        {
            if (IsManagedProvider(node) && node is JsonObject provider)
            {
                return provider;
            }
        }

        return null;
    }

    private static bool IsManagedProvider(JsonNode? node)
    {
        if (node is not JsonObject provider)
        {
            return false;
        }

        return string.Equals(ReadString(provider["name"]), ManagedProviderName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(ReadString(provider["vendor"]), OllamaVendor, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(JsonNode? node)
    {
        try
        {
            return node?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> ReadOrDefaultAsync(string filePath, string defaultContent, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return defaultContent;
        }

        return await File.ReadAllTextAsync(filePath, cancellationToken);
    }

    private static JsonArray ParseArrayOrEmpty(string content)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return new JsonArray();
            }

            var node = JsonNode.Parse(content);
            if (node is JsonArray array)
            {
                return array;
            }

            if (node is JsonObject { Count: 0 })
            {
                return new JsonArray();
            }

            throw new InvalidOperationException("VS Code chatLanguageModels.json must be a JSON array. Please choose the VS Code User directory or fix the file before applying changes.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The VS Code chatLanguageModels.json file could not be parsed. Make a backup and fix invalid JSON before applying changes.", ex);
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

    private static bool JsonEquivalent(string left, string right)
    {
        try
        {
            var leftNode = JsonNode.Parse(string.IsNullOrWhiteSpace(left) ? "[]" : left);
            var rightNode = JsonNode.Parse(string.IsNullOrWhiteSpace(right) ? "[]" : right);
            return JsonSerializer.Serialize(leftNode) == JsonSerializer.Serialize(rightNode);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }
    }

    private static string SerializeProviderForDiff(JsonObject? provider)
        => provider is null ? "未设置" : JsonSerializer.Serialize(provider, WriteOptions);

    private static string SerializeValueForDiff(JsonNode? value)
        => value is null ? "未设置" : JsonSerializer.Serialize(value);

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

    private static string ToJson(JsonArray root) => JsonSerializer.Serialize(root, WriteOptions) + Environment.NewLine;
}
