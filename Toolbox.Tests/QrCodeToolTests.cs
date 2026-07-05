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

    // RED: 这个测试将失败——当前图片在底部，按钮在上方，两者不在同一水平容器中
    [Fact]
    public void ImageBorder_IsInsideHorizontalContainer_WithButtons()
    {
        var result = RunOnStaThread(() =>
        {
            var tool = new QrCodeTool();
            var ui = tool.CreateContent();
            var panel = Assert.IsType<StackPanel>(ui);

            // 在根面板的子元素中查找一个水平容器，里面同时包含图片边框和按钮
            foreach (var child in panel.Children)
            {
                if (child is StackPanel sp && sp.Orientation == Orientation.Horizontal)
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
        var thread = new Thread(() => { result = func(); });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static bool FindButtonWithText(Panel panel, string text)
    {
        foreach (var child in panel.Children)
        {
            if (child is Button btn && btn.Content?.ToString()?.Contains(text) == true)
                return true;
            if (child is Panel childPanel)
            {
                if (FindButtonWithText(childPanel, text))
                    return true;
            }
        }
        return false;
    }

    private static StackPanel? FindHorizontalPanelContainingButton(Panel panel, string buttonContent)
    {
        foreach (var child in panel.Children)
        {
            if (child is StackPanel sp && sp.Orientation == Orientation.Horizontal)
            {
                // 检查这个水平 StackPanel 是否包含指定按钮
                foreach (var inner in sp.Children)
                {
                    if (inner is Button btn && btn.Content?.ToString()?.Contains(buttonContent) == true)
                        return sp;
                    if (inner is StackPanel innerSp)
                    {
                        // 也可能按钮在嵌套的垂直面板中
                        foreach (var c in innerSp.Children)
                        {
                            if (c is Button btn2 && btn2.Content?.ToString()?.Contains(buttonContent) == true)
                                return sp;
                        }
                    }
                }
            }
            // 递归搜索子面板
            if (child is Panel childPanel)
            {
                var found = FindHorizontalPanelContainingButton(childPanel, buttonContent);
                if (found != null)
                    return found;
            }
        }
        return null;
    }
}