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
        return CreateChange(filePath, before, after, dryRun);
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
            ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["models"] = new JsonArray(config.Models.Select(model => new JsonObject
            {
                ["id"] = model.Id,
                ["displayName"] = model.DisplayName,
                ["providerModelId"] = model.ProviderModelId
            }).ToArray<JsonNode?>())
        };

        root["vscopilotswitch"] = managed;
        var after = ToJson(root);
        return CreateChange(filePath, before, after, dryRun);
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

    private static VsCodeConfigFileChange CreateChange(string filePath, string before, string after, bool dryRun)
    {
        var existedBefore = File.Exists(filePath);
        var changed = !JsonEquivalent(before, after);
        var backupPath = changed && existedBefore && !dryRun ? CreateBackupPath(filePath) : null;
        return new VsCodeConfigFileChange(filePath, existedBefore, changed, backupPath, before, after);
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

    private static string ToJson(JsonObject root) => JsonSerializer.Serialize(root, WriteOptions) + Environment.NewLine;
}
