using System;
using System.Runtime.InteropServices;

namespace Toolbox.Core.Helpers;

/// <summary>
/// 全项目唯一的 Win32 P/Invoke 声明处（主程序 / Toolbox.Plugins / Toolbox.Core 三个程序集共用）。
/// 规则：任何新增的 Win32 API 声明、互操作结构与常量一律加在这里，禁止在调用方文件里重复声明。
/// </summary>
public static class Win32Native
{
    // ══════════════════ dwmapi.dll ══════════════════

    /// <summary>设置 DWM 窗口属性（Win11 圆角 / Mica / 深色标题栏 / 边框颜色等）</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    /// <summary>将 DWM 帧扩展到客户区（让 Mica/Acrylic 透入标题栏）</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(
        IntPtr hwnd,
        ref MARGINS pMarInset);

    // DWM 窗口属性常量（DWMWINDOWATTRIBUTE）
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    /// <summary>DWM 帧扩展边距（四方向全 -1 = 帧覆盖整个窗口）</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    // ══════════════════ user32.dll ══════════════════

    /// <summary>设置窗口合成属性（未文档化 API，Win10 1809+ / 旧版 Win11 的 Acrylic/Blur 方案）</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    // SetWindowCompositionAttribute 常量
    public const int WCA_ACCENT_POLICY = 19;
    public const int ACCENT_ENABLE_BLURBEHIND = 3;
    public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    /// <summary>窗口合成 Accent 策略（SetWindowCompositionAttribute 的数据负载）</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ACCENT_POLICY
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    /// <summary>调整窗口位置/尺寸/层级/帧状态（SWP_* 标志由调用方各自持有）</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    /// <summary>获取光标屏幕坐标（原始物理像素）</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    // ══════════════════ 任务栏嵌入专用 API ══════════════════

    /// <summary>查找顶层窗口（任务栏嵌入用：查找 Shell_TrayWnd）</summary>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    /// <summary>查找子窗口（任务栏嵌入用：在 Shell_TrayWnd 下查找 TrayNotifyWnd）</summary>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

    /// <summary>设置窗口父句柄（核心：将 WPF 窗口嵌入任务栏）</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    /// <summary>获取窗口父句柄（验证嵌入是否生效）</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetParent(IntPtr hWnd);

    /// <summary>显示/隐藏窗口（任务栏控件"无播放自动隐藏"用）</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>隐藏窗口</summary>
    public const int SW_HIDE = 0;
    /// <summary>以当前位置显示窗口，不激活</summary>
    public const int SW_SHOWNOACTIVATE = 4;

    /// <summary>获取窗口矩形（物理像素，用于计算任务栏上的可用空间）</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>修改窗口样式（任务栏嵌入用：添加/移除 WS_CHILD）</summary>
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>读取窗口样式（任务栏嵌入用：读取当前样式后再修改）</summary>
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    /// <summary>注册自定义窗口消息（任务栏嵌入用：监听 Explorer 重启消息 WM_TASKBARCREATED）</summary>
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int RegisterWindowMessage(string lpString);

    // ══════════════════ 诊断截图专用 GDI API（PrintWindow 验证分层窗口渲染）══════════════════

    /// <summary>获取窗口 DC（含非客户区）</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetWindowDC(IntPtr hWnd);

    /// <summary>释放窗口 DC</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);

    /// <summary>创建与指定 DC 兼容的内存 DC</summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    /// <summary>创建与指定 DC 兼容的位图</summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    /// <summary>选择 GDI 对象到 DC</summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    /// <summary>删除 GDI 对象</summary>
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    /// <summary>删除内存 DC</summary>
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr hdc);

    /// <summary>将指定窗口内容绘制到 DC（PW_RENDERFULLCONTENT=2 可抓取分层窗口完整内容）</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    /// <summary>PrintWindow 标志：渲染完整内容（含 DirectX/分层窗口，Win8.1+）</summary>
    public const uint PW_RENDERFULLCONTENT = 0x00000002;

    // 窗口样式常量（GWL_STYLE / GWL_EXSTYLE）
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int WS_CHILD = 0x40000000;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;

    // SetWindowPos 常量（配合已有 SetWindowPos 声明）
    public const int HWND_TOP = 0;
    public const int HWND_TOPMOST = -1;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    /// <summary>Win32 RECT 结构（与 WPF Rect 区分，用于 GetWindowRect）</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }
}
