using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Toolbox.Core.Helpers;
using static Toolbox.Core.Helpers.Win32Native;

namespace Toolbox.Tools.Helpers
{
    /// <summary>
    /// 点击穿透（游戏模式）共享实现 —— AcrylicMusicWindow / TransparentMusicWindow 共用。
    /// 开启后鼠标事件直接落到下层窗口（游戏），悬浮窗变成纯信息展示，不可拖拽、不可交互、不可被激活。
    /// </summary>
    public static class ClickThroughHelper
    {
        /// <summary>每个窗口的穿透状态（钩子只注册一次，消息处理内部用 IsClickThrough 门控）。</summary>
        private sealed class State
        {
            public bool IsClickThrough;
            public bool HookRegistered;
            /// <summary>layered 窗口（AllowsTransparency）的样式 mask 多一个 WS_EX_TRANSPARENT。</summary>
            public bool Layered;
        }

        private static readonly ConditionalWeakTable<Window, State> States = new();

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private static readonly IntPtr HTTRANSPARENT = new(-1);
        private static readonly IntPtr MA_NOACTIVATE = new(3);

        /// <summary>
        /// 窗口 OnSourceInitialized 时调用：HWND 在此时创建完毕，是注册钩子和修改扩展样式的最早可靠时机。
        /// </summary>
        public static void OnSourceInitialized(Window window, bool layered)
        {
            var state = States.GetOrCreateValue(window);
            state.Layered = layered;
            EnsureHook(window, state);
            ApplyStyles(window, state);
        }

        /// <summary>开启/关闭鼠标点击穿透（游戏模式）。</summary>
        public static void SetClickThrough(Window window, bool enable, bool layered)
        {
            var state = States.GetOrCreateValue(window);
            state.Layered = layered;
            state.IsClickThrough = enable;
            EnsureHook(window, state);
            ApplyStyles(window, state);
        }

        /// <summary>钩子只注册一次，消息处理内部用 IsClickThrough 门控。</summary>
        private static void EnsureHook(Window window, State state)
        {
            if (state.HookRegistered) return;
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return; // 窗口未 Show 前无 HWND，等 OnSourceInitialized
            HwndSource.FromHwnd(hwnd)?.AddHook(
                (IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
                    => WndProc(state, msg, ref handled));
            state.HookRegistered = true;
        }

        private static IntPtr WndProc(State state, int msg, ref bool handled)
        {
            if (!state.IsClickThrough) return IntPtr.Zero;

            // 命中测试透明：点击穿透到下层窗口（对 WS_EX_TRANSPARENT 之外的残余路径兜底）
            if (msg == WM_NCHITTEST)
            {
                handled = true;
                return HTTRANSPARENT;
            }

            // 兜底：即使点击意外落在本窗口，也拒绝激活（防止抢前台导致游戏鼠标脱捕）
            if (msg == WM_MOUSEACTIVATE)
            {
                handled = true;
                return MA_NOACTIVATE;
            }

            return IntPtr.Zero;
        }

        // ── Win32 扩展样式 ──────────────────────────────────────
        // HTTRANSPARENT 只保证点击"穿过去"；WS_EX_NOACTIVATE 保证窗口任何
        // 情况下都不会被激活——后者才是防止游戏丢失前台、鼠标脱捕的根治。
        // layered 窗口（AllowsTransparency）可加 WS_EX_TRANSPARENT：系统在命中
        // 测试阶段就整块跳过本窗口，不派发任何消息，是最硬的穿透手段；
        // 非 layered 窗口（DWM Acrylic 渲染路径）不能用 WS_EX_TRANSPARENT。

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        /// <summary>按 IsClickThrough 切换扩展样式（layered 窗口含 WS_EX_TRANSPARENT）。</summary>
        private static void ApplyStyles(Window window, State state)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return; // 等 OnSourceInitialized

            int mask = state.Layered
                ? WS_EX_NOACTIVATE | WS_EX_TRANSPARENT
                : WS_EX_NOACTIVATE;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            int newStyle = state.IsClickThrough
                ? exStyle | mask
                : exStyle & ~mask;
            if (newStyle == exStyle) return;

            SetWindowLong(hwnd, GWL_EXSTYLE, newStyle);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
    }
}
