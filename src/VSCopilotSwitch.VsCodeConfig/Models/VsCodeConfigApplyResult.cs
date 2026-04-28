namespace VSCopilotSwitch.VsCodeConfig.Models;

public sealed record VsCodeConfigApplyResult(
    string UserDirectory,
    bool DryRun,
    IReadOnlyList<VsCodeConfigFileChange> Changes);

public sealed record VsCodeOllamaConfigStatus(
    string UserDirectory,
    bool Enabled,
    bool SettingsManaged,
    bool ChatLanguageModelsManaged,
    string Message);

public sealed record VsCodeConfigFileChange(
    string FilePath,
    bool ExistedBefore,
    bool Changed,
    string? BackupPath,
    string BeforeContent,
    string AfterContent,
    IReadOnlyList<VsCodeConfigFieldChange> FieldChanges);

public sealed record VsCodeConfigFieldChange(
    string Path,
    string BeforeValue,
    string AfterValue,
    bool Changed);

public sealed record VsCodeConfigBackup(
    string FilePath,
    string BackupPath,
    string FileName,
    DateTimeOffset CreatedAt,
    long SizeBytes);

public sealed record VsCodeConfigRestoreResult(
    string UserDirectory,
    string FilePath,
    string BackupPath,
    string? SafetyBackupPath,
    bool Restored);
