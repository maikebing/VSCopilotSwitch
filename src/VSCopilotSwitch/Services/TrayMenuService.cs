namespace VSCopilotSwitch.Services;

public interface ITrayMenuService
{
    string GetToolTip();

    IReadOnlyList<TrayMenuItem> GetMenuItems();

    ValueTask HandleCommandAsync(string commandId, CancellationToken cancellationToken);
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

public sealed class TrayMenuService : ITrayMenuService
{
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
            new(string.Empty, "快速切换真实供应商", Enabled: false)
        };

        var realProviderCount = 0;
        foreach (var provider in providers)
        {
            var realProvider = IsRealProvider(provider);
            if (realProvider)
            {
                realProviderCount++;
            }

            var status = realProvider ? string.Empty : "（缺少密钥或模型）";
            items.Add(new TrayMenuItem(
                $"{ActivateProviderPrefix}{provider.Id}",
                $"{provider.Name} · {DisplayModel(provider)}{status}",
                Enabled: realProvider && !provider.Active,
                Checked: provider.Active));
        }

        if (realProviderCount == 0)
        {
            items.Add(new TrayMenuItem(string.Empty, "没有可切换的真实供应商", Enabled: false));
        }

        return items;
    }

    public async ValueTask HandleCommandAsync(string commandId, CancellationToken cancellationToken)
    {
        if (!commandId.StartsWith(ActivateProviderPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var providerId = commandId[ActivateProviderPrefix.Length..];
        var providers = await _providerConfigService.ListAsync(cancellationToken);
        var target = providers.FirstOrDefault(provider => string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase));
        if (target is null || !IsRealProvider(target))
        {
            return;
        }

        await _providerConfigService.ActivateAsync(providerId, cancellationToken);
    }

    private IReadOnlyList<ProviderConfigView> ListProviders()
        => Task.Run(() => _providerConfigService.ListAsync(CancellationToken.None))
            .GetAwaiter()
            .GetResult();

    private static bool IsRealProvider(ProviderConfigView provider)
        => provider.HasApiKey
           && !string.IsNullOrWhiteSpace(provider.ApiUrl)
           && !string.IsNullOrWhiteSpace(provider.Model);

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
