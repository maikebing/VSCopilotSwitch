using VSCopilotSwitch.VsCodeConfig.Models;
using VSCopilotSwitch.VsCodeConfig.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("ApplyOllamaConfigAsync writes provider array entry idempotently", ApplyOllamaConfigAsync_WritesProviderArrayEntryIdempotently),
    ("GetOllamaConfigStatusAsync detects managed provider", GetOllamaConfigStatusAsync_DetectsManagedProvider),
    ("RemoveOllamaConfigAsync removes only managed provider", RemoveOllamaConfigAsync_RemovesOnlyManagedProvider),
    ("ApplyOllamaConfigAsync removes duplicate managed providers", ApplyOllamaConfigAsync_RemovesDuplicateManagedProviders),
    ("ApplyOllamaConfigAsync normalizes VS Code product root to User directory", ApplyOllamaConfigAsync_NormalizesProductRootToUserDirectory),
    ("ApplyOllamaConfigAsync reports invalid chatLanguageModels JSON", ApplyOllamaConfigAsync_ReportsInvalidChatLanguageModelsJson),
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

static async Task ApplyOllamaConfigAsync_WritesProviderArrayEntryIdempotently()
{
    using var workspace = TestWorkspace.Create();
    var service = new VsCodeConfigService();

    var first = await service.ApplyOllamaConfigAsync(workspace.UserDirectory, ManagedOllamaConfig.Default, dryRun: false);
    var second = await service.ApplyOllamaConfigAsync(workspace.UserDirectory, ManagedOllamaConfig.Default, dryRun: false);
    var content = File.ReadAllText(Path.Combine(workspace.UserDirectory, "chatLanguageModels.json"));

    Assert.True(first.Changes.Count == 1, "新规则只应写入 chatLanguageModels.json。");
    Assert.True(first.Changes.Any(change => change.Changed), "第一次写入应产生变更。");
    Assert.True(second.Changes.All(change => !change.Changed), "重复写入不应产生配置漂移。");
    Assert.Contains("\"name\": \"vscc\"", content, "应写入 VS Code 语言模型 Ollama Provider 名称。");
    Assert.Contains("\"vendor\": \"ollama\"", content, "应写入 Ollama Provider 类型。");
    Assert.Contains("\"url\": \"http://127.0.0.1:5124\"", content, "应写入当前本地代理地址。");
}

static async Task ListBackups_ReturnsRecentBackups()
{
    using var workspace = TestWorkspace.Create();
    var service = new VsCodeConfigService();
    var chatLanguageModelsPath = Path.Combine(workspace.UserDirectory, "chatLanguageModels.json");

    File.WriteAllText(chatLanguageModelsPath, "[{\"name\":\"OpenAI\",\"vendor\":\"openai\"}]");
    await service.ApplyOllamaConfigAsync(workspace.UserDirectory, ManagedOllamaConfig.Default, dryRun: false);

    var backups = service.ListBackups(workspace.UserDirectory);

    Assert.True(backups.Count >= 1, "写入已有文件前应创建备份。");
    Assert.True(backups.Any(backup => backup.FileName == "chatLanguageModels.json"), "备份列表应包含 chatLanguageModels.json。");
}

static async Task GetOllamaConfigStatusAsync_DetectsManagedProvider()
{
    using var workspace = TestWorkspace.Create();
    var service = new VsCodeConfigService();

    var before = await service.GetOllamaConfigStatusAsync(workspace.UserDirectory);
    await service.ApplyOllamaConfigAsync(workspace.UserDirectory, ManagedOllamaConfig.Default, dryRun: false);
    var after = await service.GetOllamaConfigStatusAsync(workspace.UserDirectory);

    Assert.True(!before.Enabled, "写入前不应检测到托管 Provider。");
    Assert.True(after.Enabled, "写入后应检测到托管 Provider。");
    Assert.True(!after.SettingsManaged, "新规则下 settings.json 不再参与状态判断。");
    Assert.True(after.ChatLanguageModelsManaged, "应通过 chatLanguageModels.json 检测托管 Provider。");
}

static async Task RemoveOllamaConfigAsync_RemovesOnlyManagedProvider()
{
    using var workspace = TestWorkspace.Create();
    var service = new VsCodeConfigService();
    var chatLanguageModelsPath = Path.Combine(workspace.UserDirectory, "chatLanguageModels.json");

    File.WriteAllText(chatLanguageModelsPath, "[{\"name\":\"OpenAI\",\"vendor\":\"openai\"},{\"name\":\"UserOllama\",\"vendor\":\"ollama\",\"url\":\"http://localhost:11434\"}]");
    await service.ApplyOllamaConfigAsync(workspace.UserDirectory, ManagedOllamaConfig.Default, dryRun: false);

    var result = await service.RemoveOllamaConfigAsync(workspace.UserDirectory, dryRun: false);
    var status = await service.GetOllamaConfigStatusAsync(workspace.UserDirectory);
    var content = File.ReadAllText(chatLanguageModelsPath);

    Assert.True(result.Changes.Any(change => change.Changed), "撤销托管 Provider 应产生变更。");
    Assert.True(!status.Enabled, "撤销后不应再检测到托管 Provider。");
    Assert.Contains("\"name\": \"OpenAI\"", content, "撤销不应删除用户已有 OpenAI Provider。");
    Assert.Contains("\"name\": \"UserOllama\"", content, "撤销不应删除用户手工添加的其他 Ollama Provider。");
    Assert.DoesNotContain("\"name\": \"vscc\"", content, "撤销应删除本项目管理的 vscc Provider。");
}

static async Task ApplyOllamaConfigAsync_RemovesDuplicateManagedProviders()
{
    using var workspace = TestWorkspace.Create();
    var service = new VsCodeConfigService();
    var chatLanguageModelsPath = Path.Combine(workspace.UserDirectory, "chatLanguageModels.json");

    File.WriteAllText(chatLanguageModelsPath, "[{\"name\":\"vscc\",\"vendor\":\"ollama\",\"url\":\"http://old-one\"},{\"name\":\"OpenAI\",\"vendor\":\"openai\"},{\"name\":\"vscc\",\"vendor\":\"ollama\",\"url\":\"http://old-two\"}]");

    await service.ApplyOllamaConfigAsync(workspace.UserDirectory, ManagedOllamaConfig.Default, dryRun: false);
    var content = File.ReadAllText(chatLanguageModelsPath);

    Assert.Equal(1, CountOccurrences(content, "\"name\": \"vscc\""), "重复写入应收敛为一个 vscc Provider。");
    Assert.Contains("\"url\": \"http://127.0.0.1:5124\"", content, "应更新为当前代理地址。");
    Assert.Contains("\"name\": \"OpenAI\"", content, "去重时应保留其他 Provider。");
}

static async Task ApplyOllamaConfigAsync_NormalizesProductRootToUserDirectory()
{
    using var workspace = TestWorkspace.Create();
    var service = new VsCodeConfigService();
    var codeRoot = Path.Combine(workspace.Root, "Code");
    var userDirectory = Path.Combine(codeRoot, "User");
    var rootChatLanguageModelsPath = Path.Combine(codeRoot, "chatLanguageModels.json");
    var userChatLanguageModelsPath = Path.Combine(userDirectory, "chatLanguageModels.json");

    Directory.CreateDirectory(userDirectory);
    File.WriteAllText(rootChatLanguageModelsPath, "{\"legacy\":true}");

    var result = await service.ApplyOllamaConfigAsync(codeRoot, ManagedOllamaConfig.Default, dryRun: true);

    Assert.Equal(userDirectory, result.UserDirectory, "误传 VS Code 产品根目录时应自动规范化到 User 子目录。");
    Assert.Equal(userChatLanguageModelsPath, result.Changes.Single().FilePath, "差异预览应读取并写入 User 子目录下的 chatLanguageModels.json。");
    Assert.Equal("{\"legacy\":true}", File.ReadAllText(rootChatLanguageModelsPath), "不应读取或修改上一级残留配置文件。");
}

static async Task ApplyOllamaConfigAsync_ReportsInvalidChatLanguageModelsJson()
{
    using var workspace = TestWorkspace.Create();
    var service = new VsCodeConfigService();
    var chatLanguageModelsPath = Path.Combine(workspace.UserDirectory, "chatLanguageModels.json");

    File.WriteAllText(chatLanguageModelsPath, "[{\"name\":\"OpenAI\",]");

    await Assert.ThrowsAsync<InvalidOperationException>(
        () => service.ApplyOllamaConfigAsync(workspace.UserDirectory, ManagedOllamaConfig.Default, dryRun: true),
        "VS Code chatLanguageModels.json 无法解析。");
}

static async Task RestoreBackupAsync_CreatesSafetyBackup()
{
    using var workspace = TestWorkspace.Create();
    var service = new VsCodeConfigService();
    var chatLanguageModelsPath = Path.Combine(workspace.UserDirectory, "chatLanguageModels.json");
    const string original = "[{\"name\":\"OpenAI\",\"vendor\":\"openai\"}]";

    File.WriteAllText(chatLanguageModelsPath, original);
    await service.ApplyOllamaConfigAsync(workspace.UserDirectory, ManagedOllamaConfig.Default, dryRun: false);
    var backup = service.ListBackups(workspace.UserDirectory).First(item => item.FileName == "chatLanguageModels.json");

    var result = await service.RestoreBackupAsync(workspace.UserDirectory, backup.BackupPath);

    Assert.True(result.Restored, "恢复结果应标记成功。");
    Assert.True(File.Exists(result.SafetyBackupPath), "恢复前应为当前文件创建安全备份。");
    Assert.Equal(original, File.ReadAllText(chatLanguageModelsPath), "恢复后文件内容应等于原始备份内容。");
}

static int CountOccurrences(string content, string value)
{
    var count = 0;
    var index = 0;
    while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
    {
        count++;
        index += value.Length;
    }

    return count;
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

    public static void Equal(int expected, int actual, string message)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Contains(string expectedSubstring, string actual, string message)
    {
        if (!actual.Contains(expectedSubstring, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void DoesNotContain(string unexpectedSubstring, string actual, string message)
    {
        if (actual.Contains(unexpectedSubstring, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    public static async Task ThrowsAsync<TException>(Func<Task> action, string expectedMessage)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException ex)
        {
            if (!ex.Message.Contains(expectedMessage, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"异常消息不符合预期。实际：{ex.Message}");
            }

            return;
        }

        throw new InvalidOperationException($"预期抛出 {typeof(TException).Name}，但操作成功完成。");
    }
}
