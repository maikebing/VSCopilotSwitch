using VSCopilotSwitch.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("SaveAsync does not return API key", SaveAsync_DoesNotReturnApiKey),
    ("ExportAsync excludes API keys by default", ExportAsync_ExcludesApiKeysByDefault),
    ("ActivateAsync keeps one active provider", ActivateAsync_KeepsOneActiveProvider),
    ("ReorderAsync is idempotent", ReorderAsync_IsIdempotent),
    ("DeleteAsync auto-selects available provider", DeleteAsync_AutoSelectsAvailableProvider)
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

static async Task SaveAsync_DoesNotReturnApiKey()
{
    using var workspace = TestWorkspace.Create();
    var service = workspace.CreateService();
    const string secret = "sk-provider-secret-1234";

    var views = await service.SaveAsync(CreateSaveRequest("alpha", "Alpha", apiKey: secret, active: true));
    var saved = views.Single(provider => provider.Id == "alpha");
    var publicPayload = string.Join(
        "\n",
        views.Select(provider => $"{provider.Id}|{provider.Name}|{provider.ApiKeyPreview}|{provider.HasApiKey}"));

    Assert.True(saved.HasApiKey, "保存后视图应标记存在 API Key。");
    Assert.Equal("sk-...1234", saved.ApiKeyPreview ?? string.Empty, "视图只能返回脱敏后的 API Key。");
    Assert.DoesNotContain(secret, publicPayload, "保存 API 不应回传密钥原文。");
    Assert.DoesNotContain(secret, File.ReadAllText(workspace.ConfigPath), "配置文件不应保存密钥原文。");
}

static async Task ActivateAsync_KeepsOneActiveProvider()
{
    using var workspace = TestWorkspace.Create();
    var service = workspace.CreateService();

    await service.SaveAsync(CreateSaveRequest("alpha", "Alpha", active: true));
    await service.SaveAsync(CreateSaveRequest("beta", "Beta", active: false));

    var views = await service.ActivateAsync("beta");

    Assert.Equal(1, views.Count(provider => provider.Active), "任意时刻只能有一个启用供应商。");
    Assert.True(views.Single(provider => provider.Id == "beta").Active, "目标供应商应被启用。");
    Assert.True(!views.Single(provider => provider.Id == "alpha").Active, "其他供应商应被关闭。");
}

static async Task ExportAsync_ExcludesApiKeysByDefault()
{
    using var workspace = TestWorkspace.Create();
    var service = workspace.CreateService();
    const string secret = "sk-export-secret-5678";

    await service.SaveAsync(CreateSaveRequest("alpha", "Alpha", apiKey: secret, active: true));

    var exported = await service.ExportAsync();
    var exportText = System.Text.Json.JsonSerializer.Serialize(exported);

    Assert.True(!exported.IncludesSecrets, "默认导出必须标记为不包含密钥。");
    Assert.True(exported.Providers.Single(provider => provider.Id == "alpha").HasApiKey, "导出可以保留密钥存在状态。");
    Assert.DoesNotContain(secret, exportText, "默认导出不应包含 API Key 原文。");
    Assert.DoesNotContain("ApiKeyPreview", exportText, "默认导出不应包含脱敏密钥预览字段。");
    Assert.DoesNotContain("EncryptedApiKey", exportText, "默认导出不应包含加密密文字段。");
    Assert.DoesNotContain("sk-...", exportText, "默认导出不应包含脱敏密钥预览。");
}

static async Task ReorderAsync_IsIdempotent()
{
    using var workspace = TestWorkspace.Create();
    var service = workspace.CreateService();
    var order = new[] { "beta", "gamma", "alpha" };

    await service.SaveAsync(CreateSaveRequest("alpha", "Alpha", active: true));
    await service.SaveAsync(CreateSaveRequest("beta", "Beta", active: false));
    await service.SaveAsync(CreateSaveRequest("gamma", "Gamma", active: false));

    var first = await service.ReorderAsync(new ReorderProvidersRequest(order));
    var second = await service.ReorderAsync(new ReorderProvidersRequest(order));

    Assert.Equal(string.Join(",", order), string.Join(",", first.Select(provider => provider.Id)), "第一次排序结果不正确。");
    Assert.Equal(string.Join(",", first.Select(ToOrderSnapshot)), string.Join(",", second.Select(ToOrderSnapshot)), "重复排序不应产生配置漂移。");
    Assert.Equal("0,1,2", string.Join(",", second.Select(provider => provider.SortOrder)), "排序后 SortOrder 应连续归一。");
}

static async Task DeleteAsync_AutoSelectsAvailableProvider()
{
    using var workspace = TestWorkspace.Create();
    var service = workspace.CreateService();

    await service.SaveAsync(CreateSaveRequest("alpha", "Alpha", active: true));
    await service.SaveAsync(CreateSaveRequest("beta", "Beta", active: false));

    var views = await service.DeleteAsync("alpha");

    Assert.True(views.All(provider => provider.Id != "alpha"), "删除后不应再返回目标供应商。");
    Assert.Equal(1, views.Count(provider => provider.Active), "删除启用供应商后应自动选择一个可用供应商。");
    Assert.True(views.Single(provider => provider.Id == "beta").Active, "剩余供应商应自动启用。");
}

static SaveProviderConfigRequest CreateSaveRequest(
    string id,
    string name,
    string model = "gpt-5.5",
    string apiKey = "sk-test-0000",
    bool active = false)
    => new(
        id,
        name,
        string.Empty,
        $"https://example.com/{id}",
        $"https://api.example.com/{id}",
        model,
        "openai-compatible",
        apiKey,
        active);

static string ToOrderSnapshot(ProviderConfigView provider)
    => $"{provider.Id}:{provider.SortOrder}:{provider.Active}";

internal sealed class TestWorkspace : IDisposable
{
    private TestWorkspace(string root)
    {
        Root = root;
        ConfigPath = Path.Combine(root, "providers.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(ConfigPath, """
            {
              "Version": 1,
              "Providers": []
            }
            """);
    }

    public string Root { get; }

    public string ConfigPath { get; }

    public static TestWorkspace Create()
        => new(Path.Combine(Path.GetTempPath(), "VSCopilotSwitch.Services.Tests", Guid.NewGuid().ToString("N")));

    public ProviderConfigService CreateService()
        => new(ConfigPath);

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
            throw new InvalidOperationException($"{message} 预期：{expected}，实际：{actual}");
        }
    }

    public static void Equal(int expected, int actual, string message)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"{message} 预期：{expected}，实际：{actual}");
        }
    }

    public static void DoesNotContain(string unexpectedSubstring, string actual, string message)
    {
        if (actual.Contains(unexpectedSubstring, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}
