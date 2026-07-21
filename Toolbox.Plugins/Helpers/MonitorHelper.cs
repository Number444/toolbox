using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Toolbox.Tools.Helpers;

/// <summary>
/// 多屏工作区查询辅助（替代 System.Windows.Forms.Screen）。
/// 使用 Win32 MonitorFromWindow + GetMonitorInfo。
/// </summary>
internal static class MonitorHelper
{
    /// <summary>
    /// 获取指定窗口所在屏幕的 WorkingArea（物理像素坐标）。
    /// </summary>
    public static Rect GetMonitorWorkArea(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var hMonitor = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST

        var mi = new MONITORINFO();
        mi.cbSize = Marshal.SizeOf<MONITORINFO>();
        if (GetMonitorInfo(hMonitor, ref mi))
        {
            return new Rect(
                mi.rcWork.Left,
                mi.rcWork.Top,
                mi.rcWork.Right - mi.rcWork.Left,
                mi.rcWork.Bottom - mi.rcWork.Top);
        }

        // 失败时回退到系统主屏工作区
        return new Rect(
            SystemParameters.WorkArea.Left,
            SystemParameters.WorkArea.Top,
            SystemParameters.WorkArea.Width,
            SystemParameters.WorkArea.Height);
    }

    /// <summary>
    /// 获取指定窗口所在屏幕的 WorkingArea（DIP 坐标，可直接与 Window.Left/Top 比较）。
    /// </summary>
    public static Rect GetMonitorWorkAreaDips(Window window)
    {
        var phys = GetMonitorWorkArea(window);
        var (dpiX, dpiY) = GetDpiScale(window);
        return new Rect(
            phys.Left / dpiX,
            phys.Top / dpiY,
            phys.Width / dpiX,
            phys.Height / dpiY);
    }

    private static (double ScaleX, double ScaleY) GetDpiScale(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget == null) return (1.0, 1.0);
        return (
            source.CompositionTarget.TransformToDevice.M11,
            source.CompositionTarget.TransformToDevice.M22);
    }

    // ── Win32 ──

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>WPF 内部使用的 Rect 结构，与 Win32 RECT 可转换。</summary>
    internal struct Rect
    {
        public double Left;
        public double Top;
        public double Width;
        public double Height;
        public double Right => Left + Width;
        public double Bottom => Top + Height;

        public Rect(double left, double top, double width, double height)
        {
            Left = left; Top = top; Width = width; Height = height;
        }
    }
}