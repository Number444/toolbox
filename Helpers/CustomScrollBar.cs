using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Toolbox.Helpers;

/// <summary>
/// 自定义迷你滚动条——替代系统 ScrollBar，支持精确控制 Thumb 视觉长度和外观。
/// 需与隐藏了系统条的 ScrollViewer 并列放置在 Grid 中，自动同步滚动状态。
/// </summary>
public class CustomScrollBar : Control
{
    static CustomScrollBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CustomScrollBar),
            new FrameworkPropertyMetadata(typeof(CustomScrollBar)));
    }

    #region 依赖属性

    public static readonly DependencyProperty TargetScrollViewerProperty =
        DependencyProperty.Register(
            nameof(TargetScrollViewer), typeof(ScrollViewer), typeof(CustomScrollBar),
            new PropertyMetadata(null, OnTargetScrollViewerChanged));

    /// <summary>要监控的 ScrollViewer</summary>
    public ScrollViewer? TargetScrollViewer
    {
        get => (ScrollViewer?)GetValue(TargetScrollViewerProperty);
        set => SetValue(TargetScrollViewerProperty, value);
    }

    /// <summary>Thumb 视觉长度缩放比。1.0 = 全比例，0.67 ≈ 2/3</summary>
    public static readonly DependencyProperty ThumbLengthRatioProperty =
        DependencyProperty.Register(
            nameof(ThumbLengthRatio), typeof(double), typeof(CustomScrollBar),
            new PropertyMetadata(0.67));

    public double ThumbLengthRatio
    {
        get => (double)GetValue(ThumbLengthRatioProperty);
        set => SetValue(ThumbLengthRatioProperty, value);
    }

    /// <summary>Thumb 默认颜色</summary>
    public static readonly DependencyProperty ThumbColorProperty =
        DependencyProperty.Register(
            nameof(ThumbColor), typeof(Color), typeof(CustomScrollBar),
            new PropertyMetadata(Color.FromArgb(0x21, 0xFF, 0xFF, 0xFF), OnVisualPropertyChanged));

    public Color ThumbColor
    {
        get => (Color)GetValue(ThumbColorProperty);
        set => SetValue(ThumbColorProperty, value);
    }

    /// <summary>Thumb 悬停颜色</summary>
    public static readonly DependencyProperty ThumbHoverColorProperty =
        DependencyProperty.Register(
            nameof(ThumbHoverColor), typeof(Color), typeof(CustomScrollBar),
            new PropertyMetadata(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF), OnVisualPropertyChanged));

    public Color ThumbHoverColor
    {
        get => (Color)GetValue(ThumbHoverColorProperty);
        set => SetValue(ThumbHoverColorProperty, value);
    }

    #endregion

    #region 内部状态

    private Border? _thumbElement;
    private bool _isDragging;
    private Point _dragStartPoint;
    private double _scrollStartOffset;

    #endregion

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // 解除旧模板事件
        if (_thumbElement != null)
        {
            _thumbElement.MouseLeftButtonDown -= OnThumbMouseDown;
            _thumbElement.MouseEnter -= OnThumbMouseEnter;
            _thumbElement.MouseLeave -= OnThumbMouseLeave;
        }

        _thumbElement = GetTemplateChild("ThumbElement") as Border;

        // 连接新模板事件
        if (_thumbElement != null)
        {
            _thumbElement.MouseLeftButtonDown += OnThumbMouseDown;
            _thumbElement.MouseEnter += OnThumbMouseEnter;
            _thumbElement.MouseLeave += OnThumbMouseLeave;
        }

        ApplyVisualProperties();
        UpdateThumb();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        // 仅在点击轨道空白区域（非 Thumb 区域）时才调用 base
        // base.OnMouseLeftButtonDown 内部会调用 Mouse.Capture(this)，
        // 若在 Thumb 拖拽前执行会抢走 ThumbElement 的鼠标捕获，导致拖拽失效
        bool isThumbClick = _thumbElement != null &&
            (e.OriginalSource == _thumbElement ||
             _thumbElement.IsAncestorOf(e.OriginalSource as DependencyObject));

        if (isThumbClick)
        {
            // Thumb 点击：不调用 base，交由 OnThumbMouseDown 处理
            return;
        }

        base.OnMouseLeftButtonDown(e);

        // 点击轨道空白区域 → 翻页
        if (_thumbElement == null || TargetScrollViewer == null) return;

        var pos = e.GetPosition(this);
        double thumbTop = _thumbElement.Margin.Top;
        double thumbBottom = thumbTop + _thumbElement.ActualHeight;

        if (pos.Y < thumbTop)
            TargetScrollViewer.PageUp();
        else if (pos.Y > thumbBottom)
            TargetScrollViewer.PageDown();

        e.Handled = true;
    }

    #region Thumb 拖拽

    private void OnThumbMouseEnter(object? sender, MouseEventArgs e)
    {
        if (!_isDragging && _thumbElement != null)
            _thumbElement.Background = new SolidColorBrush(ThumbHoverColor);
    }

    private void OnThumbMouseLeave(object? sender, MouseEventArgs e)
    {
        if (!_isDragging && _thumbElement != null)
            _thumbElement.Background = new SolidColorBrush(ThumbColor);
    }

    private void OnThumbMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (TargetScrollViewer == null || _thumbElement == null) return;

        _isDragging = true;
        _dragStartPoint = e.GetPosition(this);
        _scrollStartOffset = TargetScrollViewer.VerticalOffset;

        _thumbElement.CaptureMouse();
        _thumbElement.MouseMove += OnThumbMouseMove;
        _thumbElement.MouseLeftButtonUp += OnThumbMouseUp;

        // 拖拽中提亮
        _thumbElement.Background = new SolidColorBrush(ThumbHoverColor);
        e.Handled = true;
    }

    private void OnThumbMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isDragging || TargetScrollViewer == null || _thumbElement == null) return;

        var currentPos = e.GetPosition(this);
        double deltaY = currentPos.Y - _dragStartPoint.Y;

        double scrollableH = TargetScrollViewer.ScrollableHeight;
        double trackHeight = ActualHeight;
        double thumbHeight = _thumbElement.ActualHeight;
        double availableSpace = trackHeight - thumbHeight;

        if (availableSpace <= 0) return;

        double scrollDelta = deltaY / availableSpace * scrollableH;
        double newOffset = _scrollStartOffset + scrollDelta;
        newOffset = Math.Clamp(newOffset, 0, scrollableH);

        TargetScrollViewer.ScrollToVerticalOffset(newOffset);
        e.Handled = true;
    }

    private void OnThumbMouseUp(object? sender, MouseButtonEventArgs e)
    {
        _isDragging = false;

        if (_thumbElement != null)
        {
            _thumbElement.ReleaseMouseCapture();
            _thumbElement.MouseMove -= OnThumbMouseMove;
            _thumbElement.MouseLeftButtonUp -= OnThumbMouseUp;
            _thumbElement.Background = new SolidColorBrush(ThumbColor);
        }

        e.Handled = true;
    }

    #endregion

    #region ScrollViewer 同步

    private static void OnTargetScrollViewerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (CustomScrollBar)d;
        if (e.OldValue is ScrollViewer oldSv)
            oldSv.ScrollChanged -= bar.OnTargetScrollChanged;
        if (e.NewValue is ScrollViewer newSv)
        {
            newSv.ScrollChanged += bar.OnTargetScrollChanged;
            bar.UpdateThumb();
        }
    }

    private void OnTargetScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // 过滤无实际变化的虚假事件（避免不可滚动页面上频繁派发 UpdateThumb）
        if (e.VerticalChange == 0 && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
            return;

        Dispatcher.BeginInvoke(() => UpdateThumb());
    }

    /// <summary>
    /// 将鼠标滚轮事件转发给绑定的 TargetScrollViewer，
    /// 解决鼠标位于自定义滚动条区域上方时滚轮事件被丢弃的问题。
    /// </summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (TargetScrollViewer != null)
        {
            var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent
            };
            TargetScrollViewer.RaiseEvent(args);
            e.Handled = args.Handled;
        }
        else
        {
            base.OnMouseWheel(e);
        }
    }

    private void ApplyVisualProperties()
    {
        if (_thumbElement != null)
        {
            _thumbElement.Background = new SolidColorBrush(ThumbColor);
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((CustomScrollBar)d).ApplyVisualProperties();
    }

    private void UpdateThumb()
    {
        if (_thumbElement == null || TargetScrollViewer == null) return;

        double viewportH = TargetScrollViewer.ViewportHeight;
        double extentH = TargetScrollViewer.ExtentHeight;
        double scrollableH = TargetScrollViewer.ScrollableHeight;

        if (extentH <= viewportH || scrollableH <= 0)
        {
            _thumbElement.Visibility = Visibility.Collapsed;
            return;
        }

        _thumbElement.Visibility = Visibility.Visible;

        // Thumb 高度 = 轨道高度 × (可视比例) × 长度缩放比
        double trackHeight = ActualHeight;
        double thumbHeight = trackHeight * (viewportH / extentH) * ThumbLengthRatio;
        if (thumbHeight < 8) thumbHeight = 8;

        _thumbElement.Height = thumbHeight;

        // Thumb 顶部位置
        double scrollPercent = TargetScrollViewer.VerticalOffset / scrollableH;
        double availableSpace = trackHeight - thumbHeight;
        double thumbTop = scrollPercent * availableSpace;

        var targetMargin = new Thickness(0, thumbTop, 0, 0);

        if (!_isDragging)
        {
            var anim = new ThicknessAnimation
            {
                To = targetMargin,
                Duration = TimeSpan.FromMilliseconds(100),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            _thumbElement.BeginAnimation(Border.MarginProperty, anim);
        }
        else
        {
            // 清除非拖拽时残留的 HoldEnd 动画（动画优先级高于本地值）
            _thumbElement.BeginAnimation(Border.MarginProperty, null);
            _thumbElement.Margin = targetMargin;
        }
    }

    #endregion
}