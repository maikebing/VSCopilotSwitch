using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using VSCopilotSwitch.Services;

namespace VSCopilotSwitch;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
        Thread.CurrentThread.SetApartmentState(ApartmentState.STA);

        Win32Platform.Register();
        GdiBackend.Register();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                StartupDiagnostics.WriteCrashLog(ex);
                NativeMessageBox.Show(ex.ToString(), "VSCopilotSwitch fatal error", NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Error);
            }
        };

        Application.DispatcherUnhandledException += e =>
        {
            StartupDiagnostics.WriteCrashLog(e.Exception, "MewUI dispatcher error.");
            NativeMessageBox.Show(e.Exception.ToString(), "VSCopilotSwitch UI error", NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Error);
            e.Handled = true;
        };

        VSCopilotSwitchNativeHost? nativeHost = null;
        NativeWorkbench? workbench = null;
        try
        {
            nativeHost = VSCopilotSwitchNativeHost.StartAsync(args).GetAwaiter().GetResult();
            workbench = new NativeWorkbench(nativeHost);
            workbench.Run();
        }
        finally
        {
            workbench?.Dispose();
            nativeHost?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
