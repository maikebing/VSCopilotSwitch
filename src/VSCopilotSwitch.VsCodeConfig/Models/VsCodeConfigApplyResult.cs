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
    string AfterContent);
