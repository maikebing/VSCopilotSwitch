namespace VSCopilotSwitch.VsCodeConfig.Models;

public sealed record VsCodeConfigApplyResult(
    string UserDirectory,
    bool DryRun,
    IReadOnlyList<VsCodeConfigFileChange> Changes);

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
