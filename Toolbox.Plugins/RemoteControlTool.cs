using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Toolbox.Core.Services;
using Toolbox.Models;
using Toolbox.Plugins.Handlers;
using Toolbox.Plugins.Helpers;
using Toolbox.Plugins.Services;

namespace Toolbox.Tools;

/// <summary>
/// 局域网远程控制工具：浏览器访问控制页，远程关机/锁屏/查看状态（设计文档 docs/REMOTE_CONTROL_TOOL_DESIGN.md）。
/// 服务为工具级静态单例（用户决策 2026-08-08：切换工具/前台后台均常驻，仅手动停止或关闭 Toolbox 时终止）；
/// 面板只是控制台，状态灯绑定 RemoteControlServer.IsRunning（唯一事实源）。
/// </summary>
public class RemoteControlTool : ITool
{
    /// <summary>服务静态单例：工具实例随内容缓存重建，但服务必须跨实例存活（常驻）</summary>
    private static readonly RemoteControlServer SharedServer = new(
        new TcpHttpServer(),
        new PowerCommandHandler(),
        new StatusCommandHandler());

    private TextBlock? _statusLight;
    private TextBox? _portBox;
    private TextBox? _keyBox;
    private TextBlock? _keyHint;
    private Button? _startButton;
    private Button? _stopButton;
    private TextBlock? _keyValue;
    private Button? _copyKeyButton;
    private ItemsControl? _urlList;
    private StackPanel? _deviceItems;
    private Button? _refreshDevicesButton;
    private TextBlock? _statusBlock;

    public string Name => "远程控制";
    public string Description => "局域网内用浏览器远程控制本机（关机/锁屏/状态查看），需先启动服务并输入密钥。";
    public string Category => ToolCategory.Network;
    public string IconGlyph => "🛰️";

    public RemoteControlTool()
    {
        // 自动启动（设置页开关）：ToolRegistry 反射实例化本工具时检查，服务常驻不随面板重建
        TryAutoStart();
    }

    /// <summary>最近一次自动启动失败原因（面板可见化，审查 P2-5：失败必带原因）</summary>
    private static string? _lastAutoStartError;

    /// <summary>设置页"启动 Toolbox 时自动启动服务"开关生效点（默认端口/密钥来自 AppSettings）</summary>
    private static void TryAutoStart()
    {
        try
        {
            _lastAutoStartError = null;
            if (!AppSettings.Instance.AutoStartRemoteControl || SharedServer.IsRunning) return;
            var port = int.TryParse(AppSettings.Instance.RemoteControlDefaultPort, out var p) && p is >= 1 and <= 65535
                ? p
                : int.Parse(AppPaths.DefaultRemotePort);
            var key = string.IsNullOrWhiteSpace(AppSettings.Instance.RemoteControlDefaultKey)
                ? null
                : AppSettings.Instance.RemoteControlDefaultKey.Trim();
            SharedServer.Start(port, key);
        }
        catch (Exception ex)
        {
            // 自动启动失败（端口冲突等）不打断工具加载；原因记录供面板展示（用户可手动启动）
            _lastAutoStartError = ex.Message;
            Debug.WriteLine($"[RemoteControlTool] 自动启动失败: {ex.Message}");
        }
    }

    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        // ① 说明文字
        panel.Children.Add(new TextBlock
        {
            Text = "启动服务后，同一局域网内的手机/电脑用浏览器访问下方地址，输入密钥即可远程控制本机。服务启动后常驻（切换工具不停止），仅手动停止或关闭 Toolbox 时终止。注意：密钥与指令为明文 HTTP 传输，请勿在不可信网络（如公共 WiFi）使用。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
            Margin = new Thickness(0, 0, 0, 16)
        });

        // ② 服务配置卡片
        var configCard = BuildCard("服务配置");
        var configInner = (StackPanel)configCard.Child;
        configInner.Children.Add(AddConfigRow("端口", BuildKeyPortBox()));
        configInner.Children.Add(AddConfigRow("密钥", BuildKeyInput()));
        configInner.Children.Add(BuildAutoGenerateRow());
        configInner.Children.Add(BuildButtonRow());
        configInner.Children.Add(AddConfigRow("状态", _statusLight = new TextBlock { FontSize = 14 }));
        configCard.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(configCard);

        // ③ 访问信息卡片
        var accessCard = BuildCard("访问信息");
        var accessInner = (StackPanel)accessCard.Child;
        accessInner.Children.Add(AddConfigRow("当前密钥", _keyValue = new TextBlock
        {
            Text = "—",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        }));

        // 当前密钥与访问地址之间 2px 浅灰分割线
        accessInner.Children.Add(new Border
        {
            Height = 2,
            Background = new SolidColorBrush(ThemeColors.BorderSubtle),
            Margin = new Thickness(0, 2, 0, 10)
        });

        // 访问地址行：标签顶部对齐第一行地址（多行地址时标签不垂直居中）
        accessInner.Children.Add(BuildUrlRowWithLabel());
        accessCard.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(accessCard);

        // ④ 已连接设备卡片
        var deviceCard = BuildCard("已连接设备");
        var deviceInner = (StackPanel)deviceCard.Child;
        _deviceItems = new StackPanel();
        deviceInner.Children.Add(_deviceItems);
        _refreshDevicesButton = new Button
        {
            Content = "🔄 刷新设备",
            FontSize = 13,
            Padding = new Thickness(12, 5, 12, 5),
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0)
        };
        _refreshDevicesButton.Click += (_, _) => RefreshDevices();
        deviceInner.Children.Add(_refreshDevicesButton);
        deviceCard.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(deviceCard);

        // ⑤ 状态文字（固定在底部）
        _statusBlock = new TextBlock
        {
            Text = "",
            FontSize = 13,
            Margin = new Thickness(0, 12, 0, 0)
        };
        panel.Children.Add(_statusBlock);

        RefreshUi();
        RefreshDevices();

        // 自动启动失败原因可见化（服务未运行且存在失败记录时显示）
        if (!SharedServer.IsRunning && _lastAutoStartError != null)
            SetStatus($"⚠️ 自动启动失败：{_lastAutoStartError}（可手动启动）", ThemeColors.Warning);

        return panel;
    }

    // ==================== UI 构建 ====================

    private static StackPanel AddConfigRow(string label, UIElement content)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            Width = 72,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary)
        });
        row.Children.Add(content);
        return row;
    }

    private TextBox BuildKeyPortBox()
    {
        // 初始值优先级：设置页默认端口（十二）→ 上次使用端口（七）
        var defaultValue = AppSettings.Instance.RemoteControlDefaultPort;
        _portBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(defaultValue) ? RemoteControlSettings.Instance.LastPort : defaultValue,
            Width = 90,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        // 失焦时才落盘（避免逐键 3 次文件 IO 写放大，审查 P3-1）
        _portBox.LostFocus += (_, _) => RemoteControlSettings.Instance.LastPort = _portBox.Text.Trim();
        return _portBox;
    }

    private UIElement BuildKeyInput()
    {
        // 初始值优先级：设置页默认密钥（十二）→ 上次使用密钥（七，明文 json）
        var defaultKey = AppSettings.Instance.RemoteControlDefaultKey;
        _keyBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(defaultKey) ? RemoteControlSettings.Instance.LastKey : defaultKey,
            Width = 260,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };

        // 常驻深色提示（WPF TextBox 无原生 placeholder，用透明提示文字叠加实现）
        _keyHint = new TextBlock
        {
            Text = "留空自动生成随机密钥",
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            IsHitTestVisible = false
        };

        var container = new Grid { Width = 260 };
        container.Children.Add(_keyBox);
        container.Children.Add(_keyHint);

        void UpdateHint() =>
            _keyHint!.Visibility = string.IsNullOrEmpty(_keyBox!.Text) && !_keyBox.IsKeyboardFocused
                ? Visibility.Visible
                : Visibility.Collapsed;

        _keyBox.TextChanged += (_, _) => UpdateHint();
        _keyBox.GotKeyboardFocus += (_, _) => _keyHint.Visibility = Visibility.Collapsed;
        _keyBox.LostKeyboardFocus += (_, _) => UpdateHint();
        UpdateHint();

        // 复制密钥按钮：位于密钥输入框右侧（与输入框同一行）
        _copyKeyButton = new Button
        {
            Content = "📋 复制密钥",
            FontSize = 12,
            Padding = new Thickness(10, 4, 10, 4),
            Height = 30,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = false
        };
        _copyKeyButton.Click += (_, _) => CopyKey();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(container);
        row.Children.Add(_copyKeyButton);
        return row;
    }

    private CheckBox BuildAutoGenerateRow()
    {
        var checkBox = new CheckBox
        {
            Style = FindResourceStyle("ClassicCheckBoxStyle"),
            Content = "无密钥时自动生成随机密钥",
            IsChecked = RemoteControlSettings.Instance.AutoGenerateKey,
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.TextPrimary),
            Margin = new Thickness(72, 0, 0, 10)
        };
        checkBox.Checked += (_, _) => RemoteControlSettings.Instance.AutoGenerateKey = true;
        checkBox.Unchecked += (_, _) => RemoteControlSettings.Instance.AutoGenerateKey = false;
        return checkBox;
    }

    private StackPanel BuildButtonRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(72, 4, 0, 10) };

        _startButton = new Button
        {
            Content = "▶️ 启动服务",
            FontSize = 14,
            Padding = new Thickness(14, 6, 14, 6),
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _startButton.Click += (_, _) => StartService();

        _stopButton = new Button
        {
            Content = "⏹ 停止服务",
            FontSize = 14,
            Padding = new Thickness(14, 6, 14, 6),
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(10, 0, 0, 0)
        };
        _stopButton.Click += (_, _) => StopService();

        row.Children.Add(_startButton);
        row.Children.Add(_stopButton);
        return row;
    }

    private UIElement BuildUrlList()
    {
        _urlList = new ItemsControl { FontSize = 13 };
        return _urlList;
    }

    /// <summary>"访问地址"标签 + 地址列表：标签顶部对齐第一行地址文字（Margin 微调对齐基线）</summary>
    private UIElement BuildUrlRowWithLabel()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(new TextBlock
        {
            Text = "访问地址",
            FontSize = 13,
            Width = 72,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 0, 0), // 与首行地址文字基线对齐
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary)
        });
        row.Children.Add(BuildUrlList());
        return row;
    }

    /// <summary>地址行：地址文本 + 独立复制按钮（每行可复制）</summary>
    private static UIElement BuildUrlRow(string url)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var copy = new Button
        {
            Content = "复制",
            FontSize = 12,
            Padding = new Thickness(8, 2, 8, 2),
            Height = 26,
            Margin = new Thickness(8, 0, 0, 0)
        };
        DockPanel.SetDock(copy, Dock.Right); // 附加属性不能对象初始化器
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(url);
            }
            catch (Exception)
            {
                // 剪贴板占用时静默（列表行无状态栏上下文，不打断用户）
            }
        };
        var text = new TextBlock
        {
            Text = url,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(ThemeColors.TextPrimary)
        };
        row.Children.Add(copy);
        row.Children.Add(text);
        return row;
    }

    private static Style? FindResourceStyle(string key)
    {
        try
        {
            if (Application.Current?.TryFindResource(key) is Style style)
                return style;
        }
        catch (Exception) { }
        return null;
    }

    // ==================== 服务操作 ====================

    private void StartService()
    {
        try
        {
            if (SharedServer.IsRunning) { RefreshUi(); return; } // 幂等

            if (!int.TryParse(_portBox!.Text.Trim(), out var port) || port is < 1 or > 65535)
            {
                SetStatus("⚠️ 端口须为 1~65535 的数字", ThemeColors.Warning);
                return;
            }

            // 无密钥永远可启动（免登录模式）；开关控制"是否生成随机密钥"，不拦截启动
            var manualKey = _keyBox!.Text.Trim();
            SharedServer.Start(port,
                string.IsNullOrEmpty(manualKey) ? null : manualKey,
                generateKey: RemoteControlSettings.Instance.AutoGenerateKey);

            // 记录本次使用的端口；密钥仅手动指定时落盘——
            // 免登录（自动生成）的随机密钥不落盘，防止下次输入框回填后误成带密钥启动
            RemoteControlSettings.Instance.LastPort = port.ToString();
            if (!SharedServer.IsNoKeyMode)
                RemoteControlSettings.Instance.LastKey = SharedServer.Token ?? "";

            SetStatus(SharedServer.IsNoKeyMode
                ? "✅ 服务已启动（免登录模式：未指定密钥，局域网内可直接访问）"
                : "✅ 服务已启动，请在浏览器打开下方地址并输入密钥", ThemeColors.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"❌ 启动失败：{ex.Message}", ThemeColors.Danger);
        }
        finally
        {
            RefreshUi();
        }
    }

    private void StopService()
    {
        try
        {
            SharedServer.Stop();
            SetStatus("⏹ 服务已停止", ThemeColors.Warning);
        }
        catch (Exception ex)
        {
            SetStatus($"❌ 停止失败：{ex.Message}", ThemeColors.Danger);
        }
        finally
        {
            RefreshUi();
        }
    }

    private void CopyKey()
    {
        try
        {
            if (SharedServer.IsNoKeyMode)
            {
                SetStatus("ℹ️ 免登录模式无密钥认证，无需复制", ThemeColors.Warning);
                return;
            }
            var key = SharedServer.Token;
            if (string.IsNullOrEmpty(key)) return;
            Clipboard.SetText(key);
            SetStatus("✅ 密钥已复制", ThemeColors.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"❌ 复制失败：{ex.Message}", ThemeColors.Danger);
        }
    }

    // ==================== 设备管理 ====================

    private void RefreshDevices()
    {
        _deviceItems!.Children.Clear();

        // 免登录模式无会话：设备列表恒空且无管理意义，直接说明（审查 P2-4）
        if (SharedServer.IsNoKeyMode)
        {
            _deviceItems.Children.Add(new TextBlock
            {
                Text = "（免登录模式：无会话，设备管理不适用）",
                FontSize = 13,
                Foreground = new SolidColorBrush(ThemeColors.TextSecondary)
            });
            return;
        }

        var connected = SharedServer.ConnectedDevices;
        var known = SharedServer.KnownDevices;

        if (connected.Count == 0 && known.Count == 0)
        {
            _deviceItems.Children.Add(new TextBlock
            {
                Text = "（暂无设备）",
                FontSize = 13,
                Foreground = new SolidColorBrush(ThemeColors.TextSecondary)
            });
            return;
        }

        foreach (var device in connected)
            _deviceItems.Children.Add(BuildDeviceRow(device.DeviceName, device.Ip, $"活跃 {device.LastActive:HH:mm:ss}", connected: true));

        foreach (var device in known.Where(k => !connected.Any(c => c.Ip == k.Ip)))
            _deviceItems.Children.Add(BuildDeviceRow(device.DeviceName, device.Ip, $"首连 {device.FirstSeen:MM-dd HH:mm}", connected: false));
    }

    private UIElement BuildDeviceRow(string name, string ip, string meta, bool connected)
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var kick = new Button
        {
            Content = connected ? "踢出" : "移除",
            FontSize = 12,
            Padding = new Thickness(8, 2, 8, 2),
            Height = 26,
            Margin = new Thickness(8, 0, 0, 0)
        };
        DockPanel.SetDock(kick, Dock.Right); // 附加属性不能对象初始化器
        kick.Click += (_, _) =>
        {
            try
            {
                SharedServer.KickDevice(ip);
                RefreshDevices();
                SetStatus($"✅ 已移除设备 {ip}", ThemeColors.Success);
            }
            catch (Exception ex)
            {
                SetStatus($"❌ 移除失败：{ex.Message}", ThemeColors.Danger);
            }
        };
        var text = new TextBlock
        {
            Text = $"{(connected ? "🟢" : "⚪")} {name} · {ip} · {meta}",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(ThemeColors.TextPrimary)
        };
        row.Children.Add(kick);
        row.Children.Add(text);
        return row;
    }

    // ==================== 状态刷新 ====================

    /// <summary>刷新全部状态控件（状态灯/按钮可用性/密钥/地址），以 IsRunning 为唯一事实源</summary>
    private void RefreshUi()
    {
        var running = SharedServer.IsRunning;

        _statusLight!.Text = running ? "● 运行中" : "● 已停止";
        _statusLight.Foreground = new SolidColorBrush(running ? ThemeColors.Success : ThemeColors.Danger);

        _startButton!.IsEnabled = !running;
        _stopButton!.IsEnabled = running;
        // 免登录模式不展示/不复制密钥（避免"显示密钥但根本不校验"的安全误导，审查 P1-1）
        _copyKeyButton!.IsEnabled = running && !SharedServer.IsNoKeyMode;
        _portBox!.IsEnabled = !running; // 运行中锁定输入（改动须停止后生效）
        _keyBox!.IsEnabled = !running;
        _keyValue!.Text = running
            ? (SharedServer.IsNoKeyMode ? "免登录（无密钥）" : SharedServer.Token ?? "—")
            : "—";

        _urlList!.Items.Clear();
        if (running)
        {
            var ips = LanAddressHelper.GetLanIPv4s();
            if (ips.Count == 0)
            {
                _urlList.Items.Add(new TextBlock
                {
                    Text = "未检测到局域网 IP（检查网卡/网关/防火墙）",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(ThemeColors.TextSecondary)
                });
            }
            else
            {
                foreach (var url in ips.Select(ip => LanAddressHelper.FormatAccessUrl(ip, SharedServer.ActualPort)))
                    _urlList.Items.Add(BuildUrlRow(url));
            }
        }
        else
        {
            _urlList.Items.Add(new TextBlock
            {
                Text = "（未启动）",
                FontSize = 13,
                Foreground = new SolidColorBrush(ThemeColors.TextSecondary)
            });
        }
    }

    private void SetStatus(string text, Color color)
    {
        _statusBlock!.Text = text;
        _statusBlock.Foreground = new SolidColorBrush(color);
    }

    // ==================== 卡片模板（规范 4.2） ====================

    private static Border BuildCard(string title)
    {
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.TextPrimary),
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
