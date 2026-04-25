namespace VSCopilotSwitch.VsCodeConfig.Models;

public sealed record VsCodeUserDirectory(
    string Path,
    VsCodeProfile Profile,
    bool Exists,
    string Description);

public enum VsCodeProfile
{
    Stable,
    Insiders,
    Vscodium,
    WslLinuxUser,
    Custom
}
