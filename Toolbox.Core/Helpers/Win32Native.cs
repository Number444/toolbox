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
}
