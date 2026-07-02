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

    // ---- 新增：将 DWM 帧扩展到标题栏区域，使 Mica 透入 ----

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