using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace VSCopilotSwitch;

internal sealed partial class NativeWorkbench
{
    private void ApplyVs2026(Vs2026ByomInfoResponse info)
    {
        var endpoint = Empty(info.Endpoint, "未启用 HTTPS");
        var validateUrl = info.Endpoint is null ? "未启用 HTTPS" : $"{info.Endpoint.TrimEnd('/')}/models/{info.ModelId}";
        var chatUrl = info.Endpoint is null ? "未启用 HTTPS" : $"{info.Endpoint.TrimEnd('/')}/chat/completions";
        ReplaceChildren(
            _vs2026Panel,
            Row("Provider", "Azure"),
            Row("Resource Endpoint / Custom URL", endpoint),
            Row("Model ID", info.ModelId),
            Row("API Key", info.ApiKeyPlaceholder),
            Row("模型校验 URL", validateUrl),
            Row("聊天 URL", chatUrl),
            Row("HTTPS 状态", info.HttpsEnabled ? "已启用" : "未启用"),
            BodyLabel(info.Message),
            new StackPanel()
                .Horizontal()
                .Spacing(8)
                .Children(
                    new Button().Content("复制全部").Padding(16, 8).OnClick(() => CopyVs2026Info(info, validateUrl, chatUrl)),
                    new Button().Content("刷新 VS2026").Padding(16, 8).OnClick(() => _ = RefreshVs2026Async())));
    }

    private async Task RefreshVs2026Async()
    {
        try
        {
            var info = await _api.GetJsonAsync<Vs2026ByomInfoResponse>("/internal/vs2026/byom", MewUiJsonContext.Default.Vs2026ByomInfoResponse);
            Application.Current.Dispatcher?.BeginInvoke(() =>
            {
                ApplyVs2026(info);
                SetStatus("VS2026 信息已刷新");
            });
        }
        catch (Exception ex)
        {
            ShowError("刷新 VS2026 信息失败", ex);
        }
    }

    private void CopyVs2026Info(Vs2026ByomInfoResponse info, string validateUrl, string chatUrl)
    {
        var text = string.Join(Environment.NewLine, new[]
        {
            "Provider: Azure",
            $"Resource Endpoint / Custom URL: {Empty(info.Endpoint, "未启用 HTTPS")}",
            $"Model ID: {info.ModelId}",
            $"API Key: {info.ApiKeyPlaceholder}",
            $"Model validate URL: {validateUrl}",
            $"Chat URL: {chatUrl}"
        });
        SetStatus(NativeClipboard.SetText(text) ? "VS2026 信息已复制" : "复制到剪贴板失败");
    }
}
