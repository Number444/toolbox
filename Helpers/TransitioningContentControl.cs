using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Toolbox.Helpers;

/// <summary>
/// 带过渡动画的 ContentControl。
/// 内容切换分两段：旧内容 200ms 淡出（EaseIn，无位移）→ 新内容 400ms 淡入 + TranslateY 8→0 上滑（EaseOut）。
/// 退场通过"回写旧内容"实现真正停留到淡出结束再切换；首次加载跳过动画。
/// </summary>
public class TransitioningContentControl : ContentControl
{
    private readonly Storyboard _enterStoryboard;
    private readonly Storyboard _exitStoryboard;

    private object? _pendingContent;      // 退场期间收到的最新内容，退场完成后切入
    private bool _isExiting;
    private bool _suppressContentChange;  // 回写 Content 时抑制 OnContentChanged 递归
    private readonly DoubleAnimation _slideUpAnimation;

    /// <summary>退场动画进行中（设置层退场需等它完成再启动，保持全程遮挡不露馅）</summary>
    public bool IsExiting => _isExiting;

    /// <summary>退场动画完成（旧内容已淡出、新内容即将切入时触发；含内容被清空的路径）</summary>
    public event Action? ExitCompleted;

    /// <summary>进场位移起始 Y（px）。默认 8 = 从下方上滑生长；负值 = 从上方下滑落下（如工具标题区，与内容区形成对向关系）</summary>
    public static readonly DependencyProperty SlideFromYProperty =
        DependencyProperty.Register(
            nameof(SlideFromY), typeof(double), typeof(TransitioningContentControl),
            new PropertyMetadata(8.0));

    public double SlideFromY
    {
        get => (double)GetValue(SlideFromYProperty);
        set => SetValue(SlideFromYProperty, value);
    }

    public TransitioningContentControl()
    {
        RenderTransform = new TranslateTransform();

        // 进场：400ms，CubicEase EaseOut，淡入 + 上滑同步进行
        var enterDuration = new Duration(TimeSpan.FromMilliseconds(400));
        IEasingFunction enterEase = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fadeIn = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = enterDuration,
            EasingFunction = enterEase
        };
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));

        var slideUp = new DoubleAnimation
        {
            From = 8.0,   // 占位值，进场前由 SlideFromY 覆盖
            To = 0.0,
            Duration = enterDuration,
            EasingFunction = enterEase
        };
        Storyboard.SetTargetProperty(slideUp, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
        _slideUpAnimation = slideUp;

        _enterStoryboard = new Storyboard();
        _enterStoryboard.Children.Add(fadeIn);
        _enterStoryboard.Children.Add(slideUp);
        // 进场完成清时钟：HoldEnd 保持值让 IsAnimated 永久 true（MainWindow 光晕闸门不关，
        // 2026-08-11 实测）；值回落本地值（Opacity=1 / RenderTransform.Y=0 = 动画终值，无缝）
        _enterStoryboard.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            (RenderTransform as TranslateTransform)?.BeginAnimation(TranslateTransform.YProperty, null);
        };

        // 退场：200ms，CubicEase EaseIn，仅淡出（无位移，避免与进场上滑方向打架）
        var fadeOut = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath("Opacity"));

        _exitStoryboard = new Storyboard();
        _exitStoryboard.Children.Add(fadeOut);
        _exitStoryboard.Completed += OnExitCompleted;
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);

        if (_suppressContentChange || newContent == null) return;

        if (oldContent == null)
        {
            // 首次加载，直接显示不播放动画
            Opacity = 1.0;
            return;
        }

        // 记录最新待切入内容（退场进行中再次切换时只更新它，始终以最新内容为准）
        _pendingContent = newContent;

        if (!_isExiting)
        {
            _isExiting = true;
            IsHitTestVisible = false;   // 交接期间挡点击，避免点到正在淡出的旧内容

            // 回写旧内容：让旧内容真正停留，200ms 淡出结束后才在 OnExitCompleted 切入新内容
            _suppressContentChange = true;
            SetCurrentValue(ContentProperty, oldContent);
            _suppressContentChange = false;

            _exitStoryboard.Begin(this);
        }
        else
        {
            // 退场进行中又切换：撤回本次替换，继续显示原内容直到淡出完成
            _suppressContentChange = true;
            SetCurrentValue(ContentProperty, oldContent);
            _suppressContentChange = false;
        }
    }

    private void OnExitCompleted(object? sender, EventArgs e)
    {
        _isExiting = false;
        // 清退场淡出时钟：HoldEnd 保持值让 IsAnimated 永久 true（光晕闸门不关）。
        // 值回落本地值 1.0（下方/首次加载路径已设）——同回调内即切入新内容 + 进场动画接管，
        // 中间态不渲染，无闪烁
        BeginAnimation(OpacityProperty, null);
        ExitCompleted?.Invoke();
        var next = _pendingContent;
        _pendingContent = null;

        if (next == null)
        {
            Opacity = 1.0;
            IsHitTestVisible = true;
            return;
        }

        _suppressContentChange = true;
        SetCurrentValue(ContentProperty, next);
        _suppressContentChange = false;

        // 进场：从透明 + SlideFromY 处淡入滑入（默认下方上滑，标题区为上方下滑）
        IsHitTestVisible = true;
        _slideUpAnimation.From = SlideFromY;
        _enterStoryboard.Begin(this);
    }
}
