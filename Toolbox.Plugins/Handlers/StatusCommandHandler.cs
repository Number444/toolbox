using System.IO;
using System.Text.Json;
using Toolbox.Plugins.Helpers;
using Toolbox.Plugins.Models;
using Toolbox.Tools.Helpers;

namespace Toolbox.Plugins.Handlers;

/// <summary>
/// 状态查询执行器：status（CPU/内存/磁盘/运行时长/IPv4）+ network（MAC/网关/DNS/公网 IP）。
/// 数据源经构造注入：测试注入固定快照断言字段完整性，不依赖真实环境（设计文档 9 章）。
/// </summary>
public sealed class StatusCommandHandler : IRemoteCommandHandler
{
    private readonly Func<SystemStatusSnapshot> _statusSource;
    private readonly Func<NetworkDetailSnapshot> _networkSource;

    public StatusCommandHandler() : this(BuildStatusSnapshot, BuildNetworkSnapshot) { }

    public StatusCommandHandler(Func<SystemStatusSnapshot> statusSource, Func<NetworkDetailSnapshot> networkSource)
    {
        _statusSource = statusSource;
        _networkSource = networkSource;
    }

    public bool CanHandle(string command) => command is "status" or "network";

    public RemoteControlResponse Execute(string command, Dictionary<string, JsonElement>? args) => command switch
    {
        "status" => RemoteControlResponse.Ok(_statusSource()),
        "network" => RemoteControlResponse.Ok(_networkSource()),
        _ => RemoteControlResponse.Fail($"未知指令: {command}")
    };

    // ==================== 真实数据源 ====================

    private static SystemStatusSnapshot BuildStatusSnapshot()
    {
        var memory = SystemMetricsHelper.GetMemoryInfo();
        var disks = new List<DiskInfo>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                var space = SystemInfoHelper.GetDriveSpace(drive.Name);
                if (space == null) continue;
                disks.Add(new DiskInfo
                {
                    Name = drive.Name.TrimEnd('\\'),
                    FreeGB = Math.Round(space.Value.free / 1024d / 1024d / 1024d, 1),
                    TotalGB = Math.Round(space.Value.total / 1024d / 1024d / 1024d, 1)
                });
            }
        }
        catch (Exception)
        {
            // 磁盘枚举失败不阻塞整个快照（热插拔竞态，吸取 NetworkInfoTool P2-5 教训）
        }

        return new SystemStatusSnapshot
        {
            CpuPercent = SystemMetricsHelper.GetCpuUsagePercent(),
            MemoryTotalGB = memory.HasValue ? Math.Round(memory.Value.TotalGB, 1) : null,
            MemoryUsedGB = memory.HasValue ? Math.Round(memory.Value.TotalGB - memory.Value.AvailableGB, 1) : null,
            Disks = disks.Count > 0 ? disks : null,
            Uptime = SystemInfoHelper.FormatUptime(SystemInfoHelper.GetUptime()),
            Ipv4 = SystemInfoHelper.GetLocalIPv4()
        };
    }

    private static NetworkDetailSnapshot BuildNetworkSnapshot() => NetworkDetailHelper.Collect();
}
