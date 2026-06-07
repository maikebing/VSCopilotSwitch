namespace VSCopilotSwitch.Services;

public interface ITrayMenuService
{
    string GetToolTip();

    IReadOnlyList<TrayMenuItem> GetMenuItems();

    ValueTask<TrayCommandResult> HandleCommandAsync(string commandId, CancellationToken cancellationToken);
}

public sealed record TrayMenuItem(
    string CommandId,
    string Text,
    bool Enabled = true,
    bool Checked = false,
    bool IsSeparator = false)
{
    public static TrayMenuItem CreateSeparator()
        => new(string.Empty, string.Empty, Enabled: false, IsSeparator: true);
}

public sealed record TrayCommandResult(
    bool Handled,
    string Message,
    string? ActiveProviderId = null,
    bool RefreshDashboard = false,
    bool OpenProviders = false)
{
    public static TrayCommandResult Ignored { get; } = new(false, string.Empty);
}

public sealed class TrayMenuService : ITrayMenuService
{
    public const string RefreshDashboardCommand = "refresh-dashboard";
    public const string OpenProvidersCommand = "open-providers";
    private const string ActivateProviderPrefix = "activate-provider:";
    private readonly IProviderConfigService _providerConfigService;

    public TrayMenuService(IProviderConfigService providerConfigService)
    {
        _providerConfigService = providerConfigService;
    }

    public string GetToolTip()
    {
        var active = ListProviders().FirstOrDefault(provider => provider.Active);
        if (active is null)
        {
            return "VSCopilotSwitch - 未启用供应商";
        }

        return $"VSCopilotSwitch - {active.Name} / {DisplayModel(active)}";
    }

    public IReadOnlyList<TrayMenuItem> GetMenuItems()
    {
        var providers = ListProviders();
        var active = providers.FirstOrDefault(provider => provider.Active);
        var items = new List<TrayMenuItem>
        {
            new(string.Empty, $"当前供应商：{DisplayProvider(active)}", Enabled: false),
            new(string.Empty, $"当前模型：{DisplayModel(active)}", Enabled: false),
            new(string.Empty, "代理服务：运行中", Enabled: false),
            TrayMenuItem.CreateSeparator(),
            new(RefreshDashboardCommand, "刷新状态"),
            new(OpenProvidersCommand, "打开供应商管理"),
            TrayMenuItem.CreateSeparator(),
            new(string.Empty, "快速切换", Enabled: false)
        };

        var switchableCount = 0;
        foreach (var provider in providers)
        {
            var readiness = GetReadinessMessage(provider);
            var switchable = readiness.Length == 0;
            if (switchable)
            {
                switchableCount++;
            }

            var status = switchable ? string.Empty : $"（{readiness}）";
            items.Add(new TrayMenuItem(
                $"{ActivateProviderPrefix}{provider.Id}",
                $"{provider.Name} · {DisplayModel(provider)}{status}",
                Enabled: switchable && !provider.Active,
                Checked: provider.Active));
        }

        if (switchableCount == 0)
        {
            items.Add(new TrayMenuItem(string.Empty, "没有可切换的真实供应商", Enabled: false));
        }

        return items;
    }

    public async ValueTask<TrayCommandResult> HandleCommandAsync(string commandId, CancellationToken cancellationToken)
    {
        if (string.Equals(commandId, RefreshDashboardCommand, StringComparison.Ordinal))
        {
            return new TrayCommandResult(true, "已刷新状态", RefreshDashboard: true);
        }

        if (string.Equals(commandId, OpenProvidersCommand, StringComparison.Ordinal))
        {
            return new TrayCommandResult(true, "已打开供应商管理", OpenProviders: true);
        }

        if (!commandId.StartsWith(ActivateProviderPrefix, StringComparison.Ordinal))
        {
            return TrayCommandResult.Ignored;
        }

        var providerId = commandId[ActivateProviderPrefix.Length..];
        var providers = await _providerConfigService.ListAsync(cancellationToken);
        var target = providers.FirstOrDefault(provider => string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return new TrayCommandResult(false, "供应商不存在。");
        }

        var readiness = GetReadinessMessage(target);
        if (readiness.Length > 0)
        {
            return new TrayCommandResult(false, $"{target.Name} 暂不能切换：{readiness}。", target.Id);
        }

        var updated = await _providerConfigService.ActivateAsync(providerId, cancellationToken);
        var active = updated.First(provider => string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase));
        return new TrayCommandResult(true, $"已切换到 {active.Name} / {DisplayModel(active)}。", active.Id, RefreshDashboard: true);
    }

    private IReadOnlyList<ProviderConfigView> ListProviders()
        => Task.Run(() => _providerConfigService.ListAsync(CancellationToken.None))
            .GetAwaiter()
            .GetResult();

    private static string GetReadinessMessage(ProviderConfigView provider)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(provider.ApiUrl))
        {
            missing.Add("API URL");
        }

        if (string.IsNullOrWhiteSpace(provider.Model))
        {
            missing.Add("模型");
        }

        if (!provider.HasApiKey)
        {
            missing.Add("密钥");
        }

        return missing.Count == 0 ? string.Empty : $"缺少{string.Join("、", missing)}";
    }

    private static bool IsRealProvider(ProviderConfigView provider)
        => GetReadinessMessage(provider).Length == 0;

    private static string DisplayProvider(ProviderConfigView? provider)
    {
        if (provider is null)
        {
            return "未启用";
        }

        return IsRealProvider(provider) ? provider.Name : $"{provider.Name}（未配置完整）";
    }

    private static string DisplayModel(ProviderConfigView? provider)
        => string.IsNullOrWhiteSpace(provider?.Model) ? "未设置" : provider.Model.Trim();
}
