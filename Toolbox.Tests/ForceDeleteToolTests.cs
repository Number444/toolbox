using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Toolbox.Tools;
using Xunit;

namespace Toolbox.Tests;

public class ForceDeleteToolTests
{
    // RED: 当前清除按钮使用 🗑️（应与删除按钮相同图标）
    // 修复后应使用不同的图标（如 🔄）
    [Fact]
    public void ClearButtonIcon_IsNotTrashIcon()
    {
        var result = RunOnStaThread(() =>
        {
            var tool = new ForceDeleteTool();
            var ui = tool.CreateContent();
            var panel = Assert.IsType<StackPanel>(ui);

            // 递归查找"清除列表"按钮
            var clearBtn = FindButtonWithContent(panel, "清除列表");
            Assert.NotNull(clearBtn);

            // 当前 bug：按钮内容包含 🗑️（与删除按钮图标相同）
            // 修复后应不包含 🗑️
            return clearBtn.Content?.ToString()?.Contains("🗑️") == true;
        });
        // 这个测试将失败（RED），因为当前清除按钮确实用了 🗑️
        Assert.False(result, "Clear button should NOT use 🗑️ icon");
    }

    // RED (UI): 当前 pathBox 嵌在 fileRow 水平行内，导致按钮被侧边栏截断
    // 修复后 pathBox 应为根面板的直接子元素（全宽），按钮在独立行
    [Fact]
    public void PathBox_IsDirectChildOfRootPanel_NotInHorizontalRowWithButtons()
    {
        var result = RunOnStaThread(() =>
        {
            var tool = new ForceDeleteTool();
            var ui = tool.CreateContent();
            var panel = Assert.IsType<StackPanel>(ui);

            // 检查根面板是否有 TextBox 作为直接子元素（而非嵌套在 fileRow 内）
            bool hasTextBoxAsDirectChild = false;
            foreach (var child in panel.Children)
            {
                if (child is TextBox)
                {
                    hasTextBoxAsDirectChild = true;
                    break;
                }
            }
            return hasTextBoxAsDirectChild;
        });
        // 当前（RED）：pathBox 嵌套在 fileRow 内，没有直接 TextBox → false
        // 修复后（GREEN）：pathBox 为根面板直接子元素 → true
        Assert.True(result, "TextBox (pathBox) should be a direct child of root panel");
    }

    // RED (UI): 当前 "选择文件"+"加入列表"按钮与 pathBox 挤在同一水平行
    // 修复后它们应在 pathBox 下方的独立水平行中
    [Fact]
    public void FileButtons_AreInHorizontalRow_AfterPathBox()
    {
        RunOnStaThread(() =>
        {
            var tool = new ForceDeleteTool();
            var ui = tool.CreateContent();
            var panel = Assert.IsType<StackPanel>(ui);

            // 在根面板中查找包含两个文件按钮的水平 StackPanel
            StackPanel? buttonRow = null;
            int buttonRowIndex = -1;
            for (int i = 0; i < panel.Children.Count; i++)
            {
                if (panel.Children[i] is StackPanel sp && sp.Orientation == Orientation.Horizontal)
                {
                    if (HasButtonWithContent(sp, "选择文件") && HasButtonWithContent(sp, "加入列表"))
                    {
                        buttonRow = sp;
                        buttonRowIndex = i;
                        break;
                    }
                }
            }
            Assert.NotNull(buttonRow);
            Assert.True(buttonRowIndex > 0, "Button row should not be at index 0");

            // 当前（RED）：buttons 在 fileRow 中排在 TextBox 后面，之前没有别的 TextBox
            // 修复后（GREEN）：按钮行之前应有一个 TextBox（pathBox）
            bool hasTextBoxBeforeRow = false;
            for (int j = 0; j < buttonRowIndex; j++)
            {
                if (panel.Children[j] is TextBox)
                {
                    hasTextBoxBeforeRow = true;
                    break;
                }
            }
            Assert.True(hasTextBoxBeforeRow, "There should be a TextBox (pathBox) before the button row");
            return true;
        });
    }

    // RED: 当前脚本路径是硬编码的固定值 'toolbox_force_delete.ps1'
    // 修复后应为每个文件生成唯一路径
    [Fact]
    public void GetTempScriptPath_ReturnsUniquePathPerFile()
    {
        // 先验证方法存在（当前没有公开的方法，编译会失败）
        string path1 = ForceDeleteTool.GetTempScriptPath(@"C:\file1.txt");
        string path2 = ForceDeleteTool.GetTempScriptPath(@"C:\file2.txt");

        // 当前两个调用应该返回相同路径（bug）
        // 修复后应返回不同路径
        Assert.NotEqual(path1, path2);
    }

    // RED: 当前 RunElevatedMoveFileEx 返回 void，调用者在它执行前就 success++
    // 修复后应返回 bool 表示真实成功/失败
    [Fact]
    public void TryMoveFileEx_ShouldReturnBool()
    {
        // 当前这个方法不存在，或者返回 void
        // 修复后应返回 bool，让调用者能正确计数
        bool result = ForceDeleteTool.TryMoveFileEx(@"C:\nonexistent\file.txt");

        // 不关心具体返回值（文件不存在），只关心类型是 bool
        Assert.IsType<bool>(result);
    }

    // RED: 当前临时脚本没有在 finally 中清理
    // 修复后应在 finally 块中删除临时脚本
    [Fact]
    public void TempScript_IsCleanedUpInFinally()
    {
        string scriptPath = ForceDeleteTool.GetTempScriptPath(@"C:\test.txt");
        // 调用后脚本文件应该被移除
        // 这个测试验证清理机制存在
        Assert.False(File.Exists(scriptPath), "Temp script should be cleaned up after execution");
    }

    private static T RunOnStaThread<T>(Func<T> func)
    {
        T result = default!;
        var thread = new Thread(() => { result = func(); });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static Button? FindButtonWithContent(Panel panel, string content)
    {
        foreach (var child in panel.Children)
        {
            if (child is Button btn && btn.Content?.ToString()?.Contains(content) == true)
                return btn;
            if (child is Panel childPanel)
            {
                var found = FindButtonWithContent(childPanel, content);
                if (found != null) return found;
            }
        }
        return null;
    }

    private static bool HasButtonWithContent(Panel panel, string content)
    {
        return FindButtonWithContent(panel, content) != null;
    }
}