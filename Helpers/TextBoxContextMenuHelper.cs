using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Toolbox.Helpers;

/// <summary>
/// TextBox 主题右键菜单：全局替换 WPF 默认的灰色系统编辑菜单。
/// App 启动时调用 <see cref="Register"/> 一次性注册类处理器，
/// 对主窗口及所有插件窗口中的 TextBox 生效（PasswordBox 不在范围内）。
/// </summary>
public static class TextBoxContextMenuHelper
{
    /// <summary>总开关：置 false 可一键恢复 WPF 默认系统菜单。
    /// （不用 const 是避免开启时编译器报"检测到无法访问的代码"警告）</summary>
    private static readonly bool Enabled = true;

    // ── 菜单文案（集中在此便于后续微调）──
    private const string TextCut = "剪切";
    private const string TextCopy = "复制";
    private const string TextPaste = "粘贴";
    private const string TextSelectAll = "全选";

    /// <summary>注册 TextBox 类级右键拦截，App 启动时调用一次。</summary>
    public static void Register()
    {
        if (!Enabled) return;

        // 在隧道阶段（Preview）拦截右键抬起并标记 Handled：
        // 可同时抑制冒泡的 MouseRightButtonUp 与 TextBox 默认系统菜单，
        // 且不影响左键点击与键盘操作（Ctrl+C / Shift+Insert 等走命令路径）
        EventManager.RegisterClassHandler(
            typeof(TextBox),
            UIElement.PreviewMouseRightButtonUpEvent,
            new MouseButtonEventHandler(OnTextBoxPreviewMouseRightButtonUp));
    }

    private static void OnTextBoxPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox tb) return;

        e.Handled = true;

        Core.Controls.ThemedMenuWindow.ShowAt(
            tb.PointToScreen(Mouse.GetPosition(tb)),
            BuildItems(tb));
    }

    private static Core.Controls.ThemedMenuWindow.Item[] BuildItems(TextBox tb)
    {
        bool hasSelection = tb.SelectionLength > 0;
        bool canEdit = !tb.IsReadOnly;

        return new[]
        {
            new Core.Controls.ThemedMenuWindow.Item
            {
                Text = TextCut,
                IsEnabled = hasSelection && canEdit,
                Action = tb.Cut
            },
            new Core.Controls.ThemedMenuWindow.Item
            {
                Text = TextCopy,
                // 复制在只读时也可用
                IsEnabled = hasSelection,
                Action = tb.Copy
            },
            new Core.Controls.ThemedMenuWindow.Item
            {
                Text = TextPaste,
                IsEnabled = canEdit && ClipboardHasText(),
                Action = tb.Paste
            },
            Core.Controls.ThemedMenuWindow.Item.Separator(),
            new Core.Controls.ThemedMenuWindow.Item
            {
                Text = TextSelectAll,
                IsEnabled = tb.Text.Length > 0,
                Action = tb.SelectAll
            }
        };
    }

    /// <summary>剪贴板查询可能因被其他程序占用而抛异常，失败时按"无文本"处理。</summary>
    private static bool ClipboardHasText()
    {
        try { return Clipboard.ContainsText(); }
        catch { return false; }
    }
}
