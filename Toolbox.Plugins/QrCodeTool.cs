using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Toolbox.Models;

namespace Toolbox.Tools;

/// <summary>
/// 二维码生成器 —— 输入文本/URL 实时生成二维码，可保存或复制
/// </summary>
public class QrCodeTool : ITool
{
    private Image? _qrImage;
    private byte[]? _currentPngBytes;
    private TextBlock? _statusBlock;
    private int _debounceVersion;

    public string Name => "二维码生成器";
    public string Description => "输入文本或 URL，实时生成二维码图片，可保存或复制。";
    public string Category => Toolbox.Models.ToolCategory.Text;
    public string IconGlyph => "📱";

    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        // 说明文字
        var desc = new TextBlock
        {
            Text = "输入文本或 URL，自动生成二维码。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            Margin = new Thickness(0, 0, 0, 16)
        };

        // 单行输入框
        var inputBox = new TextBox
        {
            Height = 34,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };

        // 二维码图片容器（深灰圆角背景，与主题融合）
        var imageBorder = new Border
        {
            Width = 200,
            Height = 200,
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
            CornerRadius = new CornerRadius(8),
            Child = new Image
            {
                Width = 180,
                Height = 180,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 16, 0)
        };
        _qrImage = (Image)imageBorder.Child;

        // 三个按钮：左对齐竖排，从短到长
        var generateButton = new Button
        {
            Content = "生成二维码",
            FontSize = 14,
            Padding = new Thickness(14, 6, 14, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6)
        };

        var saveButton = new Button
        {
            Content = "💾 保存为 PNG",
            FontSize = 14,
            Padding = new Thickness(14, 6, 14, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6)
        };

        var copyButton = new Button
        {
            Content = "📋 复制到剪贴板",
            FontSize = 14,
            Padding = new Thickness(14, 6, 14, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6)
        };

        // 状态文字
        _statusBlock = new TextBlock
        {
            Text = "",
            FontSize = 13,
            Margin = new Thickness(0, 4, 0, 0)
        };

        // 右侧垂直按钮区（左对齐，从短到长排列）
        var rightPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 0, 0)
        };
        rightPanel.Children.Add(generateButton);
        rightPanel.Children.Add(saveButton);
        rightPanel.Children.Add(copyButton);
        rightPanel.Children.Add(_statusBlock);

        // 水平容器：左侧图片 + 右侧按钮区
        var horizontalRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };
        horizontalRow.Children.Add(imageBorder);
        horizontalRow.Children.Add(rightPanel);

        // 内部生成方法
        void DoGenerate()
        {
            try
            {
                _currentPngBytes = QrCodeHelper.GeneratePngBytes(inputBox.Text);

                if (_currentPngBytes == null)
                {
                    _qrImage!.Source = null;
                    _statusBlock!.Text = "";
                    return;
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new MemoryStream(_currentPngBytes);
                bitmap.EndInit();
                bitmap.StreamSource = null;

                _qrImage!.Source = bitmap;
                _statusBlock!.Text = $"✅ 已生成 ({inputBox.Text.Length} 字符)";
                _statusBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0xA0, 0x20));
            }
            catch (Exception ex)
            {
                _statusBlock!.Text = $"❌ 生成失败：{ex.Message}";
                _statusBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x40, 0x40));
            }
        }

        // 生成按钮点击事件
        generateButton.Click += (_, _) => DoGenerate();

        // 自动防抖（辅助触发，300ms）
        inputBox.TextChanged += (_, _) =>
        {
            int ver = ++_debounceVersion;
            string text = inputBox.Text;
            _ = Task.Delay(300).ContinueWith(_ =>
            {
                if (ver != _debounceVersion) return;
                Application.Current.Dispatcher.Invoke(DoGenerate);
            });
        };

        // 保存按钮
        saveButton.Click += (_, _) =>
        {
            if (_currentPngBytes == null)
            {
                _statusBlock!.Text = "⚠️ 请先生成二维码";
                _statusBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x40, 0x40));
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG 图片 (*.png)|*.png",
                FileName = "qrcode.png"
            };

            if (dialog.ShowDialog() == true)
            {
                File.WriteAllBytes(dialog.FileName, _currentPngBytes);
                _statusBlock!.Text = $"✅ 已保存到 {dialog.FileName}";
                _statusBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0xA0, 0x20));
            }
        };

        // 复制按钮
        copyButton.Click += (_, _) =>
        {
            if (_qrImage!.Source == null)
            {
                _statusBlock!.Text = "⚠️ 请先生成二维码";
                _statusBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x40, 0x40));
                return;
            }

            try
            {
                Clipboard.SetImage((BitmapSource)_qrImage.Source);
                _statusBlock!.Text = "✅ 已复制到剪贴板";
                _statusBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0xA0, 0x20));
            }
            catch (Exception ex)
            {
                _statusBlock!.Text = $"❌ 复制失败：{ex.Message}";
                _statusBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x40, 0x40));
            }
        };

        panel.Children.Add(desc);
        panel.Children.Add(inputBox);
        panel.Children.Add(horizontalRow);  // 图片左 + 按钮右

        return panel;
    }
}