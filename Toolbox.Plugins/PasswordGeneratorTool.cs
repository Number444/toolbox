using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Toolbox.Models;

namespace Toolbox.Tools;

/// <summary>
/// 随机密码生成器 —— 输入名字作为种子，确定性生成密码（同一名字永远得到同一密码）。
/// 历史记录（名字 + 密码 + 时间）追加保存到 %LOCALAPPDATA%\Toolbox\passwords.json（明文）。
/// </summary>
public class PasswordGeneratorTool : ITool
{
    // 与全局主题（App.xaml）及其它工具一致的配色常量
    private static readonly Color BgCard = Color.FromRgb(0x2D, 0x2D, 0x2D);
    private static readonly Color BgDark = Color.FromRgb(0x1C, 0x1C, 0x1C);
    private static readonly Color TextPrimary = Color.FromRgb(0xF0, 0xF0, 0xF0);
    private static readonly Color TextSecondary = Color.FromRgb(0x80, 0x80, 0x80);
    private static readonly Color Success = Color.FromRgb(0x63, 0xD4, 0x7E);
    private static readonly Color Danger = Color.FromRgb(0xF0, 0x70, 0x70);

    // 可选字符集
    private const string UpperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowerChars = "abcdefghijklmnopqrstuvwxyz";
    private const string DigitChars = "0123456789";
    private const string SymbolChars = "!@#$%^&*-_=+?";

    // 历史记录持久化文件（%LOCALAPPDATA%\Toolbox\passwords.json）
    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Toolbox", "passwords.json");

    private TextBox? _nameBox;
    private ComboBox? _lengthCombo;
    private CheckBox? _upperCheck, _lowerCheck, _digitCheck, _symbolCheck;
    private TextBlock? _passwordBlock;
    private TextBlock? _statusBlock;
    private TextBox? _historySearchBox;
    private StackPanel? _historyPanel;

    // 历史记录搜索关键字（按名字模糊匹配，大小写不敏感）
    private string _historyFilter = "";

    // 内存中的历史记录列表（与 JSON 文件同步）
    private List<PasswordRecord> _history = new();

    public string Name => "密码生成器";
    public string Description => "输入名字作为种子，确定性生成密码：同一名字永远生成同一密码。";
    public string Category => ToolCategory.Text;
    public string IconGlyph => "🔑";

    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        // 说明文字
        var desc = new TextBlock
        {
            Text = "输入名字作为种子，根据所选长度和字符集确定性生成密码；同一名字、同一选项永远得到同一密码。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(TextSecondary),
            Margin = new Thickness(0, 0, 0, 16)
        };

        // ====== 输入卡片：名字 + 长度 + 字符集 + 生成按钮 ======
        _nameBox = new TextBox
        {
            Height = 34,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };

        // 密码长度下拉（默认 16）
        _lengthCombo = new ComboBox
        {
            Width = 90,
            Height = 30,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12)
        };
        foreach (var len in new[] { 8, 12, 16, 20, 24 })
            _lengthCombo.Items.Add(new ComboBoxItem { Content = len.ToString() });
        _lengthCombo.SelectedIndex = 2; // 默认 16

        // 字符集开关（默认全开）
        _upperCheck = BuildCheckBox("大写 (A-Z)");
        _lowerCheck = BuildCheckBox("小写 (a-z)");
        _digitCheck = BuildCheckBox("数字 (0-9)");
        _symbolCheck = BuildCheckBox("符号 (!@#…)");

        var charsetRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };
        charsetRow.Children.Add(_upperCheck);
        charsetRow.Children.Add(_lowerCheck);
        charsetRow.Children.Add(_digitCheck);
        charsetRow.Children.Add(_symbolCheck);

        var generateButton = new Button
        {
            Content = "生成密码",
            FontSize = 14,
            Padding = new Thickness(14, 6, 14, 6),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var inputCard = BuildCard("输入");
        var inputInner = (StackPanel)inputCard.Child;
        inputInner.Children.Add(_nameBox);
        inputInner.Children.Add(_lengthCombo);
        inputInner.Children.Add(charsetRow);
        inputInner.Children.Add(generateButton);
        inputCard.Margin = new Thickness(0, 0, 0, 12);

        // ====== 结果卡片：密码展示（等宽字体）+ 复制按钮 ======
        var passwordBorder = new Border
        {
            Background = new SolidColorBrush(BgDark),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 12)
        };
        _passwordBlock = new TextBlock
        {
            Text = "（尚未生成）",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 18,
            Foreground = new SolidColorBrush(TextSecondary),
            TextWrapping = TextWrapping.Wrap
        };
        passwordBorder.Child = _passwordBlock;

        var copyButton = new Button
        {
            Content = "📋 复制密码",
            FontSize = 14,
            Padding = new Thickness(14, 6, 14, 6),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var resultCard = BuildCard("结果");
        var resultInner = (StackPanel)resultCard.Child;
        resultInner.Children.Add(passwordBorder);
        resultInner.Children.Add(copyButton);
        resultCard.Margin = new Thickness(0, 0, 0, 12);

        // ====== 历史记录卡片（明文提示写在标题里，标题行右侧放"清空全部"按钮）======
        var historyTitleRow = new Grid();
        historyTitleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        historyTitleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var historyTitleText = new TextBlock
        {
            Text = "历史记录（⚠️ 密码以明文保存在本机 passwords.json，请勿在共享电脑上使用）",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(TextPrimary),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(historyTitleText, 0);

        var clearAllButton = new Button
        {
            Content = "清空全部",
            FontSize = 12,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(8, 0, 0, 10),
            VerticalAlignment = VerticalAlignment.Center
        };
        clearAllButton.Click += (_, _) => ClearAllHistory();
        Grid.SetColumn(clearAllButton, 1);

        historyTitleRow.Children.Add(historyTitleText);
        historyTitleRow.Children.Add(clearAllButton);

        // 搜索框：输入即按名字过滤历史记录（默认 TextBox 样式，发光引擎自动收录边缘光照）
        _historySearchBox = new TextBox
        {
            Height = 30,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };
        ToolTipService.SetToolTip(_historySearchBox, "输入名字过滤历史记录");
        _historySearchBox.TextChanged += (_, _) =>
        {
            _historyFilter = _historySearchBox.Text.Trim();
            RefreshHistoryPanel();
        };

        _historyPanel = new StackPanel();
        var historyCard = BuildCard(historyTitleRow);
        var historyInner = (StackPanel)historyCard.Child;
        historyInner.Children.Add(_historySearchBox);
        historyInner.Children.Add(_historyPanel);

        // 状态文字（固定在底部）
        _statusBlock = new TextBlock
        {
            Text = "",
            FontSize = 13,
            Margin = new Thickness(0, 12, 0, 0)
        };

        // 生成按钮点击事件
        generateButton.Click += (_, _) => DoGenerate();

        // 复制按钮
        copyButton.Click += (_, _) =>
        {
            var pwd = _passwordBlock!.Tag as string;
            if (string.IsNullOrEmpty(pwd))
            {
                SetStatus("⚠️ 请先生成密码", Danger);
                return;
            }
            if (TryCopyToClipboard(pwd))
                SetStatus("✅ 已复制到剪贴板", Success);
        };

        panel.Children.Add(desc);
        panel.Children.Add(inputCard);
        panel.Children.Add(resultCard);
        panel.Children.Add(historyCard);
        panel.Children.Add(_statusBlock);

        // 启动时读取历史记录
        LoadHistory();

        // 不再包 ScrollViewer：主窗口 ContentScrollViewer 已负责整体滚动，
        // 内层 ScrollViewer 会吞掉子元素上的滚轮事件并干扰发光层的视口裁剪
        return panel;
    }

    /// <summary>根据当前输入确定性生成密码，并追加到历史记录</summary>
    private void DoGenerate()
    {
        var name = _nameBox!.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            SetStatus("⚠️ 请输入名字", Danger);
            return;
        }

        // 拼接所选字符集
        var charset = new StringBuilder();
        if (_upperCheck!.IsChecked == true) charset.Append(UpperChars);
        if (_lowerCheck!.IsChecked == true) charset.Append(LowerChars);
        if (_digitCheck!.IsChecked == true) charset.Append(DigitChars);
        if (_symbolCheck!.IsChecked == true) charset.Append(SymbolChars);
        if (charset.Length == 0)
        {
            SetStatus("⚠️ 请至少勾选一个字符集", Danger);
            return;
        }

        var length = int.Parse(((ComboBoxItem)_lengthCombo!.SelectedItem).Content!.ToString()!);
        var password = GeneratePassword(name, charset.ToString(), length);

        _passwordBlock!.Text = password;
        _passwordBlock.Foreground = new SolidColorBrush(TextPrimary);
        _passwordBlock.Tag = password; // 复制按钮从这里取真实密码

        // 追加历史记录并保存
        _history.Add(new PasswordRecord
        {
            Name = name,
            Password = password,
            Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });
        SaveHistory();
        RefreshHistoryPanel();

        SetStatus($"✅ 已生成（{length} 位）", Success);
    }

    /// <summary>
    /// 确定性密码生成：对 "名字|字符集|长度" 做 SHA256，把哈希字节逐个映射到字符集；
    /// 字节不够时加盐计数器继续哈希，直到凑满长度。同一输入永远得到同一输出，
    /// 且与 .NET 运行时版本无关（不依赖 Random 的实现细节）。
    /// </summary>
    private static string GeneratePassword(string name, string charset, int length)
    {
        var result = new StringBuilder(length);
        var counter = 0;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{name}|{charset}|{length}"));

        while (result.Length < length)
        {
            foreach (var b in hash)
            {
                if (result.Length >= length) break;
                result.Append(charset[b % charset.Length]);
            }
            counter++;
            hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{name}|{charset}|{length}|{counter}"));
        }
        return result.ToString();
    }

    /// <summary>构建字符集复选框（默认勾选，套用全局 ClassicCheckBoxStyle）</summary>
    private static CheckBox BuildCheckBox(string text) => new()
    {
        Content = text,
        IsChecked = true,
        Style = FindResourceStyle("ClassicCheckBoxStyle"),
        FontSize = 13,
        Foreground = new SolidColorBrush(TextPrimary),
        VerticalContentAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 16, 0)
    };

    /// <summary>从全局资源（App.xaml）按键取样式，取不到时返回 null（控件退回默认外观）</summary>
    private static Style? FindResourceStyle(string key)
    {
        try
        {
            if (Application.Current?.TryFindResource(key) is Style style)
                return style;
        }
        catch { }
        return null;
    }

    /// <summary>从 %LOCALAPPDATA%\Toolbox\passwords.json 读取历史记录</summary>
    private void LoadHistory()
    {
        _history = new List<PasswordRecord>();
        if (File.Exists(HistoryPath))
        {
            try
            {
                var json = File.ReadAllText(HistoryPath);
                _history = JsonSerializer.Deserialize<List<PasswordRecord>>(json) ?? new List<PasswordRecord>();
            }
            catch { /* 文件损坏，忽略，视为无历史记录 */ }
        }
        RefreshHistoryPanel();
    }

    /// <summary>把历史记录整体写回 JSON 文件</summary>
    private void SaveHistory()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
            var json = JsonSerializer.Serialize(_history, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(HistoryPath, json);
        }
        catch (Exception ex)
        {
            SetStatus($"❌ 历史记录保存失败：{ex.Message}", Danger);
        }
    }

    /// <summary>重建历史记录列表 UI（最新一条排在最上面，按搜索关键字过滤）</summary>
    private void RefreshHistoryPanel()
    {
        _historyPanel!.Children.Clear();

        if (_history.Count == 0)
        {
            _historyPanel.Children.Add(new TextBlock
            {
                Text = "暂无记录",
                FontSize = 13,
                Foreground = new SolidColorBrush(TextSecondary)
            });
            return;
        }

        // 按名字模糊匹配（大小写不敏感）；关键字为空时不过滤
        var filtered = string.IsNullOrEmpty(_historyFilter)
            ? _history
            : _history.Where(r => r.Name.Contains(_historyFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        if (filtered.Count == 0)
        {
            _historyPanel.Children.Add(new TextBlock
            {
                Text = $"没有名字包含“{_historyFilter}”的记录",
                FontSize = 13,
                Foreground = new SolidColorBrush(TextSecondary)
            });
            return;
        }

        for (var i = filtered.Count - 1; i >= 0; i--)
        {
            var record = filtered[i];
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameText = new TextBlock
            {
                Text = record.Name,
                FontSize = 13,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(nameText, 0);

            var pwdText = new TextBlock
            {
                Text = record.Password,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(pwdText, 1);

            var timeText = new TextBlock
            {
                Text = record.Time,
                FontSize = 12,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 8, 0)
            };
            Grid.SetColumn(timeText, 2);

            var copyBtn = new Button
            {
                Content = "复制",
                FontSize = 12,
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            copyBtn.Click += (_, _) =>
            {
                if (TryCopyToClipboard(record.Password))
                    SetStatus($"✅ 已复制 {record.Name} 的密码", Success);
            };
            Grid.SetColumn(copyBtn, 3);

            // 删除按钮：先弹自绘确认弹窗，确认后才移除并写回 JSON
            var deleteBtn = new Button
            {
                Content = "删除",
                FontSize = 12,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            deleteBtn.Click += (_, _) => DeleteRecord(record);
            Grid.SetColumn(deleteBtn, 4);

            row.Children.Add(nameText);
            row.Children.Add(pwdText);
            row.Children.Add(timeText);
            row.Children.Add(copyBtn);
            row.Children.Add(deleteBtn);
            _historyPanel.Children.Add(row);
        }
    }

    /// <summary>删除单条历史记录（弹确认弹窗，确认后写回 JSON 并刷新列表）</summary>
    private void DeleteRecord(PasswordRecord record)
    {
        var dlg = new ConfirmDialog(
            $"确定删除“{record.Name}”的这条密码记录吗？删除后不可恢复。",
            "删除历史记录",
            "删除");
        dlg.ShowDialog();
        if (!dlg.Confirmed) return;

        _history.Remove(record);
        SaveHistory();
        RefreshHistoryPanel();
        SetStatus($"✅ 已删除 {record.Name} 的记录", Success);
    }

    /// <summary>清空全部历史记录（弹确认弹窗，确认后写回 JSON 并刷新列表）</summary>
    private void ClearAllHistory()
    {
        if (_history.Count == 0)
        {
            SetStatus("⚠️ 历史记录为空", Danger);
            return;
        }

        var dlg = new ConfirmDialog(
            $"确定清空全部 {_history.Count} 条历史记录吗？此操作不可恢复。",
            "清空历史记录",
            "清空");
        dlg.ShowDialog();
        if (!dlg.Confirmed) return;

        _history.Clear();
        SaveHistory();
        RefreshHistoryPanel();
        SetStatus("✅ 已清空全部历史记录", Success);
    }

    /// <summary>复制文本到剪贴板，失败时给出状态提示</summary>
    private bool TryCopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"❌ 复制失败：{ex.Message}", Danger);
            return false;
        }
    }

    /// <summary>更新底部状态文字</summary>
    private void SetStatus(string text, Color color)
    {
        _statusBlock!.Text = text;
        _statusBlock.Foreground = new SolidColorBrush(color);
    }

    /// <summary>构建分组卡片：深灰圆角容器 + 组标题，内容随后追加；
    /// 卡片带 GlowCardMarker 标记，纳入鼠标光照发光目标</summary>
    private static Border BuildCard(string title) => BuildCard(new TextBlock
    {
        Text = title,
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(TextPrimary),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10)
    });

    /// <summary>构建分组卡片（标题为自定义元素，用于历史卡片标题行带"清空全部"按钮的情况）</summary>
    private static Border BuildCard(UIElement titleContent)
    {
        var inner = new StackPanel();
        inner.Children.Add(titleContent);

        var card = new Border
        {
            Background = new SolidColorBrush(BgCard),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = inner
        };
        GlowCardMarker.SetIsGlowCard(card, true);
        return card;
    }

    /// <summary>历史记录条目（对应 passwords.json 中的一条）</summary>
    private sealed class PasswordRecord
    {
        public string Name { get; set; } = "";
        public string Password { get; set; } = "";
        public string Time { get; set; } = "";
    }
}
