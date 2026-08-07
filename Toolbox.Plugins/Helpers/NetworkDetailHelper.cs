using System.Net.NetworkInformation;
using System.Net.Sockets;
using Toolbox.Plugins.Models;
using Toolbox.Tools.Helpers;

namespace Toolbox.Plugins.Helpers;

/// <summary>
/// ★ 本工具私有（仅 RemoteControlTool 内部使用，不与其他工具共享）：
/// 局域网网卡详情（MAC/网关/DNS）+ 公网 IP。
/// 公网 IP 复用公共 <see cref="SystemInfoHelper.GetPublicIPv4Async"/>（双源 fallback + 5s 超时，
/// 与"网络信息"工具共用同一份实现——改源只需改一处）。
/// </summary>
public static class NetworkDetailHelper
{
    private static readonly object CacheSync = new();
    private static string? _cachedPublicIp;
    private static DateTime _cacheExpires = DateTime.MinValue;

    /// <summary>公网 IP 缓存时长（30s）：避免每次 network 查询都同步等待外部请求，阻塞指令队列（审查 P2-1）</summary>
    private static readonly TimeSpan PublicIpCacheDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 聚合网络详情。取第一个"启用 + 有网关 + 非回环"的网卡（局域网接入网卡）。
    /// 枚举热插拔竞态就地捕获（吸取 NetworkInfoTool P2-5 教训）。
    /// </summary>
    public static NetworkDetailSnapshot Collect()
    {
        var snapshot = new NetworkDetailSnapshot();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var props = nic.GetIPProperties();
                if (props.GatewayAddresses.Count == 0) continue; // 无网关 = 非局域网接入

                snapshot.Mac = nic.GetPhysicalAddress()?.ToString();
                snapshot.Gateway = props.GatewayAddresses[0].Address.ToString();
                snapshot.Dns = props.DnsAddresses
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.ToString())
                    .ToList();
                break;
            }
        }
        catch (Exception)
        {
            // 网卡枚举竞态：返回部分快照，不冒泡
        }

        // 公网 IP：复用公共 Helper（双源 fallback + 5s 超时），30s 缓存降频外部请求
        snapshot.PublicIp = GetPublicIpCached();

        return snapshot;
    }

    private static string? GetPublicIpCached()
    {
        lock (CacheSync)
        {
            if (DateTime.Now < _cacheExpires) return _cachedPublicIp;
            try
            {
                _cachedPublicIp = SystemInfoHelper.GetPublicIPv4Async().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                _cachedPublicIp = null;
            }
            _cacheExpires = DateTime.Now.Add(PublicIpCacheDuration);
            return _cachedPublicIp;
        }
    }
}
