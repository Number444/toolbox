using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Toolbox.Models;
using Toolbox.Tools.Helpers;
using Toolbox.Tools.Views;

namespace Toolbox.Tools;

/// <summary>
/// 首页仪表盘 —— 时间/运行时长主卡 + 磁盘/内存/网络/播放状态小卡 + 快捷操作。
/// 设计纪律:只用 ThemeColors 与现有卡片样式;定时器随页面显隐启停,
/// 切走页面后零后台消耗;播放信息被动窥探(MusicFloatWindowManager.PeekNowPlaying),
/// 不主动拉起 SMTC 监听;公网/内网 IP 与系统信息统一走 SystemInfoHelper。
/// </summary>
public class HomeDashboardTool : ITool
{
    public string Name => "系统总览";
    // 描述留空:首页内容即自解释,主窗口头部对空描述自动折叠(不显示简介行)
    public string Description => "";
    public string Category => ToolCategory.Home;
    public string IconGlyph => "📊";

    private DispatcherTimer? _clockTimer;   // 1s:时钟 + 播放卡
    private DispatcherTimer? _statsTimer;   // 30s:磁盘/内存/运行时长

    private TextBlock? _timeText;
    private TextBlock? _dateText;
    private TextBlock? _uptimeText;
    private TextBlock? _diskValueText;
    private TextBlock? _diskSubText;
    private TextBlock? _memValueText;
    private TextBlock? _localIpText;
    private TextBlock? _publicIpText;
    private TextBlock? _musicTitleText;
    private TextBlock? _musicSubText;

    private bool _publicIpFetched;          // 每次可见周期只请求一次公网 IP

    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        panel.Children.Add(BuildHeroCard());
        panel.Children.Add(BuildActionsCard());
        panel.Children.Add(BuildStatGrid());

        // 定时器随页面显隐启停:切走即停,零后台消耗(状态隔离铁律)
        panel.IsVisibleChanged += (_, _) =>
        {
            if (panel.IsVisible) StartTimers();
            else StopTimers();
        };
        panel.Unloaded += (_, _) => StopTimers();

        RefreshAll();
        return panel;
    }

    // ==================== 卡片构建 ====================

    /// <summary>主卡:左侧大号时间,右侧纵向放置日期星期与运行时长(左对齐、垂直居中)</summary>
    private Border BuildHeroCard()
    {
        _timeText = new TextBlock
        {
            FontSize = 44,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.TextPrimary),
            VerticalAlignment = VerticalAlignment.Center
        };

        _dateText = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary)
        };
        _uptimeText = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
            Margin = new Thickness(0, 4, 0, 0)
        };

        var sideInfo = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 0, 0)
        };
        sideInfo.Children.Add(_dateText);
        sideInfo.Children.Add(_uptimeText);

        var inner = new Grid();
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_timeText, 0);
        Grid.SetColumn(sideInfo, 1);
        inner.Children.Add(_timeText);
        inner.Children.Add(sideInfo);

        return BuildCard(inner, null);
    }

    /// <summary>主卡下方的快捷操作卡:左侧三个操作按钮(锁屏/关显示器/睡眠),右端红色关机按钮</summary>
    private UIElement BuildActionsCard()
    {
        var leftRow = new StackPanel { Orientation = Orientation.Horizontal };

        Button MakeActionButton(string text) => new()
        {
            Content = text,
            FontSize = 13,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };

        var lockButton = MakeActionButton("🔒 锁定电脑");
        lockButton.Click += (_, _) => SystemPowerHelper.Lock();

        var monitorButton = MakeActionButton("🖥️ 关闭显示器");
        monitorButton.Click += (_, _) => SystemPowerHelper.TurnOffMonitor();

        var sleepButton = MakeActionButton("😴 睡眠");
        sleepButton.Click += (_, _) => SystemPowerHelper.Sleep();

        leftRow.Children.Add(lockButton);
        leftRow.Children.Add(monitorButton);
        leftRow.Children.Add(sleepButton);

        // 分割竖线:位于睡眠与关机的间隔区正中,上下各超出按钮 2px(总长 = 按钮高 + 4)
        var divider = new Border
        {
            Width = 3,
            Background = new SolidColorBrush(ThemeColors.BorderSubtle),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, -2, 0, -2)
        };
        leftRow.Children.Add(divider);

        // 右端:关机(统一红色警示色,点击先弹主题确认弹窗)
        // 拉伸填满剩余空间:左边距 8(与左侧按钮间距一致),右边距 0(与锁定电脑左边距一致)
        var shutdownButton = new Button
        {
            Content = "⏻ 关机",
            FontSize = 13,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(ThemeColors.Danger),
            Foreground = new SolidColorBrush(ThemeColors.TextPrimary),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        shutdownButton.Click += (_, _) =>
        {
            var dlg = new ConfirmDialog(
                "确定要立即关机吗?未保存的工作将丢失。", "关机", "立即关机");
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/s /t 0",
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                });
            }
            catch { /* 关机进程启动失败:静默,系统无任何变化 */ }
        };

        var grid = new Grid();
        // 左列放三个操作按钮(自然宽度),右列给关机按钮拉伸填满
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(leftRow, 0);
        Grid.SetColumn(shutdownButton, 1);
        grid.Children.Add(leftRow);
        grid.Children.Add(shutdownButton);

        return BuildCard(grid, null);
    }

    /// <summary>2×2 小卡网格:磁盘 / 内存 / 网络 / 正在播放</summary>
    private Grid BuildStatGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 磁盘卡(点击跳转 C盘垃圾清理)
        (_diskValueText, _diskSubText) = (MakeValueText(), MakeSubText());
        var diskCard = BuildStatCard("💾 C 盘可用", _diskValueText, _diskSubText, "C盘垃圾清理");
        diskCard.Margin = new Thickness(0, 0, 6, 10);
        Grid.SetRow(diskCard, 0); Grid.SetColumn(diskCard, 0);

        // 内存卡(无对应工具,不可点击)
        _memValueText = MakeValueText();
        var memCard = BuildStatCard("🧠 内存占用", _memValueText, null, null);
        memCard.Margin = new Thickness(6, 0, 0, 10);
        Grid.SetRow(memCard, 0); Grid.SetColumn(memCard, 1);

        // 网络卡(点击跳转 网络信息)
        _localIpText = MakeValueText();
        _localIpText.FontSize = 20; // IP 较长,字号略收
        _publicIpText = MakeSubText();
        var netCard = BuildStatCard("🌐 网络", _localIpText, _publicIpText, "网络信息");
        netCard.Margin = new Thickness(0, 0, 6, 10);
        Grid.SetRow(netCard, 1); Grid.SetColumn(netCard, 0);

        // 播放卡(点击跳转 网易云音乐悬浮窗)
        _musicTitleText = MakeValueText();
        _musicTitleText.FontSize = 16;
        _musicTitleText.TextTrimming = TextTrimming.CharacterEllipsis;
        _musicSubText = MakeSubText();
        var musicCard = BuildStatCard("♪ 正在播放", _musicTitleText, _musicSubText, "网易云音乐悬浮窗");
        musicCard.Margin = new Thickness(6, 0, 0, 10);
        Grid.SetRow(musicCard, 1); Grid.SetColumn(musicCard, 1);

        grid.Children.Add(diskCard);
        grid.Children.Add(memCard);
        grid.Children.Add(netCard);
        grid.Children.Add(musicCard);
        return grid;
    }

    /// <summary>小卡:标签 + 大数字 + 辅助行;navTarget 非空时可点击跳转对应工具</summary>
    private Border BuildStatCard(string label, TextBlock valueText, TextBlock? subText, string? navTarget)
    {
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
            Margin = new Thickness(0, 0, 0, 6)
        });
        inner.Children.Add(valueText);
        if (subText != null) inner.Children.Add(subText);
        return BuildCard(inner, navTarget);
    }

    private Border BuildCard(UIElement content, string? navTarget)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(ThemeColors.BgDark),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = content
        };
        GlowCardMarker.SetIsGlowCard(card, true);

        if (navTarget != null)
        {
            card.Cursor = Cursors.Hand;
            card.MouseLeftButtonDown += (_, _) => ToolNavigation.Request(navTarget);
        }
        return card;
    }

    private static TextBlock MakeValueText() => new()
    {
        FontSize = 24,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(ThemeColors.TextPrimary)
    };

    private static TextBlock MakeSubText() => new()
    {
        FontSize = 12,
        Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
        Margin = new Thickness(0, 4, 0, 0),
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    // ==================== 定时刷新 ====================

    private void StartTimers()
    {
        if (_clockTimer == null)
        {
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => RefreshFast();
        }
        if (_statsTimer == null)
        {
            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _statsTimer.Tick += (_, _) => RefreshSlow();
        }
        _clockTimer.Start();
        _statsTimer.Start();

        RefreshAll();
        FetchPublicIpOnce();
    }

    private void StopTimers()
    {
        _clockTimer?.Stop();
        _statsTimer?.Stop();
        _publicIpFetched = false; // 下次可见时允许重新获取公网 IP
    }

    private void RefreshAll()
    {
        RefreshFast();
        RefreshSlow();
    }

    /// <summary>1s 刷新:时钟 + 播放卡(纯属性读取,代价可忽略)</summary>
    private void RefreshFast()
    {
        var now = DateTime.Now;
        if (_timeText != null) _timeText.Text = now.ToString("HH:mm");
        if (_dateText != null)
        {
            var weekday = "日一二三四五六"[(int)now.DayOfWeek];
            _dateText.Text = $"{now:M月d日} 星期{weekday}";
        }

        RefreshMusicCard();
    }

    /// <summary>30s 刷新:磁盘 / 内存 / 运行时长</summary>
    private void RefreshSlow()
    {
        if (_uptimeText != null)
            _uptimeText.Text = $"已运行 {SystemInfoHelper.FormatUptime(SystemInfoHelper.GetUptime())}";

        // 磁盘:可用 GB + 已用百分比,按占用度着色(>90% 红,>75% 橙)
        var disk = SystemInfoHelper.GetDriveSpace();
        if (disk is var (free, total) && total > 0)
        {
            double usedRatio = 1d - (double)free / total;
            if (_diskValueText != null) _diskValueText.Text = SystemInfoHelper.FormatGb(free);
            if (_diskSubText != null)
            {
                _diskSubText.Text = $"已用 {usedRatio * 100:F0}%";
                _diskSubText.Foreground = new SolidColorBrush(
                    usedRatio >= 0.90 ? ThemeColors.Danger :
                    usedRatio >= 0.75 ? ThemeColors.Warning : ThemeColors.TextSecondary);
            }
        }
        else
        {
            if (_diskValueText != null) _diskValueText.Text = "--";
            if (_diskSubText != null) _diskSubText.Text = "读取失败";
        }

        // 内存
        var mem = SystemInfoHelper.GetMemoryUsagePercent();
        if (_memValueText != null) _memValueText.Text = mem.HasValue ? $"{mem.Value}%" : "--";

        // 网络:内网 IPv4(同步);公网 IP 由 FetchPublicIpOnce 异步填充
        if (_localIpText != null)
            _localIpText.Text = SystemInfoHelper.GetLocalIPv4() ?? "未连接";
    }

    /// <summary>播放卡:被动窥探悬浮窗管理器,绝不触发其实例化</summary>
    private void RefreshMusicCard()
    {
        if (_musicTitleText == null || _musicSubText == null) return;

        var info = MusicFloatWindowManager.PeekNowPlaying();
        if (info?.HasSong == true)
        {
            _musicTitleText.Text = info.Title;
            _musicSubText.Text = string.IsNullOrWhiteSpace(info.Artist) ? " " : info.Artist;
        }
        else
        {
            _musicTitleText.Text = "未在播放";
            _musicSubText.Text = "开启悬浮窗后自动显示";
        }
    }

    /// <summary>每个可见周期异步获取一次公网 IP(双源 fallback,失败静默降级)</summary>
    private async void FetchPublicIpOnce()
    {
        if (_publicIpFetched || _publicIpText == null) return;
        _publicIpFetched = true;
        _publicIpText.Text = "公网 IP 获取中…";

        var ip = await SystemInfoHelper.GetPublicIPv4Async();
        // 页面可能已切走(TextBlock 已被丢弃也无妨,WPF 元素脱离可视树后赋值安全)
        _publicIpText.Text = ip != null ? $"公网 {ip}" : "公网 IP 获取失败";
    }
}
