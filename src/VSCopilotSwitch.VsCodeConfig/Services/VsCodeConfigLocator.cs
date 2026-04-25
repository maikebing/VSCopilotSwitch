using System.Runtime.InteropServices;
using VSCopilotSwitch.VsCodeConfig.Models;

namespace VSCopilotSwitch.VsCodeConfig.Services;

public interface IVsCodeConfigLocator
{
    IReadOnlyList<VsCodeUserDirectory> FindUserDirectories();
}

public sealed class VsCodeConfigLocator : IVsCodeConfigLocator
{
    public IReadOnlyList<VsCodeUserDirectory> FindUserDirectories()
    {
        var directories = new List<VsCodeUserDirectory>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            AddIfNotWhiteSpace(directories, appData, "Code", VsCodeProfile.Stable, "VS Code Stable user settings");
            AddIfNotWhiteSpace(directories, appData, "Code - Insiders", VsCodeProfile.Insiders, "VS Code Insiders user settings");
            AddIfNotWhiteSpace(directories, appData, "VSCodium", VsCodeProfile.Vscodium, "VSCodium user settings");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            AddIfNotWhiteSpace(directories, home, "Library/Application Support/Code", VsCodeProfile.Stable, "VS Code Stable user settings");
            AddIfNotWhiteSpace(directories, home, "Library/Application Support/Code - Insiders", VsCodeProfile.Insiders, "VS Code Insiders user settings");
            AddIfNotWhiteSpace(directories, home, "Library/Application Support/VSCodium", VsCodeProfile.Vscodium, "VSCodium user settings");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            AddIfNotWhiteSpace(directories, home, ".config/Code", VsCodeProfile.Stable, "VS Code Stable user settings");
            AddIfNotWhiteSpace(directories, home, ".config/Code - Insiders", VsCodeProfile.Insiders, "VS Code Insiders user settings");
            AddIfNotWhiteSpace(directories, home, ".config/VSCodium", VsCodeProfile.Vscodium, "VSCodium user settings");

            if (IsWsl())
            {
                AddIfNotWhiteSpace(directories, home, ".vscode-server/data/User", VsCodeProfile.WslLinuxUser, "VS Code Remote WSL server user settings");
            }
        }

        return directories;
    }

    private static void AddIfNotWhiteSpace(List<VsCodeUserDirectory> directories, string root, string relativePath, VsCodeProfile profile, string description)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        directories.Add(new VsCodeUserDirectory(path, profile, Directory.Exists(path), description));
    }

    private static bool IsWsl()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME")))
        {
            return true;
        }

        try
        {
            return File.Exists("/proc/version") && File.ReadAllText("/proc/version").Contains("microsoft", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
