using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Toolbox.Helpers;
using Toolbox.Models;

namespace Toolbox.Tools;

/// <summary>
/// 截图识字 —— 导入图片（文件选择 / 拖入虚线框 / 粘贴），调用 Windows 内置 OCR 引擎离线提取文字。
/// 界面约定（卡片化、配色、状态行）与二维码生成器等工具保持一致。
/// </summary>
public class OcrTool : ITool
{
    // 配色统一使用 Toolbox.Models.ThemeColors
    private Image? _previewImage;
    private Border? _previewBorder;
    private TextBlock? _previewInfo;
    private TextBox? _resultBox;
    private TextBlock? _statusBlock;

    // 拖拽投放区（虚线框）与"带洞压暗"遮罩
    private Grid? _rootGrid;
    private Grid? _zoneGrid;
    private Rectangle? _zoneRect;
    private TextBlock? _zoneText;
    private Canvas? _dimLayer;

    private int _ocrVersion; // 防抖/丢弃过期识别结果

    public string Name => "截图识字";
    public string Description => "导入截图或照片，离线提取其中的文字（Windows 内置引擎，不上传网络）。";
    public string Category => ToolCategory.Text;
    public string IconGlyph => "🔍";

    public UIElement CreateContent()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        // ====== 卡片一：图片来源 ======
        var openButton = new Button
        {
            Content = "📂 选择图片",
            FontSize = 14,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };

        var pasteButton = new Button
        {
            Content = "📋 粘贴剪贴板图片",
            FontSize = 14,
            Padding = new Thickness(14, 6, 14, 6)
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };
        buttonRow.Children.Add(openButton);
        buttonRow.Children.Add(pasteButton);

        // 拖拽投放区：横置长条虚线圆角矩形，初始灰色，拖入后变绿
        _zoneRect = new Rectangle
        {
            Stroke = new SolidColorBrush(ThemeColors.TextSecondary),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            RadiusX = 8,
            RadiusY = 8
        };
        _zoneText = new TextBlock
        {
            Text = "将图片拖入框内",
            FontSize = 14,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _zoneGrid = new Grid { Height = 88, Margin = new Thickness(0, 0, 0, 10) };
        _zoneGrid.Children.Add(_zoneRect);
        _zoneGrid.Children.Add(_zoneText);

        // 预览区：左侧缩略图 + 右侧文件名/尺寸（初始隐藏，加载图片后显示）
        _previewImage = new Image
        {
            MaxWidth = 320,
            MaxHeight = 200,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var previewImageBox = new Border
        {
            Background = new SolidColorBrush(ThemeColors.BgDark),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Child = _previewImage
        };

        _previewInfo = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.TextSecondary),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 0, 0, 0)
        };

        var previewRow = new StackPanel { Orientation = Orientation.Horizontal };
        previewRow.Children.Add(previewImageBox);
        previewRow.Children.Add(_previewInfo);

        _previewBorder = new Border
        {
            Child = previewRow,
            Visibility = Visibility.Collapsed
        };

        var sourceCard = BuildCard("图片来源");
        var sourceInner = (StackPanel)sourceCard.Child;
        sourceInner.Children.Add(buttonRow);
        sourceInner.Children.Add(_zoneGrid);
        sourceInner.Children.Add(_previewBorder);
        sourceCard.Margin = new Thickness(0, 0, 0, 12);

        // ====== 卡片二：识别结果 ======
        _resultBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 140,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var copyTextButton = new Button
        {
            Content = "📄 复制全部文字",
            FontSize = 14,
            Padding = new Thickness(14, 6, 14, 6),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var resultCard = BuildCard("识别结果");
        var resultInner = (StackPanel)resultCard.Child;
        resultInner.Children.Add(_resultBox);
        resultInner.Children.Add(copyTextButton);

        _statusBlock = new TextBlock
        {
            Text = "",
            FontSize = 13,
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };

        panel.Children.Add(sourceCard);
        panel.Children.Add(resultCard);
        panel.Children.Add(_statusBlock);

        // ====== 根容器：内容 + "带洞压暗"遮罩层（拖入虚线框时压暗四周、只留投放区高亮） ======
        _rootGrid = new Grid { AllowDrop = true };
        _rootGrid.Children.Add(panel);

        _dimLayer = new Canvas
        {
            IsHitTestVisible = false, // 不拦截拖放事件
            Visibility = Visibility.Collapsed
        };
        for (int i = 0; i < 4; i++)
        {
            _dimLayer.Children.Add(new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x8C, 0x00, 0x00, 0x00)) // 55% 黑，压暗用
            });
        }
        _rootGrid.Children.Add(_dimLayer);

        // ====== 事件 ======

        // 选择文件（仅限图片）
        openButton.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = ImageFileHelper.DialogFilter,
                Title = "选择要识别的图片"
            };
            if (dialog.ShowDialog() == true)
                LoadFromFile(dialog.FileName);
        };

        // 粘贴按钮与 Ctrl+V 共用同一入口
        pasteButton.Click += (_, _) => LoadFromClipboard();

        // 拖拽：悬停实时判断鼠标是否在虚线框内，框内则变绿 + 压暗四周
        _rootGrid.PreviewDragEnter += OnDragOver;
        _rootGrid.PreviewDragOver += OnDragOver;
        _rootGrid.PreviewDragLeave += (_, _) => ResetDragVisuals();
        _rootGrid.PreviewDrop += (_, e) =>
        {
            var path = GetDroppedImagePath(e);
            ResetDragVisuals();
            if (path != null)
            {
                e.Handled = true;
                LoadFromFile(path);
            }
        };

        // Ctrl+V 粘贴：仅当剪贴板确实有图片/图片文件时拦截，避免影响正常文本粘贴
        _rootGrid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.V &&
                (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                (Clipboard.ContainsImage() || Clipboard.ContainsFileDropList()))
            {
                e.Handled = true;
                LoadFromClipboard();
            }
        };

        // 复制识别结果
        copyTextButton.Click += (_, _) =>
        {
            var text = _resultBox!.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus("⚠️ 没有可复制的文字", ThemeColors.Danger);
                return;
            }
            try
            {
                Clipboard.SetText(text);
                SetStatus("✅ 文字已复制到剪贴板", ThemeColors.Success);
            }
            catch (Exception ex)
            {
                SetStatus($"❌ 复制失败：{ex.Message}", ThemeColors.Danger);
            }
        };

        return _rootGrid;

        // ====== 以下为交互实现（局部函数，仅服务本工具） ======

        void OnDragOver(object sender, DragEventArgs e)
        {
            if (GetDroppedImagePath(e) == null)
            {
                e.Effects = DragDropEffects.None;
                ResetDragVisuals();
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Copy;
            // 实时判断鼠标是否位于虚线框内：框内高亮 + 压暗，框外恢复原样
            var pos = e.GetPosition(_zoneGrid!);
            bool inside = pos.X >= 0 && pos.Y >= 0
                       && pos.X <= _zoneGrid!.ActualWidth && pos.Y <= _zoneGrid.ActualHeight;
            if (inside) SetZoneActive();
            else ResetDragVisuals();
            e.Handled = true;
        }

        // 投放区激活：虚线变绿，遮罩压暗四周（投放区位置挖洞保持高亮）
        void SetZoneActive()
        {
            _zoneRect!.Stroke = new SolidColorBrush(ThemeColors.Success);
            _zoneText!.Text = "⬇️ 松开立即识别";
            _zoneText.Foreground = new SolidColorBrush(ThemeColors.Success);
            _zoneText.FontWeight = FontWeights.SemiBold;
            ShowDimAroundZone();
        }

        // 恢复默认：灰色虚线，撤掉压暗
        void ResetDragVisuals()
        {
            _zoneRect!.Stroke = new SolidColorBrush(ThemeColors.TextSecondary);
            _zoneText!.Text = "将图片拖入框内";
            _zoneText.Foreground = new SolidColorBrush(ThemeColors.TextSecondary);
            _zoneText.FontWeight = FontWeights.Normal;
            _dimLayer!.Visibility = Visibility.Collapsed;
        }

        // 用 4 块矩形拼出"中间挖洞"的压暗遮罩，洞口即投放区
        void ShowDimAroundZone()
        {
            var topLeft = _zoneGrid!.TransformToVisual(_rootGrid!).Transform(new Point(0, 0));
            double zx = topLeft.X, zy = topLeft.Y;
            double zw = _zoneGrid.ActualWidth, zh = _zoneGrid.ActualHeight;
            double rw = _rootGrid!.ActualWidth, rh = _rootGrid.ActualHeight;

            SetDimRect(0, 0, 0, rw, zy);                    // 上
            SetDimRect(1, 0, zy + zh, rw, rh - zy - zh);    // 下
            SetDimRect(2, 0, zy, zx, zh);                   // 左
            SetDimRect(3, zx + zw, zy, rw - zx - zw, zh);   // 右
            _dimLayer!.Visibility = Visibility.Visible;
        }

        void SetDimRect(int index, double x, double y, double w, double h)
        {
            var rect = (Rectangle)_dimLayer!.Children[index];
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            rect.Width = Math.Max(0, w);
            rect.Height = Math.Max(0, h);
        }

        void SetStatus(string text, Color color)
        {
            _statusBlock!.Text = text;
            _statusBlock.Foreground = new SolidColorBrush(color);
        }

        void LoadFromFile(string path)
        {
            if (!ImageFileHelper.IsImageFile(path))
            {
                SetStatus("⚠️ 不支持的文件格式，请选择图片文件", ThemeColors.Danger);
                return;
            }
            var bitmap = ImageFileHelper.LoadBitmap(path);
            if (bitmap == null)
            {
                SetStatus("❌ 图片加载失败（文件可能损坏或缺少编解码器）", ThemeColors.Danger);
                return;
            }
            ShowPreview(bitmap, System.IO.Path.GetFileName(path));
            _ = RunOcrAsync(bitmap);
        }

        void LoadFromClipboard()
        {
            // 优先位图（截图），其次资源管理器复制的图片文件
            var image = ImageFileHelper.TryGetClipboardImage();
            if (image != null)
            {
                ShowPreview(image, "剪贴板图片");
                _ = RunOcrAsync(image);
                return;
            }

            var file = ImageFileHelper.TryGetClipboardImageFile();
            if (file != null)
            {
                LoadFromFile(file);
                return;
            }

            SetStatus("⚠️ 剪贴板中没有图片（可先截图或复制图片文件）", ThemeColors.Danger);
        }

        void ShowPreview(BitmapSource bitmap, string sourceName)
        {
            _previewImage!.Source = bitmap;
            _previewInfo!.Text = $"{sourceName}\n{bitmap.PixelWidth} × {bitmap.PixelHeight} 像素";
            _previewBorder!.Visibility = Visibility.Visible;
        }

        async Task RunOcrAsync(BitmapSource bitmap)
        {
            int ver = ++_ocrVersion;
            SetStatus("⏳ 正在识别…", ThemeColors.Warning);

            var sw = Stopwatch.StartNew();
            try
            {
                string text = await Task.Run(() => OcrHelper.RecognizeAsync(bitmap));
                if (ver != _ocrVersion) return; // 期间又导入新图，丢弃过期结果

                sw.Stop();
                if (string.IsNullOrWhiteSpace(text))
                {
                    _resultBox!.Text = "";
                    SetStatus("⚠️ 未识别到文字（可换更清晰的图片重试）", ThemeColors.Danger);
                    return;
                }

                _resultBox!.Text = text;
                int lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
                SetStatus($"✅ 识别完成：{text.Trim().Length} 字 / {lines} 行，耗时 {sw.ElapsedMilliseconds / 1000.0:0.0} 秒",
                    ThemeColors.Success);
            }
            catch (Exception ex)
            {
                if (ver != _ocrVersion) return;
                SetStatus($"❌ 识别失败：{ex.Message}", ThemeColors.Danger);
            }
        }
    }

    /// <summary>取拖放数据中的第一个图片文件路径；非文件或无图片返回 null</summary>
    private static string? GetDroppedImagePath(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return null;
        return paths.FirstOrDefault(ImageFileHelper.IsImageFile);
    }

    /// <summary>构建分组卡片：深灰圆角容器 + 组标题，内容随后追加；
    /// 卡片带 GlowCardMarker 标记，纳入鼠标光照发光目标</summary>
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
