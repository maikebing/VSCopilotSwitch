using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using VSCopilotSwitch.VsCodeConfig.Models;

namespace VSCopilotSwitch;

internal sealed partial class NativeWorkbench
{
    private Element[] BuildDirectoryRows(IReadOnlyList<DashboardVsCodeUserDirectory> directories, bool selectable)
    {
        if (directories.Count == 0)
        {
            return [BodyLabel("没有发现 VS Code User 配置目录。")];
        }

        return directories
            .Select(directory =>
            {
                var title = selectable && string.Equals(directory.Path, _selectedVsCodeDirectory, StringComparison.OrdinalIgnoreCase)
                    ? $"{directory.Description}  [已选择]"
                    : directory.Description;
                var subtitle = $"{(directory.Exists ? "存在" : "可创建")} / {directory.Path}";
                if (!selectable)
                {
                    return Row(title, subtitle);
                }

                return new DockPanel()
                    .Padding(8, 6)
                    .Children(
                        new StackPanel()
                            .Spacing(3)
                            .Children(
                                new Label().Text(title).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis),
                                BodyLabel(subtitle).TextWrapping(TextWrapping.Wrap))
                            .DockLeft(),
                        new Button()
                            .Content("选择")
                            .Padding(14, 6)
                            .OnClick(() => SelectVsCodeDirectory(directory.Path))
                            .DockRight());
            })
            .Cast<Element>()
            .ToArray();
    }

    private void RenderVsCodeDirectories()
    {
        ReplaceChildren(
            _vscodeDirectories,
            BuildDirectoryRows(_directories, selectable: true)
                .Append(new StackPanel()
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Button().Content("生成 dry-run 预览").Padding(16, 8).OnClick(() => _ = PreviewVsCodeApplyAsync()),
                        new Button().Content("检查状态").Padding(16, 8).OnClick(() => _ = CheckVsCodeStatusAsync()),
                        new Button().Content("刷新备份").Padding(16, 8).OnClick(() => _ = RefreshBackupsAsync())))
                .ToArray());
    }

    private void RenderVsCodePreview(VsCodeConfigApplyResult? result)
    {
        if (result is null)
        {
            ReplaceChildren(
                _vscodePreview,
                BodyLabel("先选择 VS Code User 目录，再生成 dry-run 差异预览。"),
                BodyLabel("写入只会维护 chatLanguageModels.json 中 name=vscs、vendor=ollama 的 Provider 条目。"));
            return;
        }

        var changes = result.Changes.SelectMany(change =>
            change.FieldChanges.Select(field => Row(
                $"{Path.GetFileName(change.FilePath)} / {field.Path}",
                $"{(field.Changed ? "将变更" : "无变化")}：{ShortValue(field.BeforeValue)} -> {ShortValue(field.AfterValue)}")))
            .Cast<Element>()
            .ToList();

        changes.Insert(0, Row("目标目录", result.UserDirectory));
        changes.Add(new StackPanel()
            .Horizontal()
            .Spacing(8)
            .Children(
                new Button().Content("确认写入 VS Code Ollama 配置").Padding(16, 8).OnClick(() => _ = ApplyVsCodeConfigAsync()).IsEnabled(result.Changes.Any(change => change.Changed)),
                new Button().Content("dry-run 撤销预览").Padding(16, 8).OnClick(() => _ = PreviewVsCodeRemoveAsync()),
                new Button().Content("确认撤销 vscs Provider").Padding(16, 8).OnClick(() => _ = RemoveVsCodeConfigAsync())));

        ReplaceChildren(_vscodePreview, changes.ToArray());
    }

    private async Task RefreshBackupsAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedVsCodeDirectory))
        {
            ReplaceChildren(_vscodeBackups, BodyLabel("尚未选择 VS Code User 目录。"));
            return;
        }

        try
        {
            var backups = await _api.PostJsonAsync<ListVsCodeConfigBackupsRequest, IReadOnlyList<VsCodeConfigBackup>>(
                "/internal/vscode/backups",
                new ListVsCodeConfigBackupsRequest(_selectedVsCodeDirectory),
                MewUiJsonContext.Default.IReadOnlyListVsCodeConfigBackup);

            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                if (backups.Count == 0)
                {
                    ReplaceChildren(_vscodeBackups, BodyLabel("当前目录还没有 VSCopilotSwitch 备份。"));
                    return;
                }

                ReplaceChildren(
                    _vscodeBackups,
                    backups
                        .Take(10)
                        .Select(backup => BackupRow(backup))
                        .Cast<Element>()
                        .ToArray());
            });
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher?.BeginInvoke(() =>
                ReplaceChildren(_vscodeBackups, ErrorLabel($"读取备份失败：{ex.Message}")));
        }
    }

    private Element BackupRow(VsCodeConfigBackup backup)
        => new DockPanel()
            .Padding(8, 6)
            .Children(
                new StackPanel()
                    .Spacing(3)
                    .Children(
                        new Label().Text($"{backup.FileName}  {backup.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}").SemiBold(),
                        BodyLabel($"{backup.SizeBytes} bytes / {backup.BackupPath}").TextWrapping(TextWrapping.Wrap))
                    .DockLeft(),
                new Button()
                    .Content(string.Equals(_pendingRestoreBackupPath, backup.BackupPath, StringComparison.OrdinalIgnoreCase)
                        ? "再次点击恢复"
                        : "恢复此备份")
                    .Padding(14, 6)
                    .OnClick(() => _ = RestoreBackupAsync(backup))
                    .DockRight());

    private void SelectVsCodeDirectory(string path)
    {
        _selectedVsCodeDirectory = path;
        _lastVsCodePreview = null;
        _lastVsCodePreviewKind = VsCodePreviewKind.None;
        RenderVsCodeDirectories();
        RenderVsCodePreview(null);
        _ = RefreshBackupsAsync();
        SetStatus("已选择 VS Code 目录");
    }

    private async Task PreviewVsCodeApplyAsync()
    {
        if (!EnsureVsCodeDirectorySelected())
        {
            return;
        }

        try
        {
            SetStatus("生成 VS Code dry-run...");
            var result = await _api.PostJsonAsync<ApplyVsCodeOllamaConfigRequest, VsCodeConfigApplyResult>(
                "/internal/vscode/apply-ollama",
                new ApplyVsCodeOllamaConfigRequest(_selectedVsCodeDirectory!, null, DryRun: true),
                MewUiJsonContext.Default.VsCodeConfigApplyResult);
            _lastVsCodePreview = result;
            _lastVsCodePreviewKind = VsCodePreviewKind.Apply;
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                RenderVsCodePreview(result);
                SetStatus("dry-run 预览已生成");
            });
        }
        catch (Exception ex)
        {
            ShowError("生成 VS Code 预览失败", ex);
        }
    }

    private async Task ApplyVsCodeConfigAsync()
    {
        if (!EnsureVsCodeDirectorySelected())
        {
            return;
        }

        if (_lastVsCodePreview is null || _lastVsCodePreviewKind != VsCodePreviewKind.Apply)
        {
            NativeMessageBox.Show("请先生成写入 dry-run 差异预览，再确认写入。", "需要预览", NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Warning);
            return;
        }

        try
        {
            SetStatus("写入 VS Code 配置...");
            var result = await _api.PostJsonAsync<ApplyVsCodeOllamaConfigRequest, VsCodeConfigApplyResult>(
                "/internal/vscode/apply-ollama",
                new ApplyVsCodeOllamaConfigRequest(_selectedVsCodeDirectory!, null, DryRun: false),
                MewUiJsonContext.Default.VsCodeConfigApplyResult);
            _lastVsCodePreview = result;
            _lastVsCodePreviewKind = VsCodePreviewKind.Apply;
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                RenderVsCodePreview(result);
                SetStatus("VS Code 配置已写入");
            });
            await RefreshBackupsAsync();
        }
        catch (Exception ex)
        {
            ShowError("写入 VS Code 配置失败", ex);
        }
    }

    private async Task PreviewVsCodeRemoveAsync()
    {
        if (!EnsureVsCodeDirectorySelected())
        {
            return;
        }

        try
        {
            SetStatus("生成撤销 dry-run...");
            var result = await _api.PostJsonAsync<RemoveVsCodeOllamaConfigRequest, VsCodeConfigApplyResult>(
                "/internal/vscode/remove-ollama",
                new RemoveVsCodeOllamaConfigRequest(_selectedVsCodeDirectory!, DryRun: true),
                MewUiJsonContext.Default.VsCodeConfigApplyResult);
            _lastVsCodePreview = result;
            _lastVsCodePreviewKind = VsCodePreviewKind.Remove;
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                RenderVsCodePreview(result);
                SetStatus("撤销预览已生成");
            });
        }
        catch (Exception ex)
        {
            ShowError("生成撤销预览失败", ex);
        }
    }

    private async Task RemoveVsCodeConfigAsync()
    {
        if (!EnsureVsCodeDirectorySelected())
        {
            return;
        }

        if (_lastVsCodePreview is null || _lastVsCodePreviewKind != VsCodePreviewKind.Remove)
        {
            NativeMessageBox.Show("请先生成撤销 dry-run 差异预览，再确认撤销。", "需要预览", NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Warning);
            return;
        }

        try
        {
            SetStatus("撤销 VS Code 配置...");
            var result = await _api.PostJsonAsync<RemoveVsCodeOllamaConfigRequest, VsCodeConfigApplyResult>(
                "/internal/vscode/remove-ollama",
                new RemoveVsCodeOllamaConfigRequest(_selectedVsCodeDirectory!, DryRun: false),
                MewUiJsonContext.Default.VsCodeConfigApplyResult);
            _lastVsCodePreview = result;
            _lastVsCodePreviewKind = VsCodePreviewKind.Remove;
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                RenderVsCodePreview(result);
                SetStatus("VS Code vscs Provider 已撤销");
            });
            await RefreshBackupsAsync();
        }
        catch (Exception ex)
        {
            ShowError("撤销 VS Code 配置失败", ex);
        }
    }

    private async Task CheckVsCodeStatusAsync()
    {
        if (!EnsureVsCodeDirectorySelected())
        {
            return;
        }

        try
        {
            var status = await _api.PostJsonAsync<VsCodeUserDirectoryRequest, VsCodeOllamaConfigStatus>(
                "/internal/vscode/ollama-status",
                new VsCodeUserDirectoryRequest(_selectedVsCodeDirectory!),
                MewUiJsonContext.Default.VsCodeOllamaConfigStatus);
            NativeMessageBox.Show(status.Message, "VS Code 配置状态", NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Information);
            SetStatus(status.Enabled ? "VS Code 已配置" : "VS Code 未配置");
        }
        catch (Exception ex)
        {
            ShowError("检查 VS Code 配置失败", ex);
        }
    }

    private async Task RestoreBackupAsync(VsCodeConfigBackup backup)
    {
        if (!EnsureVsCodeDirectorySelected())
        {
            return;
        }

        if (!string.Equals(_pendingRestoreBackupPath, backup.BackupPath, StringComparison.OrdinalIgnoreCase))
        {
            _pendingRestoreBackupPath = backup.BackupPath;
            await RefreshBackupsAsync();
            SetStatus($"再次点击恢复以确认还原 {backup.FileName}");
            return;
        }

        try
        {
            SetStatus("恢复备份...");
            var result = await _api.PostJsonAsync<RestoreVsCodeConfigBackupRequest, VsCodeConfigRestoreResult>(
                "/internal/vscode/restore-backup",
                new RestoreVsCodeConfigBackupRequest(_selectedVsCodeDirectory!, backup.BackupPath),
                MewUiJsonContext.Default.VsCodeConfigRestoreResult);
            _pendingRestoreBackupPath = null;
            NativeMessageBox.Show(
                $"已恢复：{result.FilePath}{Environment.NewLine}安全备份：{result.SafetyBackupPath ?? "当前文件原本不存在"}",
                "恢复完成",
                NativeMessageBoxButtons.Ok,
                NativeMessageBoxIcon.Information);
            await RefreshBackupsAsync();
            SetStatus("备份已恢复");
        }
        catch (Exception ex)
        {
            ShowError("恢复备份失败", ex);
        }
    }

    private bool EnsureVsCodeDirectorySelected()
    {
        if (!string.IsNullOrWhiteSpace(_selectedVsCodeDirectory))
        {
            return true;
        }

        NativeMessageBox.Show("请先选择 VS Code User 配置目录。", "缺少目标目录", NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Warning);
        return false;
    }
}
