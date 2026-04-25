using VSCopilotSwitch.VsCodeConfig.Models;
using VSCopilotSwitch.VsCodeConfig.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("ApplyOllamaConfigAsync is idempotent", ApplyOllamaConfigAsync_IsIdempotent),
    ("ListBackups returns recent backups", ListBackups_ReturnsRecentBackups),
    ("RestoreBackupAsync creates safety backup", RestoreBackupAsync_CreatesSafetyBackup)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static async Task ApplyOllamaConfigAsync_IsIdempotent()
{
    using var workspace = TestWorkspace.Create();
    var service = new VsCodeConfigService();

    var first = await service.ApplyOllamaConfigAsync(workspace.UserDirectory, ManagedOllamaConfig.Default, dryRun: false);
    var second = await service.ApplyOllamaConfigAsync(workspace.UserDirectory, ManagedOllamaConfig.Default, dryRun: false);

    Assert.True(first.Changes.Any(change => change.Changed), "第一次写入应产生变更。");
    Assert.True(second.Changes.All(change => !change.Changed), "重复写入不应产生配置漂移。");
}

static async Task ListBackups_ReturnsRecentBackups()
{
    using var workspace = TestWorkspace.Create();
    var service = new VsCodeConfigService();

    File.WriteAllText(Path.Combine(workspace.UserDirectory, "settings.json"), "{\"editor.fontSize\": 14}");
    await service.ApplyOllamaConfigAsync(workspace.UserDirectory, ManagedOllamaConfig.Default, dryRun: false);

    var backups = service.ListBackups(workspace.UserDirectory);

    Assert.True(backups.Count >= 1, "写入已有文件前应创建备份。");
    Assert.True(backups.Any(backup => backup.FileName == "settings.json"), "备份列表应包含 settings.json。");
}

static async Task RestoreBackupAsync_CreatesSafetyBackup()
{
    using var workspace = TestWorkspace.Create();
    var service = new VsCodeConfigService();
    var settingsPath = Path.Combine(workspace.UserDirectory, "settings.json");
    const string original = "{\"editor.fontSize\": 14}";

    File.WriteAllText(settingsPath, original);
    await service.ApplyOllamaConfigAsync(workspace.UserDirectory, ManagedOllamaConfig.Default, dryRun: false);
    var backup = service.ListBackups(workspace.UserDirectory).First(item => item.FileName == "settings.json");

    var result = await service.RestoreBackupAsync(workspace.UserDirectory, backup.BackupPath);

    Assert.True(result.Restored, "恢复结果应标记成功。");
    Assert.True(File.Exists(result.SafetyBackupPath), "恢复前应为当前文件创建安全备份。");
    Assert.Equal(original, File.ReadAllText(settingsPath), "恢复后文件内容应等于原始备份内容。");
}

internal sealed class TestWorkspace : IDisposable
{
    private TestWorkspace(string root)
    {
        Root = root;
        UserDirectory = Path.Combine(root, "User");
        Directory.CreateDirectory(UserDirectory);
    }

    public string Root { get; }

    public string UserDirectory { get; }

    public static TestWorkspace Create()
        => new(Path.Combine(Path.GetTempPath(), "VSCopilotSwitch.Tests", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Equal(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}
