using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Toolbox.Models;

namespace Toolbox.Tools;

/// <summary>
/// 强制删除被占用文件 —— 使用 MoveFileEx 标记重启后删除
/// </summary>
public class ForceDeleteTool : ITool
{
    private TextBox? _pathBox;
    private TextBlock? _resultBlock;
    private ListBox? _pendingList;
    private readonly List<string> _pendingFiles = new();
    private readonly ObservableCollection<string> _pendingItems = new();

    public string Name => "强制删除被占用文件";
    public string Description => "删除被其他程序锁定的文件，标记为重启后删除。";
    public string Category => Toolbox.Models.ToolCategory.File;
    public string IconGlyph => "🗑️";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, uint dwFlags);

    private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

    // ===== 可测试的公开方法 =====

    /// <summary>
    /// 为指定文件生成唯一的临时脚本路径（每个文件独立，避免竞态）
    /// </summary>
    public static string GetTempScriptPath(string filePath)
    {
        string safeName = Path.GetFileNameWithoutExtension(filePath)
            .Replace(" ", "_")
            .Replace(".", "_");
        if (safeName.Length > 20)
            safeName = safeName[..20];
        string guid = Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(Path.GetTempPath(), $"toolbox_fd_{safeName}_{guid}.ps1");
    }

    /// <summary>
    /// 尝试调用 MoveFileEx 标记重启后删除，返回是否成功。
    /// 失败时（通常因权限不足）以提权子进程重试，返回最终结果。
    /// </summary>
    public static bool TryMoveFileEx(string filePath)
    {
        // 尝试直接调用
        if (MoveFileEx(filePath, null, MOVEFILE_DELAY_UNTIL_REBOOT))
            return true;

        int error = Marshal.GetLastWin32Error();
        if (error != 5) // 不是 ACCESS_DENIED → 真正的失败
            return false;

        // 权限不足 → 通过提权子进程重试
        return RunElevatedMoveFileEx(filePath);
    }

    /// <summary>
    /// 通过提权子进程执行 MoveFileEx（触发 UAC）
    /// </summary>
    private static bool RunElevatedMoveFileEx(string filePath)
    {
        string scriptPath = GetTempScriptPath(filePath);
        try
        {
            File.WriteAllText(scriptPath,
                "Add-Type -TypeDefinition @\"\n" +
                "using System.Runtime.InteropServices;\n" +
                "public class NativeMethods {\n" +
                "    [DllImport(\"kernel32.dll\", CharSet = CharSet.Unicode, SetLastError = true)]\n" +
                "    public static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, uint dwFlags);\n" +
                "}\n" +
                "\"@;\n" +
                $"[NativeMethods]::MoveFileEx('{filePath.Replace("'", "''")}', $null, 4);\n");

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return false;

            proc.WaitForExit(30000); // 30s 超时（提权+PWsh加载可能较慢）

            if (!proc.HasExited)
            {
                proc.Kill();
                return false;
            }

            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            // 确保临时脚本被清理
            try { if (File.Exists(scriptPath)) File.Delete(scriptPath); }
            catch { /* 清理失败不影响主逻辑 */ }
        }
    }

    // ===== UI =====

    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        var desc = new TextBlock
        {
            Text = "选择被占用的文件，标记为重启后自动删除。需要管理员权限。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            Margin = new Thickness(0, 0, 0, 16)
        };

        var warning = new TextBlock
        {
            Text = "⚠️ 此操作将在下次重启时生效，请确认不再需要该文件。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x30)),
            Margin = new Thickness(0, 0, 0, 12)
        };

        // 文件路径输入框（全宽，直接作为根面板的子元素）
        _pathBox = new TextBox
        {
            Height = 32,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsReadOnly = true,
            Text = "拖入文件或点击选择...",
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            Margin = new Thickness(0, 0, 0, 8)
        };

        // 文件选择按钮行（位于 pathBox 下方）
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var browseButton = new Button
        {
            Content = "📂 选择文件",
            FontSize = 14,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 8, 0)
        };

        var addButton = new Button
        {
            Content = "➕ 加入列表",
            FontSize = 14,
            Padding = new Thickness(8)
        };

        // 支持拖拽
        _pathBox.AllowDrop = true;
        _pathBox.DragEnter += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
        };
        _pathBox.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                _pathBox.Text = files[0];
                _pathBox.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
            }
        };

        browseButton.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择要强制删除的文件" };
            if (dialog.ShowDialog() == true)
            {
                _pathBox.Text = dialog.FileName;
                _pathBox.Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
            }
        };

        addButton.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(_pathBox.Text) || _pathBox.Text == "拖入文件或点击选择...")
            {
                _resultBlock!.Text = "⚠️ 请先选择文件";
                _resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x40, 0x40));
                return;
            }

            string path = _pathBox.Text;
            if (!File.Exists(path))
            {
                _resultBlock!.Text = $"⚠️ 文件不存在：{path}";
                _resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x40, 0x40));
                return;
            }

            if (!_pendingFiles.Contains(path))
            {
                _pendingFiles.Add(path);
                _pendingItems.Add(path);
                _resultBlock!.Text = $"✅ 已加入待删除列表 ({_pendingItems.Count} 项)";
                _resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0xA0, 0x20));
            }
        };

        buttonRow.Children.Add(browseButton);
        buttonRow.Children.Add(addButton);

        // 待删除列表
        var listLabel = new TextBlock
        {
            Text = "待删除文件列表：",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            Margin = new Thickness(0, 0, 0, 4)
        };

        _pendingList = new ListBox
        {
            Height = 120,
            ItemsSource = _pendingItems,
            Margin = new Thickness(0, 0, 0, 12),
            FontSize = 13
        };

        // 操作按钮行
        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var deleteButton = new Button
        {
            Content = "🗑️ 标记重启后删除",
            FontSize = 14,
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x40, 0x40)),
            Foreground = Brushes.White
        };

        // 修复 1：清除按钮使用 🔄 而非 🗑️（避免图标混淆）
        var clearButton = new Button
        {
            Content = "🔄 清除列表",
            FontSize = 14,
            Padding = new Thickness(10),
            Margin = new Thickness(8, 0, 0, 0)
        };

        _resultBlock = new TextBlock
        {
            Text = "",
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };

        // 修复 2：删除按钮使用 TryMoveFileEx 正确计数
        deleteButton.Click += (_, _) =>
        {
            if (_pendingFiles.Count == 0)
            {
                _resultBlock.Text = "⚠️ 待删除列表为空";
                _resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x40, 0x40));
                return;
            }

            var result = MessageBox.Show(
                "标记的文件将在下次重启后自动删除。确认操作？\n\n需要管理员权限，将弹出 UAC 对话框。",
                "确认强制删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            int success = 0, failed = 0;
            var errors = new List<string>();

            foreach (var filePath in _pendingFiles)
            {
                try
                {
                    // 修复 2：只在确认成功后 +1（不再在提权调用前预增）
                    if (TryMoveFileEx(filePath))
                    {
                        success++;
                    }
                    else
                    {
                        failed++;
                        errors.Add($"{Path.GetFileName(filePath)}: UAC 被取消或执行失败");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            if (failed == 0)
            {
                _resultBlock.Text = $"✅ 已标记 {success} 个文件为重启后删除（重启后生效）";
                _resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0xA0, 0x20));
                _pendingFiles.Clear();
                _pendingItems.Clear();
            }
            else
            {
                _resultBlock.Text = $"⚠️ 成功 {success} 个，失败 {failed} 个：{string.Join("; ", errors)}";
                _resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x40, 0x40));
            }
        };

        // 修复 3：清除列表使用 🔄 图标
        clearButton.Click += (_, _) =>
        {
            _pendingFiles.Clear();
            _pendingItems.Clear();
            _resultBlock.Text = "✅ 已清除待删除列表";
            _resultBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0xA0, 0x20));
        };

        actionRow.Children.Add(deleteButton);
        actionRow.Children.Add(clearButton);

        panel.Children.Add(desc);
        panel.Children.Add(warning);
        panel.Children.Add(_pathBox);
        panel.Children.Add(buttonRow);
        panel.Children.Add(listLabel);
        panel.Children.Add(_pendingList);
        panel.Children.Add(actionRow);
        panel.Children.Add(_resultBlock);

        return panel;
    }
}