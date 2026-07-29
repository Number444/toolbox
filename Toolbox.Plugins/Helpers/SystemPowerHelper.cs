using System.Runtime.InteropServices;

namespace Toolbox.Tools.Helpers;

/// <summary>
/// 快捷系统操作 —— 锁屏 / 关闭显示器 / 睡眠的 Win32 封装。
/// 供「快捷系统操作」工具与「首页」仪表盘的快捷操作卡共用。
/// 注意:插件层无法访问主程序的 Win32Helper,此处自含 P/Invoke。
/// </summary>
public static class SystemPowerHelper
{
    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool force, bool wakeupEventDisabled);

    private const IntPtr HwndBroadcast = 0xFFFF;
    private const uint WmSysCommand = 0x0112;
    private const IntPtr ScMonitorPower = 0xF170;
    private const IntPtr MonitorOff = 2;    // -1 开 / 1 低功耗 / 2 关闭

    /// <summary>锁定工作站(等同 Win+L);成功返回 true</summary>
    public static bool Lock()
    {
        try { return LockWorkStation(); }
        catch { return false; }
    }

    /// <summary>关闭显示器(移动鼠标/按键即唤醒);成功返回 true</summary>
    public static bool TurnOffMonitor()
    {
        try
        {
            SendMessage(HwndBroadcast, WmSysCommand, ScMonitorPower, MonitorOff);
            return true;
        }
        catch { return false; }
    }

    /// <summary>进入睡眠(不休眠、不强制);成功返回 true</summary>
    public static bool Sleep()
    {
        try { return SetSuspendState(false, true, false); }
        catch { return false; }
    }
}
