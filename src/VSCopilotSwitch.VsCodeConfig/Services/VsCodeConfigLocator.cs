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

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return directories;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            AddIfNotWhiteSpace(directories, appData, "Code", VsCodeProfile.Stable, "VS Code Stable user settings");
            AddIfNotWhiteSpace(directories, appData, "Code - Insiders", VsCodeProfile.Insiders, "VS Code Insiders user settings");
            AddIfNotWhiteSpace(directories, appData, "VSCodium", VsCodeProfile.Vscodium, "VSCodium user settings");
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

}
