using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Toolbox.Tools.Helpers
{
    /// <summary>
    /// DWM 窗口背景效果类型（对应 Windows 11 22H2+ 的 DWMWA_SYSTEMBACKDROP_TYPE）。
    /// </summary>
    public enum BackdropType
    {
        Auto = 0,
        None = 1,
        Mica = 2,
        Acrylic = 3,
        MicaAlt = 4
    }

    /// <summary>
    /// 窗口圆角偏好（对应 DWMWA_WINDOW_CORNER_PREFERENCE）。
    /// </summary>
    public enum CornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3
    }

    /// <summary>
    /// DWM / User32 模糊/亚克力效果帮助类。
    /// 
    /// 实现原理与 ExplorerBlurMica 一致：
    /// 1. Windows 11 22H2+ → 官方 DWMWA_SYSTEMBACKDROP_TYPE API（最稳定、性能最好）
    /// 2. Windows 10 / 旧版 Win11 → SetWindowCompositionAttribute + ACCENT_POLICY（未文档化但广泛兼容）
    /// </summary>
    public static class DwmHelper
    {
        #region P/Invoke 常量

        // dwmapi DWM 窗口属性
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMWA_MICA_EFFECT = 1029; // 已废弃，Win11 22H2+ 请用 SYSTEMBACKDROP_TYPE

        // SetWindowCompositionAttribute
        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_ENABLE_BLURBEHIND = 3;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
        private const int ACCENT_ENABLE_HOSTBACKDROP = 5;

        #endregion

        #region P/Invoke 声明

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled(out bool enabled);

        // SetWindowPos flags for SWP_FRAMECHANGED
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_NOMOVE_NOSIZE_NOZORDER_NOACTIVATE = 0x0001 | 0x0002 | 0x0004 | 0x0010;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ACCENT_POLICY
        {
            public int AccentState;
            public int AccentFlags;
            public uint GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        #endregion

        #region 公共 API

        /// <summary>
        /// 设置窗口背景效果（Mica / Acrylic / MicaAlt）。
        /// 仅 Windows 11 22H2 (Build 22621) 及以上生效。
        /// </summary>
        public static bool SetBackdrop(Window window, BackdropType type)
        {
            if (!IsWindows11_22H2OrLater())
                return false;

            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            int value = (int)type;
            return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref value, sizeof(int)) == 0;
        }

        /// <summary>
        /// 设置窗口圆角。
        /// 仅 Windows 11 (Build 22000) 及以上生效。
        /// </summary>
        public static bool SetWindowCorners(Window window, CornerPreference preference)
        {
            if (!IsWindows11OrLater())
                return false;

            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            int value = (int)preference;
            return DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int)) == 0;
        }

        /// <summary>
        /// 设置沉浸式深色模式（影响标题栏和 Mica 效果着色）。
        /// 仅 Windows 10 (Build 17763) 及以上生效。
        /// </summary>
        public static bool SetImmersiveDarkMode(Window window, bool enabled)
        {
            if (!IsWindows10OrLater())
                return false;

            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            int value = enabled ? 1 : 0;
            return DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) == 0;
        }

        /// <summary>
        /// 使用 SetWindowCompositionAttribute 启用 Acrylic 模糊效果。
        /// 适用于 Windows 10 1809+ 及旧版 Windows 11。
        /// 注意：Windows 11 23H2+ 此方法可能失效，优先使用 SetBackdrop。
        /// </summary>
        /// <param name="window">目标 WPF 窗口</param>
        /// <param name="color">混合色，格式 0xAABBGGRR（如 0xCC1A1A1A = 深灰半透明）</param>
        public static bool EnableAcrylicBlur(Window window, uint color = 0)
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();

            var accent = new ACCENT_POLICY
            {
                AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,           // 绘制所有边框（避免黑色边框残留）
                GradientColor = color,
                AnimationId = 0
            };

            IntPtr accentPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ACCENT_POLICY>());
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = accentPtr,
                    SizeOfData = Marshal.SizeOf<ACCENT_POLICY>()
                };
                return SetWindowCompositionAttribute(hwnd, ref data) == 0;
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }

        /// <summary>
        /// 使用 SetWindowCompositionAttribute 启用普通模糊效果（无颜色混合）。
        /// 适用于 Windows 10 1803+。
        /// </summary>
        public static bool EnableBlur(Window window)
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();

            var accent = new ACCENT_POLICY
            {
                AccentState = ACCENT_ENABLE_BLURBEHIND,
                AccentFlags = 2,
                GradientColor = 0,
                AnimationId = 0
            };

            IntPtr accentPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ACCENT_POLICY>());
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = accentPtr,
                    SizeOfData = Marshal.SizeOf<ACCENT_POLICY>()
                };
                return SetWindowCompositionAttribute(hwnd, ref data) == 0;
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }

        /// <summary>
        /// 将窗口客户区扩展到整个窗口（Aero Glass 风格，配合 SetWindowCompositionAttribute 使用）。
        /// 在 Windows 10 上调用 EnableAcrylicBlur 后，建议同时调用此方法以获得最佳效果。
        /// </summary>
        public static void ExtendFrameIntoClientArea(Window window)
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }

        /// <summary>
        /// 完全禁用所有 DWM 背景效果（Acrylic / Mica / Blur）。
        /// 清理 Win11 的 SYSTEMBACKDROP 和 Win10 的 ACCENT_POLICY。
        /// 注意：不撤销 ExtendFrameIntoClientArea，因为 WindowChrome 方案依赖它维持窗口透明。
        /// </summary>
        public static void DisableAllBackdrops(Window window)
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();

            // Win11 22H2+: 设置 BackdropType.None
            if (IsWindows11_22H2OrLater())
                SetBackdrop(window, BackdropType.None);

            // Win10 / 旧版 Win11: 清除 ACCENT_POLICY（AccentState = 0 = disabled）
            var accent = new ACCENT_POLICY { AccentState = 0, AccentFlags = 0, GradientColor = 0, AnimationId = 0 };
            IntPtr accentPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ACCENT_POLICY>());
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = accentPtr,
                    SizeOfData = Marshal.SizeOf<ACCENT_POLICY>()
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }

        /// <summary>
        /// 强制 DWM 重新评估窗口帧区域。
        /// 在窗口尺寸变化后调用，确保 Acrylic/帧扩展在新区间正确渲染。
        /// </summary>
        public static void RefreshWindowFrame(Window window)
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE_NOSIZE_NOZORDER_NOACTIVATE | SWP_FRAMECHANGED);
        }

        #endregion

        #region 系统版本检测

        /// <summary>当前系统是否为 Windows 10 (Build 10240+) 或更高。</summary>
        public static bool IsWindows10OrLater() =>
            Environment.OSVersion.Version >= new Version(10, 0);

        /// <summary>当前系统是否为 Windows 11 (Build 22000+) 或更高。</summary>
        public static bool IsWindows11OrLater() =>
            Environment.OSVersion.Version.Build >= 22000;

        /// <summary>当前系统是否为 Windows 11 22H2 (Build 22621+) 或更高（支持 DWMWA_SYSTEMBACKDROP_TYPE）。</summary>
        public static bool IsWindows11_22H2OrLater() =>
            Environment.OSVersion.Version.Build >= 22621;

        #endregion
    }
}