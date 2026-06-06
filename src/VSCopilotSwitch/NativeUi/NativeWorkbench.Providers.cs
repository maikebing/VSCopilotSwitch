using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using VSCopilotSwitch.Core.Providers;
using VSCopilotSwitch.Services;

namespace VSCopilotSwitch;

internal sealed partial class NativeWorkbench
{
    private Element[] BuildProviderRows(IReadOnlyList<DashboardProviderConfigView> providers, bool includeActions)
    {
        if (providers.Count == 0)
        {
            return [BodyLabel("还没有供应商配置。")];
        }

        return providers
            .Select((provider, index) => ProviderRow(provider, includeActions, index, providers.Count))
            .Cast<Element>()
            .ToArray();
    }

    private Element ProviderRow(DashboardProviderConfigView provider, bool includeActions, int index, int count)
    {
        var title = provider.Active ? $"{provider.Name}  [当前]" : provider.Name;
        var subtitle = $"{provider.Vendor} / {Empty(provider.Model, "未设置模型")} / 密钥：{(provider.HasApiKey ? provider.ApiKeyPreview ?? "已保存" : "未保存")} / {provider.ApiUrl}";
        var text = new StackPanel()
            .Spacing(3)
            .Children(
                new Label().Text(title).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis),
                BodyLabel(subtitle).TextWrapping(TextWrapping.Wrap));

        if (!includeActions)
        {
            return new DockPanel().Padding(8, 6).Children(text);
        }

        return new DockPanel()
            .Padding(8, 6)
            .Children(
                text.DockLeft(),
                new StackPanel()
                    .Horizontal()
                    .Spacing(6)
                    .Right()
                    .CenterVertical()
                    .Children(
                        new Button().Content("编辑").Padding(12, 6).OnClick(() => EditProvider(provider)),
                        new Button().Content("测试连接").Padding(12, 6).OnClick(() => _ = TestProviderAsync(provider)),
                        new Button().Content("上移").Padding(12, 6).OnClick(() => _ = MoveProviderAsync(provider, -1)).IsEnabled(index > 0),
                        new Button().Content("下移").Padding(12, 6).OnClick(() => _ = MoveProviderAsync(provider, 1)).IsEnabled(index < count - 1),
                        new Button().Content("启用").Padding(12, 6).OnClick(() => _ = ActivateProviderAsync(provider)).IsEnabled(!provider.Active),
                        new Button()
                            .Content(string.Equals(_pendingDeleteProviderId, provider.Id, StringComparison.OrdinalIgnoreCase) ? "再次点击删除" : "删除")
                            .Padding(12, 6)
                            .OnClick(() => _ = DeleteProviderAsync(provider))).DockRight());
    }

    private static Element[] BuildModelRows(IReadOnlyList<DashboardModelInfo> models)
    {
        if (models.Count == 0)
        {
            return [BodyLabel("当前供应商未返回模型列表。")];
        }

        return models
            .Take(16)
            .Select(model => Row(model.Name, $"{model.Details?.Family ?? "provider"} / {model.Details?.ParentModel ?? model.Model}"))
            .Cast<Element>()
            .Append(BodyLabel(models.Count > 16 ? $"另有 {models.Count - 16} 个模型未显示。" : string.Empty))
            .Where(element => element is not Label label || !string.IsNullOrWhiteSpace(label.Text))
            .ToArray();
    }

    private void RenderProviderEditor()
    {
        ReplaceChildren(
            _providerEditor,
            Field("名称", _providerEditorState.Name, value => _providerEditorState = _providerEditorState with { Name = value }),
            Field("备注", _providerEditorState.Remark, value => _providerEditorState = _providerEditorState with { Remark = value }),
            Field("官网 URL", _providerEditorState.Url, value => _providerEditorState = _providerEditorState with { Url = value }),
            Field("API URL", _providerEditorState.ApiUrl, value => _providerEditorState = _providerEditorState with { ApiUrl = value }),
            Field("模型", _providerEditorState.Model, value => _providerEditorState = _providerEditorState with { Model = value }),
            Field("协议类型", _providerEditorState.Vendor, value => _providerEditorState = _providerEditorState with { Vendor = value }),
            SecretField("API Key", value => _providerEditorState = _providerEditorState with { ApiKey = value }),
            new StackPanel()
                .Horizontal()
                .Spacing(8)
                .Children(
                    new Button().Content(_providerEditorState.IsNew ? "新增供应商" : "保存供应商").Padding(16, 8).OnClick(() => _ = SaveProviderAsync()),
                    new Button().Content("测试表单").Padding(16, 8).OnClick(() => _ = TestProviderFormAsync()),
                    new Button().Content("清空表单").Padding(16, 8).OnClick(NewProvider)));
    }

    private static Element Field(string label, string value, Action<string> onChanged)
    {
        var textBox = new TextBox()
            .Text(value)
            .OnTextChanged(onChanged);

        return new StackPanel()
            .Spacing(4)
            .Children(
                BodyLabel(label),
                textBox);
    }

    private static Element SecretField(string label, Action<string> onChanged)
    {
        var password = new PasswordBox()
            .OnTextChanged(onChanged);

        return new StackPanel()
            .Spacing(4)
            .Children(
                BodyLabel(label),
                password,
                BodyLabel("留空保存时会沿用已有密钥；新密钥只发送给后端加密保存，不会显示在界面列表中。"));
    }

    private void EditProvider(DashboardProviderConfigView provider)
    {
        _providerEditorState = new ProviderEditorState(
            provider.Id,
            provider.Name,
            provider.Remark,
            provider.Url,
            provider.ApiUrl,
            provider.Model,
            provider.Vendor,
            ApiKey: string.Empty,
            provider.Active,
            IsNew: false);
        _pendingDeleteProviderId = null;
        RenderProviderEditor();
        if (_dashboard is not null)
        {
            ReplaceChildren(_providerRows, BuildProviderRows(_dashboard.Providers, includeActions: true));
        }

        SetStatus($"正在编辑 {provider.Name}");
    }

    private void NewProvider()
    {
        _providerEditorState = ProviderEditorState.CreateNew();
        _pendingDeleteProviderId = null;
        RenderProviderEditor();
        if (_dashboard is not null)
        {
            ReplaceChildren(_providerRows, BuildProviderRows(_dashboard.Providers, includeActions: true));
        }

        SetStatus("已清空供应商表单");
    }

    private async Task SaveProviderAsync()
    {
        try
        {
            SetStatus("保存供应商...");
            var providers = await _api.PostJsonAsync<SaveProviderConfigRequest, IReadOnlyList<DashboardProviderConfigView>>(
                "/internal/providers",
                _providerEditorState.ToSaveRequest(),
                MewUiJsonContext.Default.IReadOnlyListDashboardProviderConfigView);
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                _providerEditorState = ProviderEditorState.CreateNew();
                if (_dashboard is not null)
                {
                    ApplyDashboard(_dashboard with { Providers = providers });
                }

                SetStatus("供应商已保存");
            });
            _ = RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError("保存供应商失败", ex);
        }
    }

    private async Task TestProviderFormAsync()
    {
        await TestProviderAsync(_providerEditorState.ToTestRequest(), "表单连接测试");
    }

    private async Task TestProviderAsync(DashboardProviderConfigView provider)
    {
        await TestProviderAsync(
            new TestProviderConnectionRequest(provider.Id, provider.Name, provider.ApiUrl, provider.Model, provider.Vendor, null),
            $"{provider.Name} 连接测试");
    }

    private async Task TestProviderAsync(TestProviderConnectionRequest request, string title)
    {
        try
        {
            SetStatus("测试连接...");
            var result = await _api.PostJsonAsync<TestProviderConnectionRequest, ProviderConnectionTestResult>(
                "/internal/providers/test-connection",
                request,
                MewUiJsonContext.Default.ProviderConnectionTestResult);
            var detail = string.Join(
                Environment.NewLine,
                result.Steps.Select(step => $"{(step.Success ? "通过" : "失败")} {step.Label}: {step.Message}"));
            NativeMessageBox.Show(
                $"{(result.Success ? "连接测试通过。" : "连接测试失败。")}{Environment.NewLine}{detail}",
                title,
                NativeMessageBoxButtons.Ok,
                result.Success ? NativeMessageBoxIcon.Information : NativeMessageBoxIcon.Warning);
            SetStatus(result.Success ? "连接测试通过" : "连接测试失败");
        }
        catch (Exception ex)
        {
            ShowError("测试连接失败", ex);
        }
    }

    private async Task MoveProviderAsync(DashboardProviderConfigView provider, int delta)
    {
        if (_dashboard is null)
        {
            return;
        }

        var providers = _dashboard.Providers.ToList();
        var index = providers.FindIndex(item => string.Equals(item.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
        var targetIndex = index + delta;
        if (index < 0 || targetIndex < 0 || targetIndex >= providers.Count)
        {
            return;
        }

        providers.RemoveAt(index);
        providers.Insert(targetIndex, provider);

        try
        {
            SetStatus("调整供应商排序...");
            var sorted = await _api.PostJsonAsync<ReorderProvidersRequest, IReadOnlyList<DashboardProviderConfigView>>(
                "/internal/providers/reorder",
                new ReorderProvidersRequest(providers.Select(item => item.Id).ToArray()),
                MewUiJsonContext.Default.IReadOnlyListDashboardProviderConfigView);
            _pendingDeleteProviderId = null;
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                if (_dashboard is not null)
                {
                    ApplyDashboard(_dashboard with { Providers = sorted });
                }

                SetStatus("供应商排序已更新");
            });
        }
        catch (Exception ex)
        {
            ShowError("调整供应商排序失败", ex);
        }
    }

    private async Task ActivateProviderAsync(DashboardProviderConfigView provider)
    {
        try
        {
            SetStatus("切换供应商...");
            await _api.PostJsonAsync<object, IReadOnlyList<DashboardProviderConfigView>>(
                $"/internal/providers/{Uri.EscapeDataString(provider.Id)}/activate",
                new { },
                MewUiJsonContext.Default.IReadOnlyListDashboardProviderConfigView);
            await RefreshAsync();
            SetStatus($"已启用 {provider.Name}");
        }
        catch (Exception ex)
        {
            ShowError("启用供应商失败", ex);
        }
    }

    private async Task DeleteProviderAsync(DashboardProviderConfigView provider)
    {
        if (!string.Equals(_pendingDeleteProviderId, provider.Id, StringComparison.OrdinalIgnoreCase))
        {
            _pendingDeleteProviderId = provider.Id;
            if (_dashboard is not null)
            {
                ReplaceChildren(_providerRows, BuildProviderRows(_dashboard.Providers, includeActions: true));
            }

            SetStatus($"再次点击删除以确认移除 {provider.Name}");
            return;
        }

        try
        {
            SetStatus("删除供应商...");
            await _api.DeleteJsonAsync<IReadOnlyList<DashboardProviderConfigView>>(
                $"/internal/providers/{Uri.EscapeDataString(provider.Id)}",
                MewUiJsonContext.Default.IReadOnlyListDashboardProviderConfigView);
            _pendingDeleteProviderId = null;
            await RefreshAsync();
            SetStatus("供应商已删除");
        }
        catch (Exception ex)
        {
            ShowError("删除供应商失败", ex);
        }
    }
}
