using System.Runtime.InteropServices;

namespace Toolbox.Plugins.Helpers;

/// <summary>
/// ★ 本工具私有（仅 RemoteControlTool 内部使用，不与其他工具共享）：
/// CPU 占用（GetSystemTimes 差分采样）+ 内存总量/可用（GlobalMemoryStatusEx）。
/// P/Invoke 插件内私有声明（遵循 SystemPowerHelper 先例，不改 Core 的 Win32Native），零第三方依赖。
/// </summary>
public static class SystemMetricsHelper
{
    private static readonly object Sync = new();
    private static long _lastIdleTicks;
    private static long _lastTotalTicks;
    private static bool _hasBaseline;

    /// <summary>
    /// CPU 占用百分比（0-100，保留 1 位小数）。采用差分采样：每次调用与上次采样比较，
    /// 首次调用无基线返回 null（非阻塞设计，避免在请求线程内 Sleep 采样）。
    /// </summary>
    public static double? GetCpuUsagePercent()
    {
        lock (Sync)
        {
            if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
                return null;

            var idle = FileTimeToTicks(idleFt);
            var total = FileTimeToTicks(kernelFt) + FileTimeToTicks(userFt);

            if (!_hasBaseline)
            {
                _lastIdleTicks = idle;
                _lastTotalTicks = total;
                _hasBaseline = true;
                return null; // 首次无基线
            }

            var idleDelta = idle - _lastIdleTicks;
            var totalDelta = total - _lastTotalTicks;
            _lastIdleTicks = idle;
            _lastTotalTicks = total;

            if (totalDelta <= 0) return null;
            return Math.Round(100d * (1d - (double)idleDelta / totalDelta), 1);
        }
    }

    /// <summary>内存总量/可用（GB，保留 1 位小数）；失败返回 null</summary>
    public static (double TotalGB, double AvailableGB)? GetMemoryInfo()
    {
        var status = new MemoryStatusEx { DwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status)) return null;

        return (status.UllTotalPhys / 1024d / 1024d / 1024d,
                status.UllAvailPhys / 1024d / 1024d / 1024d);
    }

    // ==================== P/Invoke（插件内私有声明） ====================

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private static long FileTimeToTicks(FileTime ft) =>
        ((long)ft.DwHighDateTime << 32) | (uint)ft.DwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint DwLowDateTime;
        public uint DwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint DwLength;
        public uint DwMemoryLoad;
        public ulong UllTotalPhys;
        public ulong UllAvailPhys;
        public ulong UllTotalPageFile;
        public ulong UllAvailPageFile;
        public ulong UllTotalVirtual;
        public ulong UllAvailVirtual;
        public ulong UllAvailExtendedVirtual;
    }
}
