using Toolbox.Tools.Helpers;

namespace Toolbox.Plugins.Models;

/// <summary>
/// 系统状态快照 —— GET /api/status 返回的 data 结构（字段序列化为 camelCase，见设计文档 5.3）。
/// </summary>
public sealed class SystemStatusSnapshot
{
    /// <summary>CPU 占用百分比（首次采样无基线时为 null）</summary>
    public double? CpuPercent { get; set; }

    public double? MemoryTotalGB { get; set; }

    public double? MemoryUsedGB { get; set; }

    public List<DiskInfo>? Disks { get; set; }

    /// <summary>系统运行时长（如 "3 天 4 小时"）</summary>
    public string? Uptime { get; set; }

    /// <summary>本机局域网 IPv4</summary>
    public string? Ipv4 { get; set; }

    /// <summary>电池信息（笔记本；桌面机 IsBatteryPresent=false，前端隐藏该行）</summary>
    public SystemInfoHelper.BatteryInfo? Battery { get; set; }
}

/// <summary>单个磁盘分区信息</summary>
public sealed class DiskInfo
{
    public string? Name { get; set; }
    public double FreeGB { get; set; }
    public double TotalGB { get; set; }
}

/// <summary>
/// 网络详情快照 —— network 指令返回的 data 结构。
/// </summary>
public sealed class NetworkDetailSnapshot
{
    public string? Mac { get; set; }
    public string? Gateway { get; set; }
    public List<string>? Dns { get; set; }
    public string? PublicIp { get; set; }
}
