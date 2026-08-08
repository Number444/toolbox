using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Toolbox.Helpers;

/// <summary>
/// 带过渡动画的 ContentControl。
/// 当内容发生变化时，Opacity 0→1 淡入 + TranslateY 8→0 轻微上滑（首次加载跳过动画）。
/// </summary>
public class TransitioningContentControl : ContentControl
{
    private readonly Storyboard _transitionStoryboard;

    public TransitioningContentControl()
    {
        RenderTransform = new TranslateTransform();

        // 200ms，CubicEase EaseOut：淡入 + 上滑同步进行
        var duration = new Duration(TimeSpan.FromMilliseconds(200));
        IEasingFunction ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fadeIn = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));

        var slideUp = new DoubleAnimation
        {
            From = 8.0,
            To = 0.0,
            Duration = duration,
            EasingFunction = ease
        };
        Storyboard.SetTargetProperty(slideUp, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

        _transitionStoryboard = new Storyboard();
        _transitionStoryboard.Children.Add(fadeIn);
        _transitionStoryboard.Children.Add(slideUp);
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);

        if (newContent != null)
        {
            if (oldContent == null)
            {
                // 首次加载，直接显示不播放动画
                Opacity = 1.0;
            }
            else
            {
                // 内容切换：先设为透明，再延迟一帧后启动淡入+上滑
                Opacity = 0.0;
                Dispatcher.BeginInvoke(() =>
                {
                    _transitionStoryboard.Begin(this);
                });
            }
        }
    }
}