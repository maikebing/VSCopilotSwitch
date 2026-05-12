using System.Globalization;
using System.Text;

namespace VSCopilotSwitch.Services;

internal static class StartupDiagnostics
{
    private const string LogFileName = "startup-error.log";
    private static readonly object WriteLock = new();

    public static void WriteCrashLog(Exception exception, string? note = null)
    {
        try
        {
            var path = GetLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var builder = new StringBuilder();
            builder.Append('[').Append(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)).Append("] ");
            if (!string.IsNullOrWhiteSpace(note))
            {
                builder.AppendLine(note);
            }
            builder.AppendLine(exception.ToString());
            builder.AppendLine();

            lock (WriteLock)
            {
                File.AppendAllText(path, builder.ToString());
            }
        }
        catch
        {
            // 诊断日志本身失败时无处可写，只能放弃，避免反噬调用方。
        }
    }

    public static string GetLogPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VSCopilotSwitch",
            LogFileName);
}
