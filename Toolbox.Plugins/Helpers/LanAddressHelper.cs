using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Toolbox.Plugins.Helpers;

/// <summary>
/// ★ 本工具私有（仅 RemoteControlTool 内部使用，不与其他工具共享）：
/// 局域网 IP 列表获取（展示访问地址）+ 访问 URL 格式化。
/// </summary>
public static class LanAddressHelper
{
    /// <summary>
    /// 获取所有"启用 + 有网关 + 非回环"网卡的 IPv4 地址。
    /// 网卡枚举竞态就地捕获，失败返回空列表（不冒泡）。
    /// </summary>
    public static List<string> GetLanIPv4s()
    {
        var result = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var props = nic.GetIPProperties();
                if (props.GatewayAddresses.Count == 0) continue; // 无网关 = 非局域网接入

                foreach (var address in props.UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                        result.Add(address.Address.ToString());
                }
            }
        }
        catch (Exception)
        {
            // 热插拔竞态：返回已收集部分
        }
        return result;
    }

    /// <summary>格式化访问地址（纯函数，可单测）</summary>
    public static string FormatAccessUrl(string ipv4, int port) => $"http://{ipv4}:{port}/";
}
