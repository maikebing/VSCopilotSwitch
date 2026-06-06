using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace VSCopilotSwitch;

internal sealed partial class NativeWorkbench
{
    private static Element MetricCard(string title, Label value)
        => new GroupBox()
            .Header(title, accessKey: false)
            .Padding(14)
            .Content(
                new StackPanel()
                    .Spacing(5)
                    .Children(value));

    private static Element Panel(string title, Element content)
        => new GroupBox()
            .Header(title, accessKey: false)
            .Padding(14)
            .Content(content);

    private static Element Row(string title, string subtitle)
        => new DockPanel()
            .Padding(8, 6)
            .Children(
                new StackPanel()
                    .Spacing(3)
                    .Children(
                        new Label().Text(title).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis),
                        BodyLabel(subtitle).TextWrapping(TextWrapping.Wrap)));

    private static Label BodyLabel(string text)
        => new Label()
            .Text(text)
            .TextWrapping(TextWrapping.Wrap);

    private static Label ValueLabel(string text)
        => new Label()
            .Text(text)
            .FontSize(20)
            .SemiBold()
            .TextTrimming(TextTrimming.CharacterEllipsis);

    private static Label ErrorLabel(string text)
        => new Label()
            .Text(text)
            .TextWrapping(TextWrapping.Wrap);

    private static void ReplaceChildren(StackPanel panel, params Element[] children)
    {
        panel.Clear();
        panel.AddRange(children);
    }

    private void SetStatus(string text)
    {
        if (_statusText is not null)
        {
            _statusText.Text = text;
        }
    }

    private void ShowError(string title, Exception ex)
    {
        Application.Current.Dispatcher?.BeginInvoke(() =>
        {
            SetStatus(title);
            NativeMessageBox.Show(ex.Message, title, NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Error);
        });
    }

    private void OpenCurrentWebUi()
    {
        var url = _api.BaseAddress?.AbsoluteUri ?? "http://127.0.0.1:5124/";
        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    private static string Empty(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string ShortValue(string value)
    {
        var normalized = value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return normalized.Length <= 160 ? normalized : normalized[..160] + "...";
    }
}
