using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Toolbox.Helpers;

/// <summary>
/// 带淡入过渡动画的 ContentControl。
/// 当内容发生变化时，从 Opacity 0 淡入到 1（首次加载跳过动画）。
/// </summary>
public class TransitioningContentControl : ContentControl
{
    private readonly Storyboard _fadeInStoryboard;

    public TransitioningContentControl()
    {
        // 构建淡入动画：200ms，QuadraticEase EaseOut
        var fadeIn = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));

        _fadeInStoryboard = new Storyboard();
        _fadeInStoryboard.Children.Add(fadeIn);
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
                // 内容切换：先设为透明，再延迟一帧后启动淡入
                Opacity = 0.0;
                Dispatcher.BeginInvoke(() =>
                {
                    _fadeInStoryboard.Begin(this);
                });
            }
        }
    }
}