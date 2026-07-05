using System.Runtime.InteropServices;

namespace Toolbox.Helpers;

/// <summary>
/// Win32 API P/Invoke —— 用于启用 Windows 11 圆角和 Mica 材质
/// </summary>
public static class Win32Helper
{
    // DWM 窗口属性常量
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;

    // 圆角类型（官方值：0=Default, 1=DoNotRound, 2=Round, 3=RoundSmall）
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

    // 背景材质类型
    private const int DWMSBT_MAINWINDOW = 2;
    private const int DWMSBT_TABBEDWINDOW = 4;
    private const int DWMSBT_ACRYLIC = 3;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    /// <summary>启用 Windows 11 圆角窗口</summary>
    public static void EnableRoundedCorners(IntPtr hwnd)
    {
        int cornerPreference = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
    }

    /// <summary>
    /// 启用 Mica Alt 背景材质（与 Windows 文件资源管理器同款），
    /// 低版本 Win11 自动降级为标准 Mica
    /// </summary>
    public static void EnableMicaBackdrop(IntPtr hwnd)
    {
        // Mica Alt (DWMSBT_TABBEDWINDOW = 4) 需要 Windows 11 Build 22621+
        // 低版本使用标准 Mica (DWMSBT_MAINWINDOW = 2) 作为降级
        int backdropType = Environment.OSVersion.Version.Build >= 22621
            ? DWMSBT_TABBEDWINDOW
            : DWMSBT_MAINWINDOW;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
    }

    /// <summary>
    /// 启用沉浸式深色模式——强制 DWM 使用深色标题栏/边框，
    /// 避免系统浅色主题下 DWM 绘制白色边框。
    /// </summary>
    public static void EnableDarkMode(IntPtr hwnd)
    {
        int dark = 1;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    /// <summary>
    /// 覆写 DWM 边框颜色——将系统绘制的 resize border 颜色
    /// 替换为指定值。传入 0xFFFFFFFE 可完全禁用 DWM 边框绘制。
    /// 颜色格式：0x00bbggrr（BGR 序）
    /// </summary>
    public static void SetBorderColor(IntPtr hwnd, uint colorBgr = 0xFFFFFFFE)
    {
        int borderColor = unchecked((int)colorBgr);
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
    }

    // ---- 将 DWM 帧扩展到标题栏区域，使 Mica 透入 ----

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(
        IntPtr hwnd,
        ref MARGINS pMarInset);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    /// <summary>将 DWM 帧扩展到整个客户区（让 Mica 透入标题栏）</summary>
    public static void ExtendFrameIntoClientArea(IntPtr hwnd)
    {
        // 四方向都设为 -1，表示帧覆盖整个窗口
        var margins = new MARGINS
        {
            cxLeftWidth = -1,
            cxRightWidth = -1,
            cyTopHeight = -1,
            cyBottomHeight = -1
        };
        _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    // ---- WM_NCCALCSIZE & WM_ERASEBKGND 拦截 ----

    private const int WM_NCCALCSIZE = 0x0083;
    private const int WM_ERASEBKGND = 0x0014;

    /// <summary>
    /// WndProc 消息钩子（由 HwndSource.AddHook 注册）。
    /// 拦截 WM_NCCALCSIZE，返回 0 告诉系统：没有非客户区，
    /// 客户区 = 整个窗口。消除 AllowsTransparency=False +
    /// WindowStyle=None + ExtendFrameIntoClientArea(-1) 组合下
    /// 的 1px GDI NC 边界残留（特定机器上表现为白色线条）。
    /// 同时拦截 WM_ERASEBKGND 阻止系统默认白色背景填充。
    /// </summary>
    public static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCCALCSIZE && wParam != IntPtr.Zero)
        {
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_ERASEBKGND)
        {
            handled = true;
            return new IntPtr(1);
        }

        return IntPtr.Zero;
    }

    // ---- 单实例互斥锁辅助 API ----

    public const int SW_RESTORE = 9;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>通过窗口标题查找已运行的实例</summary>
    public static IntPtr FindWindowByTitle(string title)
    {
        return FindWindow(null, title);
    }
}