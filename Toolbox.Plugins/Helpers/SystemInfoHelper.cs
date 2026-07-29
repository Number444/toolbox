using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Toolbox.Tools.Helpers;

/// <summary>
/// 轻量系统信息读取 —— 内存占用 / 系统运行时长 / 磁盘空间 / 本机 IPv4。
/// 供「首页」仪表盘使用;全部同步快速返回,不做网络请求。
/// </summary>
public static class SystemInfoHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;        // 0-100 已用百分比
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    /// <summary>物理内存已用百分比(0-100);读取失败返回 null</summary>
    public static int? GetMemoryUsagePercent()
    {
        try
        {
            var status = new MemoryStatusEx();
            return GlobalMemoryStatusEx(status) ? (int)status.MemoryLoad : null;
        }
        catch { return null; }
    }

    /// <summary>系统运行时长(自开机起)</summary>
    public static TimeSpan GetUptime() => TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>磁盘空间(字节);驱动器不存在返回 null</summary>
    public static (long free, long total)? GetDriveSpace(string driveName = "C:")
    {
        try
        {
            var drive = new DriveInfo(driveName);
            if (!drive.IsReady) return null;
            return (drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch { return null; }
    }

    /// <summary>本机 IPv4 地址：优先 WLAN/以太网卡（跳过虚拟网卡），找不到再退到其他有网关的网卡；无可用网卡返回 null</summary>
    public static string? GetLocalIPv4()
    {
        try
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces();
            // 第一遍只要物理上网卡（WLAN/以太网），第二遍兜底任意有网关卡
            return FindIPv4(nics, physicalOnly: true) ?? FindIPv4(nics, physicalOnly: false);
        }
        catch { return null; }
    }

    // 常见虚拟网卡描述关键词（Radmin/VMware/Hyper-V/VirtualBox/TAP 类 VPN 适配器等），
    // 第一遍物理网卡扫描时按描述排除——这类适配器常伪装成 Ethernet 且带网关
    private static readonly string[] VirtualNicKeywords =
        { "radmin", "vmware", "virtualbox", "hyper-v", "vethernet", "tap-", "zerotier", "tailscale", "wireguard", "openvpn" };

    private static string? FindIPv4(NetworkInterface[] nics, bool physicalOnly)
    {
        foreach (var nic in nics)
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback) continue;
            if (physicalOnly)
            {
                if (nic.NetworkInterfaceType is not (
                    NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet
                    or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetFx
                    or NetworkInterfaceType.FastEthernetT)) continue;
                var desc = nic.Description.ToLowerInvariant();
                if (VirtualNicKeywords.Any(desc.Contains)) continue;
            }

            var props = nic.GetIPProperties();
            if (props.GatewayAddresses.Count == 0) continue; // 无网关的多为虚拟网卡

            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    return addr.Address.ToString();
            }
        }
        return null;
    }

    // 公网 IP 查询源（与 NetworkInfoTool 同策略）：首选 4.ipw.cn（纯文本 IPv4），
    // 备用 myip.ipip.net（描述文本，正则提取）——单一源在国内网络下不稳，必须带 fallback
    private static readonly string[] PublicIpSources = { "https://4.ipw.cn", "https://myip.ipip.net" };
    private static readonly System.Text.RegularExpressions.Regex Ipv4Regex =
        new(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Net.Http.HttpClient PublicIpHttp = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>异步获取公网 IPv4（双源 fallback，5s 超时）；全部失败返回 null</summary>
    public static async Task<string?> GetPublicIPv4Async()
    {
        foreach (var source in PublicIpSources)
        {
            try
            {
                var body = await PublicIpHttp.GetStringAsync(source);
                var match = Ipv4Regex.Match(body);
                if (match.Success) return match.Value;
            }
            catch { /* 换下一个源 */ }
        }
        return null;
    }

    /// <summary>格式化运行时长,如 "3 天 5 小时" / "2 小时 14 分钟"</summary>
    public static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays} 天 {uptime.Hours} 小时";
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours} 小时 {uptime.Minutes} 分钟";
        return $"{uptime.Minutes} 分钟";
    }

    /// <summary>格式化字节数为 GB(保留 0 位小数,如 "128 GB")</summary>
    public static string FormatGb(long bytes) => $"{bytes / 1024d / 1024d / 1024d:F0} GB";
}
