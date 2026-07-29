using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Toolbox.Models;
using Toolbox.Tools.Helpers;

namespace Toolbox.Tools;

/// <summary>
/// 本机网络信息面板 —— 展示主机名、每张启用中网卡的详细信息（类型/MAC/IP/网关/DNS），
/// 以及公网 IP（异步请求，双源 fallback 与 5 秒超时策略由 <see cref="SystemInfoHelper"/> 统一实现）。
/// </summary>
public class NetworkInfoTool : ITool
{
    // 配色统一使用 Toolbox.Models.ThemeColors
    // 公网 IP 查询源 / 正则 / 超时统一收敛在 SystemInfoHelper.GetPublicIPv4Async,
    // 与首页仪表盘的网络卡共用同一份实现,改源只需改一处
    private StackPanel? _cardsPanel;
    private TextBlock? _publicIpText;
    private Button? _publicIpCopyButton;
    private string? _publicIp;

    public string Name => "网络信息";
    public string Description => "查看主机名、各网卡的 IP/MAC/网关/DNS，以及公网 IP。";
    public string Category => ToolCategory.Network;
    public string IconGlyph => "📡";

    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        // 说明文字
        var desc = new TextBlock
        {
            Text = "展示本机主机名、启用中的网卡信息和公网 IP，每项均可单独复制。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
            Margin = new Thickness(0, 0, 0, 16)
        };

        // 刷新按钮
        var refreshButton = new Button
        {
            Content = "🔄 刷新",
            FontSize = 14,
            Padding = new Thickness(14, 6, 14, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12)
        };
        refreshButton.Click += (_, _) => LoadAll();

        _cardsPanel = new StackPanel();

        panel.Children.Add(desc);
        panel.Children.Add(refreshButton);
        panel.Children.Add(_cardsPanel);

        // 打开时自动加载一次
        LoadAll();

        // 不再包 ScrollViewer：主窗口 ContentScrollViewer 已负责整体滚动，
        // 内层 ScrollViewer 会吞掉子元素上的滚轮事件并干扰发光层的视口裁剪
        return panel;
    }

    /// <summary>重新加载全部信息（本机信息同步读取，公网 IP 异步请求）</summary>
    private void LoadAll()
    {
        _cardsPanel!.Children.Clear();

        // ====== 主机名卡片 ======
        var hostCard = BuildCard("主机名");
        AddInfoRow((StackPanel)hostCard.Child, "主机名", Environment.MachineName);
        hostCard.Margin = new Thickness(0, 0, 0, 12);
        _cardsPanel.Children.Add(hostCard);

        // ====== 公网 IP 卡片（异步，加载中/失败/成功三态）======
        var publicCard = BuildCard("公网 IP");
        var publicInner = (StackPanel)publicCard.Child;

        var ipRow = new StackPanel { Orientation = Orientation.Horizontal };
        _publicIpText = new TextBlock
        {
            Text = "正在获取公网 IP…",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
            VerticalAlignment = VerticalAlignment.Center
        };
        _publicIpCopyButton = new Button
        {
            Content = "复制",
            FontSize = 12,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = false
        };
        _publicIpCopyButton.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_publicIp))
                TryCopyToClipboard(_publicIp, _publicIpText!);
        };
        ipRow.Children.Add(_publicIpText);
        ipRow.Children.Add(_publicIpCopyButton);
        publicInner.Children.Add(ipRow);

        publicCard.Margin = new Thickness(0, 0, 0, 12);
        _cardsPanel.Children.Add(publicCard);

        // ====== 每张启用中的网卡一张卡片 ======
        foreach (var nic in GetActiveInterfaces())
            _cardsPanel.Children.Add(BuildNicCard(nic));

        // 异步获取公网 IP（不卡 UI 线程，失败不影响本机信息）
        _ = LoadPublicIpAsync();
    }

    /// <summary>枚举所有启用中（且非回环）的网卡</summary>
    private static IEnumerable<NetworkInterface> GetActiveInterfaces()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            yield return nic;
        }
    }

    /// <summary>构建单张网卡的信息卡片</summary>
    private static Border BuildNicCard(NetworkInterface nic)
    {
        var card = BuildCard(nic.Name);
        var inner = (StackPanel)card.Child;
        var props = nic.GetIPProperties();

        AddInfoRow(inner, "类型", $"{nic.NetworkInterfaceType}（{nic.Speed / 1_000_000} Mbps）");
        AddInfoRow(inner, "MAC", FormatMac(nic.GetPhysicalAddress().ToString()));

        // IPv4 / IPv6 地址（可能多个，逐个一行）
        foreach (var addr in props.UnicastAddresses)
        {
            if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                AddInfoRow(inner, "IPv4", addr.Address.ToString());
            else if (addr.Address.AddressFamily == AddressFamily.InterNetworkV6)
                AddInfoRow(inner, "IPv6", addr.Address.ToString());
        }

        var gateways = props.GatewayAddresses
            .Select(g => g.Address.ToString()).ToList();
        AddInfoRow(inner, "默认网关", gateways.Count > 0 ? string.Join("，", gateways) : "（无）");

        var dns = props.DnsAddresses
            .Select(d => d.ToString()).ToList();
        AddInfoRow(inner, "DNS 服务器", dns.Count > 0 ? string.Join("，", dns) : "（无）");

        card.Margin = new Thickness(0, 0, 0, 12);
        return card;
    }

    /// <summary>向卡片内追加一行信息：标签 + 值 + 复制按钮</summary>
    private static void AddInfoRow(StackPanel parent, string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(labelText, 0);

        var valueText = new TextBlock
        {
            Text = value,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.TextPrimary),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(valueText, 1);

        var copyBtn = new Button
        {
            Content = "复制",
            FontSize = 12,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        copyBtn.Click += (_, _) => TryCopyToClipboard(value, valueText);
        Grid.SetColumn(copyBtn, 2);

        row.Children.Add(labelText);
        row.Children.Add(valueText);
        row.Children.Add(copyBtn);
        parent.Children.Add(row);
    }

    /// <summary>异步获取公网 IP：双源 fallback 与 5s 超时由 SystemInfoHelper 统一实现；
    /// 这里只负责把结果映射到 加载中/成功/失败 三态 UI</summary>
    private async Task LoadPublicIpAsync()
    {
        _publicIp = await SystemInfoHelper.GetPublicIPv4Async();

        if (_publicIp is not null)
        {
            _publicIpText!.Text = _publicIp;
            _publicIpText.Foreground = new SolidColorBrush(ThemeColors.Success);
            _publicIpCopyButton!.IsEnabled = true;
            return;
        }

        // 所有源都失败：仅提示失败，不影响本机信息展示
        _publicIpText!.Text = "获取失败（请检查网络连接）";
        _publicIpText.Foreground = new SolidColorBrush(ThemeColors.Danger);
        _publicIpCopyButton!.IsEnabled = false;
    }

    /// <summary>把无分隔符的 MAC 字符串格式化为 AA:BB:CC:DD:EE:FF</summary>
    private static string FormatMac(string raw)
    {
        if (raw.Length != 12) return string.IsNullOrEmpty(raw) ? "（无）" : raw;
        return string.Join(":", Enumerable.Range(0, 6).Select(i => raw.Substring(i * 2, 2)));
    }

    /// <summary>复制文本到剪贴板，结果反馈到指定文本块的颜色上</summary>
    private static void TryCopyToClipboard(string text, TextBlock feedback)
    {
        try
        {
            Clipboard.SetText(text);
            feedback.Foreground = new SolidColorBrush(ThemeColors.Success);
        }
        catch
        {
            feedback.Foreground = new SolidColorBrush(ThemeColors.Danger);
        }
    }

    /// <summary>构建分组卡片：深灰圆角容器 + 组标题，内容随后追加；
    /// 卡片带 GlowCardMarker 标记，纳入鼠标光照发光目标</summary>
    private static Border BuildCard(string title)
    {
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.TextPrimary),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var card = new Border
        {
            Background = new SolidColorBrush(ThemeColors.BgDark),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = inner
        };
        GlowCardMarker.SetIsGlowCard(card, true);
        return card;
    }
}
