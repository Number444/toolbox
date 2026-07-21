using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Toolbox.Helpers;

/// <summary>
/// 纯 Win32 实现的系统托盘图标（不依赖 WinForms NotifyIcon）。
/// 使用 Shell_NotifyIconW + 隐藏 HwndSource 接收回调。
/// </summary>
public sealed class SystemTrayHelper : IDisposable
{
    private static readonly Lazy<SystemTrayHelper> _instance = new(() => new SystemTrayHelper());
    public static SystemTrayHelper Instance => _instance.Value;

    private HwndSource? _hwndSource;
    private bool _added;
    private bool _isDisposed;

    private Action? _onDoubleClick;
    private Action? _onExitClick;

    private const uint WM_APP_NOTIFYICON = 0x8000; // WM_APP
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr CopyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private IntPtr _hIcon = IntPtr.Zero;

    private SystemTrayHelper() { }

    public bool IsVisible => _added;

    public void Show(string tooltip, Action onDoubleClick, Action onExitClick)
    {
        if (_added) return;

        _onDoubleClick = onDoubleClick;
        _onExitClick = onExitClick;

        // 创建隐藏 HwndSource 用于接收托盘图标回调
        if (_hwndSource == null)
        {
            var parameters = new HwndSourceParameters("ToolboxTray")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                PositionX = 0,
                PositionY = 0
            };
            _hwndSource = new HwndSource(parameters);
            _hwndSource.AddHook(WndProc);
        }

        // 加载图标
        _hIcon = LoadAppIcon();

        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hwndSource.Handle,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_APP_NOTIFYICON,
            hIcon = _hIcon,
            szTip = tooltip ?? "Toolbox"
        };

        _added = Shell_NotifyIconW(NIM_ADD, ref data);
    }

    public void Hide()
    {
        if (!_added || _hwndSource == null) return;

        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hwndSource.Handle,
            uID = 1
        };

        Shell_NotifyIconW(NIM_DELETE, ref data);
        _added = false;

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Hide();
        _hwndSource?.Dispose();
        _hwndSource = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_APP_NOTIFYICON)
        {
            var mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
            switch (mouseMsg)
            {
                case WM_LBUTTONDBLCLK:
                    _onDoubleClick?.Invoke();
                    handled = true;
                    break;

                case WM_RBUTTONUP:
                    ShowContextMenu();
                    handled = true;
                    break;
            }
        }

        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        GetCursorPos(out var pt);

        // 物理像素 → DIP（托盘回调窗口的 DPI 即系统 DPI）
        double dpi = _hwndSource?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var pos = new Point(pt.X / dpi, pt.Y / dpi);

        Core.Controls.ThemedMenuWindow.ShowAt(pos, new[]
        {
            new Core.Controls.ThemedMenuWindow.Item
            {
                Text = "显示 Toolbox",
                Action = () => _onDoubleClick?.Invoke()
            },
            Core.Controls.ThemedMenuWindow.Item.Separator(),
            new Core.Controls.ThemedMenuWindow.Item
            {
                Text = "退出",
                Action = () => _onExitClick?.Invoke()
            },
        });
    }

    private IntPtr LoadAppIcon()
    {
        // 尝试从应用资源加载 Toolbox.ico
        try
        {
            var uri = new Uri("pack://application:,,,/Toolbox.ico", UriKind.Absolute);
            var resourceStream = Application.GetResourceStream(uri);
            if (resourceStream != null)
            {
                using var stream = resourceStream.Stream;
                using var icon = new System.Drawing.Icon(stream);
                // 复制句柄，原 icon 在 using 结束后释放
                var copied = CopyIcon(icon.Handle);
                if (copied != IntPtr.Zero) return copied;
            }
        }
        catch { }

        // 尝试从 WPF 图标资源加载
        if (Application.Current?.MainWindow?.Icon is ImageSource imageSource)
        {
            try
            {
                using var stream = new System.IO.MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create((BitmapSource)imageSource));
                encoder.Save(stream);
                stream.Position = 0;
                using var icon = new System.Drawing.Icon(stream);
                var copied = CopyIcon(icon.Handle);
                if (copied != IntPtr.Zero) return copied;
            }
            catch { }
        }

        // 回退到系统默认图标
        return LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
    }
}
