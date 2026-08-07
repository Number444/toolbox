using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Toolbox.Models;
using Toolbox.Plugins.Handlers;
using Toolbox.Plugins.Helpers;
using Toolbox.Plugins.Services;

namespace Toolbox.Tools;

/// <summary>
/// 局域网远程控制工具：浏览器访问控制页，远程关机/锁屏/查看状态（设计文档 docs/REMOTE_CONTROL_TOOL_DESIGN.md）。
/// 服务默认关闭，用户显式点"启动"才监听；状态灯绑定 RemoteControlServer.IsRunning（唯一事实源）。
/// </summary>
public class RemoteControlTool : ITool
{
    private readonly RemoteControlServer _server;

    private TextBlock? _statusLight;
    private TextBox? _portBox;
    private TextBox? _tokenBox;
    private Button? _startButton;
    private Button? _stopButton;
    private TextBlock? _tokenDisplay;
    private Button? _copyTokenButton;
    private Button? _copyUrlButton;
    private TextBlock? _urlList;
    private TextBlock? _statusBlock;

    public string Name => "远程控制";
    public string Description => "局域网内用浏览器远程控制本机（关机/锁屏/状态查看），需先启动服务并输入 Token。";
    public string Category => ToolCategory.System;
    public string IconGlyph => "🛰️";

    public RemoteControlTool()
    {
        _server = new RemoteControlServer(
            new TcpHttpServer(),
            new PowerCommandHandler(),
            new StatusCommandHandler());
    }

    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        // ① 说明文字
        panel.Children.Add(new TextBlock
        {
            Text = "启动服务后，同一局域网内的手机/电脑用浏览器访问下方地址，输入 Token 即可远程控制本机。服务仅在你显式启动时监听。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
            Margin = new Thickness(0, 0, 0, 16)
        });

        // ② 服务配置卡片
        var configCard = BuildCard("服务配置");
        var configInner = (StackPanel)configCard.Child;
        configInner.Children.Add(AddConfigRow("状态", _statusLight = new TextBlock { FontSize = 14 }));
        configInner.Children.Add(AddConfigRow("端口", BuildPortBox()));
        configInner.Children.Add(AddConfigRow("Token", BuildTokenBox()));
        configInner.Children.Add(BuildButtonRow());
        configCard.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(configCard);

        // ③ 访问信息卡片
        var accessCard = BuildCard("访问信息");
        var accessInner = (StackPanel)accessCard.Child;
        accessInner.Children.Add(AddConfigRow("当前 Token", _tokenDisplay = new TextBlock
        {
            Text = "当前 Token：—",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        }));
        accessInner.Children.Add(AddConfigRow("访问地址", _urlList = new TextBlock
        {
            Text = "（未启动）",
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
            TextWrapping = TextWrapping.Wrap
        }));

        // 复制访问地址（FR-1）：取列表第一行
        _copyUrlButton = new Button
        {
            Content = "📋 复制地址",
            FontSize = 13,
            Padding = new Thickness(10, 4, 10, 4),
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(72, 0, 0, 10),
            IsEnabled = false
        };
        _copyUrlButton.Click += (_, _) => CopyAccessUrl();
        accessInner.Children.Add(_copyUrlButton);

        accessCard.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(accessCard);

        // ④ 状态文字（固定在底部）
        _statusBlock = new TextBlock
        {
            Text = "",
            FontSize = 13,
            Margin = new Thickness(0, 12, 0, 0)
        };
        panel.Children.Add(_statusBlock);

        // 服务生命周期：切换工具/关闭时自动停止（主窗口内容缓存复用，实例可能一直存活）
        panel.Unloaded += (_, _) => _server.Stop();

        RefreshUi();
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

    private TextBox BuildPortBox()
    {
        _portBox = new TextBox
        {
            Text = "8090",
            Width = 90,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        return _portBox;
    }

    private TextBox BuildTokenBox()
    {
        _tokenBox = new TextBox
        {
            Text = "",
            Width = 260,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "留空则自动生成随机 Token（不落盘）；手动指定仅当前会话有效"
        };
        return _tokenBox;
    }

    private StackPanel BuildButtonRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(72, 4, 0, 0) };

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

        _copyTokenButton = new Button
        {
            Content = "📋 复制 Token",
            FontSize = 13,
            Padding = new Thickness(10, 4, 10, 4),
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(10, 0, 0, 0),
            IsEnabled = false
        };
        _copyTokenButton.Click += (_, _) => CopyToken();

        row.Children.Add(_startButton);
        row.Children.Add(_stopButton);
        row.Children.Add(_copyTokenButton);
        return row;
    }

    // ==================== 服务操作 ====================

    private void StartService()
    {
        try
        {
            if (_server.IsRunning) { RefreshUi(); return; } // 幂等

            if (!int.TryParse(_portBox!.Text.Trim(), out var port) || port is < 1 or > 65535)
            {
                SetStatus("⚠️ 端口须为 1~65535 的数字", ThemeColors.Warning);
                return;
            }

            var token = string.IsNullOrWhiteSpace(_tokenBox!.Text) ? null : _tokenBox.Text.Trim();
            _server.Start(port, token); // 端口冲突时抛异常 → 下方 catch 提示
            SetStatus("✅ 服务已启动，请在浏览器打开上方地址并输入 Token", ThemeColors.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"❌ 启动失败：{ex.Message}", ThemeColors.Danger);
        }
        finally
        {
            RefreshUi(); // 状态灯始终以 IsRunning 为唯一事实源
        }
    }

    private void StopService()
    {
        try
        {
            _server.Stop();
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

    private void CopyToken()
    {
        try
        {
            var token = _server.Token;
            if (string.IsNullOrEmpty(token)) return;
            Clipboard.SetText(token);
            SetStatus("✅ Token 已复制", ThemeColors.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"❌ 复制失败：{ex.Message}", ThemeColors.Danger);
        }
    }

    private void CopyAccessUrl()
    {
        try
        {
            var firstLine = _urlList!.Text.Split('\n')[0].Trim();
            if (string.IsNullOrEmpty(firstLine) || firstLine.StartsWith("未检测")) return;
            Clipboard.SetText(firstLine);
            SetStatus("✅ 访问地址已复制", ThemeColors.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"❌ 复制失败：{ex.Message}", ThemeColors.Danger);
        }
    }

    /// <summary>刷新全部状态控件（状态灯/按钮可用性/Token/地址），以 IsRunning 为唯一事实源</summary>
    private void RefreshUi()
    {
        var running = _server.IsRunning;

        _statusLight!.Text = running ? "● 运行中" : "● 已停止";
        _statusLight.Foreground = new SolidColorBrush(running ? ThemeColors.Success : ThemeColors.Danger);

        _startButton!.IsEnabled = !running;
        _stopButton!.IsEnabled = running;
        _copyTokenButton!.IsEnabled = running;
        _copyUrlButton!.IsEnabled = running;
        _portBox!.IsEnabled = !running;  // 运行中端口/Token 锁输入（改动须停止后生效）
        _tokenBox!.IsEnabled = !running;
        _tokenDisplay!.Text = running ? $"当前 Token：{_server.Token}" : "当前 Token：—";

        if (running)
        {
            var ips = LanAddressHelper.GetLanIPv4s();
            _urlList!.Text = ips.Count == 0
                ? "未检测到局域网 IP（检查网卡/网关/防火墙）"
                : string.Join("\n", ips.Select(ip => LanAddressHelper.FormatAccessUrl(ip, _server.ActualPort)));
        }
        else
        {
            _urlList!.Text = "（未启动）";
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
