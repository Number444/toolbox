using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Toolbox.Tools;
using Xunit;

namespace Toolbox.Tests;

public class QrCodeToolTests
{
    [Fact]
    public void GenerateQrBytes_NullContent_ReturnsNull()
    {
        var result = QrCodeHelper.GeneratePngBytes(null!);
        Assert.Null(result);
    }

    [Fact]
    public void GenerateQrBytes_EmptyContent_ReturnsNull()
    {
        var result = QrCodeHelper.GeneratePngBytes("");
        Assert.Null(result);
    }

    [Fact]
    public void GenerateQrBytes_ValidContent_ReturnsNonEmptyBytes()
    {
        var result = QrCodeHelper.GeneratePngBytes("https://example.com");
        Assert.NotNull(result);
        Assert.True(result.Length > 100);
    }

    [Fact]
    public void CreateContent_ContainsGenerateButton()
    {
        var result = RunOnStaThread(() =>
        {
            var tool = new QrCodeTool();
            var ui = tool.CreateContent();
            var panel = Assert.IsType<StackPanel>(ui);
            return FindButtonWithText(panel, "生成二维码");
        });
        Assert.True(result, "UI should contain a button with text '生成二维码'");
    }

    [Fact]
    public void StatusBlock_NotInsideButtonRow()
    {
        var result = RunOnStaThread(() =>
        {
            var tool = new QrCodeTool();
            var ui = tool.CreateContent();
            var panel = Assert.IsType<StackPanel>(ui);

            // 递归搜索水平按钮行（包含"保存"按钮的）
            var buttonRow = FindHorizontalPanelContainingButton(panel, "保存");
            Assert.NotNull(buttonRow);

            bool hasStatusInButtonRow = false;
            foreach (var child in buttonRow.Children)
            {
                if (child is TextBlock)
                {
                    hasStatusInButtonRow = true;
                    break;
                }
            }
            return !hasStatusInButtonRow;
        });
        Assert.True(result, "Status TextBlock should NOT be inside the horizontal button row");
    }

    // 布局已调整为：图片与按钮在同一个水平容器中（嵌套在"结果"卡片内），该测试验证此结构
    [Fact]
    public void ImageBorder_IsInsideHorizontalContainer_WithButtons()
    {
        var result = RunOnStaThread(() =>
        {
            var tool = new QrCodeTool();
            var ui = tool.CreateContent();
            var panel = Assert.IsType<StackPanel>(ui);

            // 递归查找一个水平容器，里面同时包含图片边框和按钮
            foreach (var element in EnumerateTree(panel))
            {
                if (element is StackPanel sp && sp.Orientation == Orientation.Horizontal)
                {
                    bool hasImageBorder = false;
                    bool hasAnyButton = false;

                    foreach (var innerChild in sp.Children)
                    {
                        // 检查直接子元素是否为图片边框
                        if (innerChild is Border b && b.Child is Image)
                            hasImageBorder = true;

                        // 检查按钮（直接或嵌套在垂直 StackPanel 中）
                        if (innerChild is Button)
                            hasAnyButton = true;
                        if (innerChild is StackPanel innerSp)
                        {
                            foreach (var c in innerSp.Children)
                            {
                                if (c is Button)
                                    hasAnyButton = true;
                            }
                        }
                    }

                    if (hasImageBorder && hasAnyButton)
                        return true;
                }
            }
            return false;
        });
        Assert.True(result, "Image border should be in the same horizontal container as the buttons");
    }

    private static T RunOnStaThread<T>(Func<T> func)
    {
        T result = default!;
        Exception? error = null;
        // 必须捕获并转发异常：否则断言失败会在工作线程上成为未处理异常，
        // 直接崩溃整个测试主机进程（本测试类之前的崩溃就是这个原因）
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
        return result;
    }

    /// <summary>枚举元素的逻辑子元素：Panel 取 Children，Border 取 Child（UI 用 Border 卡片分组）</summary>
    private static IEnumerable<UIElement> GetChildren(UIElement element)
    {
        if (element is Panel p)
        {
            foreach (UIElement child in p.Children)
                yield return child;
        }
        else if (element is Border b && b.Child != null)
        {
            yield return b.Child;
        }
    }

    /// <summary>递归枚举整棵子树（含自身），穿透 Panel 与 Border 卡片</summary>
    private static IEnumerable<UIElement> EnumerateTree(UIElement root)
    {
        yield return root;
        foreach (var child in GetChildren(root))
        {
            foreach (var descendant in EnumerateTree(child))
                yield return descendant;
        }
    }

    private static bool FindButtonWithText(UIElement root, string text)
    {
        foreach (var element in EnumerateTree(root))
        {
            if (element is Button btn && btn.Content?.ToString()?.Contains(text) == true)
                return true;
        }
        return false;
    }

    private static StackPanel? FindHorizontalPanelContainingButton(UIElement root, string buttonContent)
    {
        foreach (var element in EnumerateTree(root))
        {
            if (element is StackPanel sp && sp.Orientation == Orientation.Horizontal)
            {
                // 检查这个水平 StackPanel 是否包含指定按钮（直接子级或嵌套在垂直面板中）
                foreach (var inner in sp.Children)
                {
                    if (inner is Button btn && btn.Content?.ToString()?.Contains(buttonContent) == true)
                        return sp;
                    if (inner is Panel innerPanel && FindButtonWithText(innerPanel, buttonContent))
                        return sp;
                }
            }
        }
        return null;
    }
}