using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace VSCopilotSwitch;

internal sealed partial class NativeWorkbench
{
    private static readonly ProviderPreset[] ProviderPresets =
    [
        new("OpenAI Official", "openai", "https://api.openai.com/v1", "gpt-4.1", "https://platform.openai.com", "文本、工具、视觉、长上下文", "官方 OpenAI API，适合直接接入 GPT 系列模型。"),
        new("Claude Official", "claude", "https://api.anthropic.com", "claude-sonnet-4-5", "https://console.anthropic.com", "文本、工具、视觉、长上下文", "Anthropic 官方 Messages API，适合 Claude Sonnet / Opus 系列。"),
        new("DeepSeek", "deepseek", "https://api.deepseek.com", "deepseek-chat", "https://platform.deepseek.com", "文本、推理模型可选", "DeepSeek 官方 API，reasoner 模型需按实际能力测试后启用。"),
        new("NVIDIA NIM", "nvidia-nim", "https://integrate.api.nvidia.com/v1", "meta/llama-3.1-405b-instruct", "https://build.nvidia.com", "文本、部分模型工具/视觉", "NVIDIA build.nvidia.com / NIM OpenAI-compatible API。"),
        new("MoArk", "moark", "https://api.moark.ai/v1", "gpt-5.5", "https://moark.ai", "文本、工具、长上下文", "MoArk 中转协议模板，保存前请按账号实际模型名调整。"),
        new("Sonnet VIP", "openai-compatible", "https://sonnet.vip/v1", "gpt-5.5", "https://sonnet.vip", "文本、工具、长上下文、按上游模型声明支持视觉", "Sonnet VIP OpenAI-compatible 真实供应商模板；API Key 需用户手动填写，不会随预设写入。"),
        new("sub2api", "sub2api", "https://api.example.com/v1", "gpt-5.5", "https://github.com/maikebing/sub2api", "文本、工具、长上下文", "sub2api 中转站模板，请替换为自己的 Base URL。"),
        new("OpenAI-compatible 中转站", "openai-compatible", "https://api.example.com/v1", "gpt-5.5", "https://example.com", "文本、工具、视觉按上游声明", "通用 OpenAI-compatible 模板，适合 sonnet.vip 等中转站。")
    ];

    private void RenderProviderPresets()
    {
        ReplaceChildren(_providerPresets, ProviderPresets.Select(BuildProviderPresetRow).ToArray());
    }

    private Element BuildProviderPresetRow(ProviderPreset preset)
        => new DockPanel()
            .Padding(8, 6)
            .Children(
                new StackPanel()
                    .Spacing(3)
                    .Children(
                        new Label().Text(preset.Name).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis),
                        BodyLabel($"{preset.Vendor} / {preset.ApiUrl} / 推荐模型 {preset.Model}"),
                        BodyLabel($"能力：{preset.CapabilitySummary}"))
                    .DockLeft(),
                new Button()
                    .Content("套用到表单")
                    .Padding(12, 6)
                    .OnClick(() => ApplyProviderPreset(preset))
                    .DockRight());

    private void ApplyProviderPreset(ProviderPreset preset)
    {
        _providerEditorState = new ProviderEditorState(
            null,
            preset.Name,
            preset.Remark,
            preset.Url,
            preset.ApiUrl,
            preset.Model,
            preset.Vendor,
            ApiKey: string.Empty,
            Active: true,
            IsNew: true);
        _pendingDeleteProviderId = null;
        RenderProviderEditor();
        SetStatus($"已套用 {preset.Name} 预设；请手动填写 API Key 后再保存");
    }

    private void ParseProviderImportPreview()
    {
        var json = _providerImportText.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            RenderProviderImportPreview(Array.Empty<ProviderImportPreview>(), "请先粘贴供应商导出 JSON。");
            return;
        }

        try
        {
            var previews = ParseProviderImports(json);
            RenderProviderImportPreview(previews, previews.Count == 0 ? "JSON 可解析，但没有找到可导入的供应商项。" : $"已解析 {previews.Count} 个供应商；应用只会填表，不会导入密钥。");
            SetStatus(previews.Count == 0 ? "未找到可导入供应商" : $"已解析 {previews.Count} 个导入项");
        }
        catch (JsonException ex)
        {
            RenderProviderImportPreview(Array.Empty<ProviderImportPreview>(), $"JSON 格式无效：{ex.Message}");
            SetStatus("供应商导入 JSON 无效");
        }
        catch (Exception ex)
        {
            RenderProviderImportPreview(Array.Empty<ProviderImportPreview>(), $"导入预览失败：{ex.Message}");
            SetStatus("供应商导入预览失败");
        }
    }

    private void ClearProviderImportPreview()
    {
        _providerImportText.Text = string.Empty;
        RenderProviderImportPreview(Array.Empty<ProviderImportPreview>(), "尚未解析导入 JSON。");
        SetStatus("已清空导入预览");
    }

    private void RenderProviderImportPreview(IReadOnlyList<ProviderImportPreview> previews, string message)
    {
        var children = new List<Element> { BodyLabel(message) };
        children.AddRange(previews.Select(BuildProviderImportPreviewRow));
        ReplaceChildren(_providerImportPreview, children.ToArray());
    }

    private Element BuildProviderImportPreviewRow(ProviderImportPreview preview)
        => new DockPanel()
            .Padding(8, 6)
            .Children(
                new StackPanel()
                    .Spacing(3)
                    .Children(
                        new Label().Text(preview.Name).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis),
                        BodyLabel($"{preview.Vendor} / {preview.ApiUrl} / 模型 {Empty(preview.Model, "未声明")}"),
                        BodyLabel(preview.HasApiKey ? "原导出声明存在密钥；为安全起见不会导入，请手动重新填写。" : "未声明密钥，应用后仍需按需填写。"))
                    .DockLeft(),
                new Button()
                    .Content("应用到表单")
                    .Padding(12, 6)
                    .OnClick(() => ApplyProviderImportPreview(preview))
                    .DockRight());

    private void ApplyProviderImportPreview(ProviderImportPreview preview)
    {
        _providerEditorState = preview.ToEditorState();
        _pendingDeleteProviderId = null;
        RenderProviderEditor();
        SetStatus($"已将 {preview.Name} 填入表单；API Key 未导入，请手动填写后保存");
    }

    private static IReadOnlyList<ProviderImportPreview> ParseProviderImports(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            var array = JsonSerializer.Deserialize(root.GetRawText(), MewUiJsonContext.Default.ProviderImportItemArray);
            return array is null
                ? Array.Empty<ProviderImportPreview>()
                : array.Select(ToImportPreview).Where(item => item is not null).Cast<ProviderImportPreview>().ToArray();
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<ProviderImportPreview>();
        }

        if (root.TryGetProperty("providers", out var providersElement) && providersElement.ValueKind == JsonValueKind.Array)
        {
            var importDocument = JsonSerializer.Deserialize(root.GetRawText(), MewUiJsonContext.Default.ProviderImportDocument);
            return importDocument?.Providers is null
                ? Array.Empty<ProviderImportPreview>()
                : importDocument.Providers.Select(ToImportPreview).Where(item => item is not null).Cast<ProviderImportPreview>().ToArray();
        }

        var item = JsonSerializer.Deserialize(root.GetRawText(), MewUiJsonContext.Default.ProviderImportItem);
        var preview = item is null ? null : ToImportPreview(item);
        return preview is null ? Array.Empty<ProviderImportPreview>() : [preview];
    }

    private static ProviderImportPreview? ToImportPreview(ProviderImportItem item)
    {
        var name = Empty(item.Name, string.Empty);
        var apiUrl = Empty(item.ApiUrl, string.Empty);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(apiUrl))
        {
            return null;
        }

        return new ProviderImportPreview(
            name,
            Empty(item.Vendor, "openai-compatible"),
            apiUrl,
            Empty(item.Model, string.Empty),
            Empty(item.Url, "https://example.com"),
            Empty(item.Remark, string.Empty),
            item.HasApiKey == true
                || !string.IsNullOrWhiteSpace(item.ApiKey)
                || !string.IsNullOrWhiteSpace(item.ApiKeyPreview)
                || item.EncryptedApiKey is not null);
    }
}
