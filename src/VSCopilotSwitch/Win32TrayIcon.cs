using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Aprillz.MewUI;
using VSCopilotSwitch.Services;

namespace VSCopilotSwitch;

internal sealed class Win32TrayIcon : IDisposable
{
    private const int CallbackMessage = 0x0400 + 424;
    private const uint IconId = 1;
    private const uint CommandOpen = 1001;
    private const uint CommandExit = 1002;
    private const uint CommandCustomStart = 2000;
    private const uint WmCommand = 0x0111;
    private const uint WmDestroy = 0x0002;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint MfGrayed = 0x00000001;
    private const uint MfChecked = 0x00000008;

    private readonly Window _window;
    private readonly ITrayMenuService _trayMenu;
    private readonly Func<bool> _requestExit;
    private readonly WndProc _wndProc;
    private readonly Dictionary<uint, string> _customCommands = new();
    private IntPtr _messageWindow;
    private IntPtr _icon;
    private bool _created;

    public Win32TrayIcon(Window window, ITrayMenuService trayMenu, Func<bool> requestExit)
    {
        _window = window;
        _trayMenu = trayMenu;
        _requestExit = requestExit;
        _wndProc = WindowProc;
    }

    public void Initialize()
    {
        if (!OperatingSystem.IsWindows() || _created)
        {
            return;
        }

        _messageWindow = CreateMessageWindow();
        _icon = LoadIcon();
        var data = CreateNotifyIconData(NifMessage | NifIcon | NifTip);
        if (!Shell_NotifyIconW(NimAdd, ref data))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建系统托盘图标。");
        }

        _created = true;
    }

    public void UpdateToolTip()
    {
        if (!_created)
        {
            return;
        }

        var data = CreateNotifyIconData(NifTip);
        Shell_NotifyIconW(NimModify, ref data);
    }

    public void Dispose()
    {
        if (_created)
        {
            var data = CreateNotifyIconData(0);
            Shell_NotifyIconW(NimDelete, ref data);
            _created = false;
        }

        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }

        if (_messageWindow != IntPtr.Zero)
        {
            DestroyWindow(_messageWindow);
            _messageWindow = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == CallbackMessage)
        {
            var trayMessage = unchecked((uint)lParam.ToInt64());
            if (trayMessage == WmLButtonDblClk)
            {
                ShowMainWindow();
                return IntPtr.Zero;
            }

            if (trayMessage == WmRButtonUp)
            {
                ShowTrayMenu();
                return IntPtr.Zero;
            }
        }

        if (message == WmCommand)
        {
            HandleCommand(unchecked((uint)(wParam.ToInt64() & 0xffff)));
            return IntPtr.Zero;
        }

        if (message == WmDestroy)
        {
            RemoveTrayIcon();
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void HandleCommand(uint command)
    {
        if (command == CommandOpen)
        {
            ShowMainWindow();
            return;
        }

        if (command == CommandExit)
        {
            if (_requestExit())
            {
                _window.Close();
            }

            return;
        }

        if (!_customCommands.TryGetValue(command, out var commandId))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await _trayMenu.HandleCommandAsync(commandId, CancellationToken.None);
            Application.Current.Dispatcher?.BeginInvoke(UpdateToolTip);
        });
    }

    private void ShowMainWindow()
    {
        _window.Show();
        _window.Activate();
        UpdateToolTip();
    }

    private void ShowTrayMenu()
    {
        UpdateToolTip();
        using var menu = new SafeMenuHandle(CreatePopupMenu());
        if (menu.IsInvalid)
        {
            return;
        }

        AppendMenuW(menu.DangerousGetHandle(), MfString, CommandOpen, "打开 VSCopilotSwitch");
        AppendMenuW(menu.DangerousGetHandle(), MfSeparator, 0, null);
        AppendCustomItems(menu.DangerousGetHandle());
        AppendMenuW(menu.DangerousGetHandle(), MfSeparator, 0, null);
        AppendMenuW(menu.DangerousGetHandle(), MfString, CommandExit, "退出 VSCopilotSwitch");

        GetCursorPos(out var point);
        SetForegroundWindow(_messageWindow);
        TrackPopupMenu(menu.DangerousGetHandle(), 0, point.X, point.Y, 0, _messageWindow, IntPtr.Zero);
    }

    private void AppendCustomItems(IntPtr menu)
    {
        _customCommands.Clear();
        var nextCommand = CommandCustomStart;
        foreach (var item in _trayMenu.GetMenuItems())
        {
            if (item.IsSeparator)
            {
                AppendMenuW(menu, MfSeparator, 0, null);
                continue;
            }

            var flags = MfString
                        | (item.Enabled ? 0 : MfGrayed)
                        | (item.Checked ? MfChecked : 0);
            var command = string.IsNullOrWhiteSpace(item.CommandId) ? 0 : nextCommand++;
            if (command != 0)
            {
                _customCommands[command] = item.CommandId;
            }

            AppendMenuW(menu, flags, command, item.Text);
        }
    }

    private NOTIFYICONDATAW CreateNotifyIconData(uint flags)
        => new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _messageWindow,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
            szTip = Trim(_trayMenu.GetToolTip(), 127)
        };

    private static string Trim(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static IntPtr LoadIcon()
    {
        var iconPath = EnsureIconFile();
        if (File.Exists(iconPath))
        {
            var icon = LoadImageW(IntPtr.Zero, iconPath, 1, 0, 0, 0x00000010);
            if (icon != IntPtr.Zero)
            {
                return icon;
            }
        }

        return LoadIconW(IntPtr.Zero, new IntPtr(32512));
    }

    private static string EnsureIconFile()
    {
        var iconPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VSCopilotSwitch",
            "Assets",
            "VSCopilotSwitch.ico");
        Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(static name => name.EndsWith("VSCopilotSwitch.ico", StringComparison.OrdinalIgnoreCase));
        using var resource = resourceName is null ? null : assembly.GetManifestResourceStream(resourceName);
        if (resource is null)
        {
            return iconPath;
        }

        var shouldWrite = !File.Exists(iconPath) || new FileInfo(iconPath).Length != resource.Length;
        if (!shouldWrite)
        {
            return iconPath;
        }

        using var file = File.Create(iconPath);
        resource.CopyTo(file);
        return iconPath;
    }

    private IntPtr CreateMessageWindow()
    {
        var className = "VSCopilotSwitchTrayWindow";
        var module = GetModuleHandleW(null);
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = module,
            lpszClassName = className
        };

        RegisterClassExW(ref wc);
        var hwnd = CreateWindowExW(
            0,
            className,
            "VSCopilotSwitch Tray",
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            module,
            IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建托盘消息窗口。");
        }

        return hwnd;
    }

    private void RemoveTrayIcon()
    {
        if (!_created)
        {
            return;
        }

        var data = CreateNotifyIconData(0);
        Shell_NotifyIconW(NimDelete, ref data);
        _created = false;
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private sealed class SafeMenuHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeMenuHandle(IntPtr handle)
            : base(ownsHandle: true)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle()
            => DestroyMenu(handle);
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);
}
