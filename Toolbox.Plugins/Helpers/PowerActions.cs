using System.Diagnostics;

namespace Toolbox.Plugins.Helpers;

/// <summary>
/// ★ 本工具私有（仅 RemoteControlTool 内部使用，不与其他工具共享）：
/// shutdown.exe / taskkill+explorer 标准命令封装（设计文档 4.1 节）。
/// 进程启动经可注入执行器委托：测试注入记录型假执行器，绝不触发真实关机（设计文档 9 章）。
/// </summary>
public sealed class PowerActions
{
    private readonly Func<ProcessStartInfo, int> _executor;

    public PowerActions() : this(DefaultExecutor) { }

    /// <param name="executor">进程启动委托：返回退出码；-1 表示启动失败/超时</param>
    public PowerActions(Func<ProcessStartInfo, int> executor) => _executor = executor;

    /// <summary>定时关机（延迟秒数已由调用方校验范围）</summary>
    public int Shutdown(int delaySeconds) => Run("shutdown.exe", $"/s /t {delaySeconds}");

    /// <summary>立即重启</summary>
    public int Restart() => Run("shutdown.exe", "/r /t 0");

    /// <summary>取消已排程关机</summary>
    public int CancelShutdown() => Run("shutdown.exe", "/a");

    /// <summary>结束资源管理器进程（explorer 未运行时返回非 0，属预期）</summary>
    public int KillExplorer() => Run("taskkill.exe", "/f /im explorer.exe");

    /// <summary>启动资源管理器（shell 应用，不等待退出）</summary>
    public int StartExplorer() => RunShell("explorer.exe");

    private int Run(string fileName, string arguments) =>
        _executor(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        });

    private int RunShell(string fileName) =>
        _executor(new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = true
        });

    /// <summary>
    /// 默认执行器：命令行工具 WaitForExit + 退出码校验（防假成功）；
    /// shell 应用（explorer）不等待（GUI 进程常驻，等待必超时）。
    /// </summary>
    private static int DefaultExecutor(ProcessStartInfo psi)
    {
        try
        {
            using var process = Process.Start(psi);
            if (process == null) return -1;
            if (psi.UseShellExecute) return 0;
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(); } catch (Exception) { }
                return -1;
            }
            return process.ExitCode;
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
