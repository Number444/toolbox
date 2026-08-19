using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Toolbox.Core.Controls;

/// <summary>
/// 弹窗/浮层内容打开/关闭动画的公共实现（2026-08-19 自 dsh-app 同名助手原样移植，
/// 供 ConfirmDialog / DownloadDialog / ThemedMenuWindow 等自绘弹窗复用，与 dsh-app 菜单动画同款）。
/// 位于 Core 层：ThemedMenuWindow（Core）也要用，Plugins 层引用 Core 单向可达。
/// - 打开：垂直抛出（单条 BackEase 连续过冲回弹）+ 小→1 放大 + 模糊渐清 + 快速淡入
/// - 关闭：打开动画的严格时间倒放（先沿惯性微顿 2px 再飞回起点，同时缩小/渐模糊/淡出）
///
/// 实现关键（dsh-app v1.3.1 修复）：必须用 Animatable.BeginAnimation 直调（Transform/Effect 均为 Animatable），
/// 不能用 Storyboard.SetTarget —— Storyboard 对非元素目标（Transform/Effect 等 Freezable）的动画
/// 会被静默丢弃，导致只有元素属性（Opacity）动画生效、缩放/位移/模糊全部定格在起始态。
/// BeginAnimation 的同属性替换语义天然取消旧动画；被替换/重置的旧时钟不触发 Completed，
/// 孤儿动画（RenderTransform 被替换后仍跑完的旧 Transform 时钟）完成回调用 ReferenceEquals 防误清。
/// 尊重系统"菜单动画"设置；重新打开会取消进行中的收拢（IsClosing 复位）。
///
/// 注意：BlurEffect.Radius 渲染按整数阶梯量化（2026-08-19 实测验证，见 docs/changelog-2026-08-19 §7），
/// 半径 20 的渐清在 420ms 内约每 21ms 一档，肉眼无感；小半径慢速渐清慎用此直接动画。
/// </summary>
public static class PopupAnimator
{
    /// <summary>菜单/弹窗默认档：全套（垂直抛出 + 惯性回弹 + 模糊渐清）。</summary>
    public static readonly OpenOptions DefaultOpen = new();

    /// <summary>默认关闭档：打开动画的严格时间倒放。</summary>
    public static readonly CloseOptions DefaultClose = new();

    /// <summary>收拢动画进行中标志（幂等防重入；重新打开时复位）。</summary>
    private static readonly DependencyProperty IsClosingProperty = DependencyProperty.RegisterAttached(
        "IsClosing", typeof(bool), typeof(PopupAnimator), new PropertyMetadata(false));

    /// <summary>逐帧模糊降级开关：渲染 Tier 2（完整硬件加速）以下禁用 BlurEffect 动画
    /// （软渲染/远程桌面逐帧模糊是掉帧大户）；位移/缩放/淡入淡出不降级。</summary>
    private static bool BlurSupported => RenderCapability.Tier >= 0x20000;

    /// <summary>
    /// 播放打开动画。flyFrom = 抛出起点相对最终位置的偏移（DIP）；**直上直下：仅取 Y 分量**（X 忽略），
    /// 弹窗传 (0,-24)（从上方抛出落位）。
    /// 起始态在同步段内设置并立即播放，渲染首帧即动画起点（不闪帧）。
    /// </summary>
    public static void PlayOpen(FrameworkElement el, Point? flyFrom = null, OpenOptions? options = null)
    {
        options ??= DefaultOpen;
        el.SetValue(IsClosingProperty, false); // 重新打开：取消收拢态（进行中的收拢动画由下方 BeginAnimation 替换移除）
        ResetVisual(el);

        if (!SystemParameters.MenuAnimation) return; // 系统关闭动画：直接以最终态显示

        // 起始态：透明 + 小 + 模糊 + 锚点方向偏移
        var scale = new ScaleTransform(options.StartScale, options.StartScale);
        var translate = new TranslateTransform(0, options.Fly ? flyFrom?.Y ?? 0 : 0);
        el.RenderTransform = new TransformGroup { Children = { scale, translate } };
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.Opacity = 0;
        // 模糊（Tier 降级时不挂 Effect，Effect 属性留给调用方的静态阴影等用途）
        BlurEffect? blur = null;
        if (BlurSupported && options.BlurRadius > 0)
        {
            blur = new BlurEffect { Radius = options.BlurRadius };
            el.Effect = blur;
        }

        var dur = TimeSpan.FromMilliseconds(options.DurationMs);
        // 淡入比总时长快（240ms），保证"惯性滑出"发生时透明度已高、肉眼可见
        var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(
            options.FadeMs > 0 ? options.FadeMs : Math.Min(240, options.DurationMs))))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        // 缩放：全程平滑展开（0.5→1.0，70% 与落位同步到位），不过冲——
        // "惯性"由位置承担（见 BuildGlideBack），缩放过冲观感是"猛地鼓一下"，位置惯性才是落位弹回
        AnimationTimeline growTx, growTy;
        if (options.Elastic)
        {
            growTx = BuildScale(options, dur);
            growTy = BuildScale(options, dur);
        }
        else
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            growTx = new DoubleAnimation(options.StartScale, 1, new Duration(dur)) { EasingFunction = ease };
            growTy = new DoubleAnimation(options.StartScale, 1, new Duration(dur)) { EasingFunction = ease };
        }
        // 位置：直上直下——仅 Y 轴动画，惯性方向 = 飞行方向（-flyFrom.Y 归一化，2px）：
        // 单条 BackEase 全程连续（冲过落点 2px 再平滑弹回），替代三段关键帧拼接——
        // 消除拼接点（70%）速度归零再重启的接缝顿挫；Amplitude 由过冲比 |inertia/from| 反解。
        var inertiaY = CalcInertiaY(translate.Y);
        AnimationTimeline glideTy;
        if (options.Elastic)
        {
            glideTy = BuildGlideBack(translate.Y, inertiaY, dur);
        }
        else
        {
            glideTy = new DoubleAnimation(translate.Y, 0, new Duration(dur))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        }
        // 模糊渐清：匀速贯穿大部分动画（不设 EasingFunction = 默认线性，避免前段骤减），放大期间全程可见"模糊渐变"
        var clear = new DoubleAnimation(options.BlurRadius, 0, new Duration(TimeSpan.FromMilliseconds(options.BlurMs)));

        // 延迟入场（编排用）：所有时间线统一 BeginTime，延迟期间保持起始态（首帧即透明+偏移，不闪最终态）
        if (options.BeginMs > 0)
        {
            var bt = TimeSpan.FromMilliseconds(options.BeginMs);
            fadeIn.BeginTime = bt;
            growTx.BeginTime = bt;
            growTy.BeginTime = bt;
            glideTy.BeginTime = bt;
            clear.BeginTime = bt;
        }

        // 先挂完成回调再开播；主动画（growTx，总时长最晚）完成时释放 Effect
        if (blur is not null)
        {
            growTx.Completed += (_, _) =>
            {
                // 孤儿动画完成时不误清新动画的 Effect
                if (ReferenceEquals(el.Effect, blur)) el.Effect = null;
            };
        }
        el.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, growTx);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, growTy);
        translate.BeginAnimation(TranslateTransform.YProperty, glideTy);
        blur?.BeginAnimation(BlurEffect.RadiusProperty, clear);
    }

    /// <summary>
    /// 播放关闭动画。flyFrom = 打开时的抛出起点（DIP，相对最终位置，仅取 Y 分量）：非 null 时执行
    /// **打开动画的严格倒放**（时间反转 + 缓动反转）——先微动 2px（打开"弹回"的逆过程），
    /// 再沿垂直原路飞回起点，同时缩小/渐模糊/淡出，总时长与打开一致（480ms）。
    /// 完成后回调（调用方在回调里真正关窗）。幂等：已有收拢在跑时直接返回；
    /// 重新打开由 PlayOpen 取消。
    /// </summary>
    public static void PlayClose(FrameworkElement el, Action? done = null, CloseOptions? options = null,
        Point? flyFrom = null)
    {
        options ??= DefaultClose;
        if ((bool)el.GetValue(IsClosingProperty)) return; // 已有收拢在跑：由它完成关闭
        el.SetValue(IsClosingProperty, true);
        ResetVisual(el);

        if (!SystemParameters.MenuAnimation)
        {
            el.SetValue(IsClosingProperty, false);
            done?.Invoke();
            return;
        }

        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform(0, 0);
        el.RenderTransform = new TransformGroup { Children = { scale, translate } };
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.Opacity = 1;
        // 模糊（Tier 降级时跳过：仅渐隐 + 缩小/位移）
        BlurEffect? blur = null;
        if (BlurSupported)
        {
            blur = new BlurEffect { Radius = 0 };
            el.Effect = blur;
        }

        AnimationTimeline shrinkTx, shrinkTy;
        AnimationTimeline? glideTy;
        AnimationTimeline blurIn, fadeOut;
        if (flyFrom is { } from && from.Y != 0)
        {
            // ---- 倒放档：打开动画严格时间反转 ----
            var dur = TimeSpan.FromMilliseconds(DefaultOpen.DurationMs);
            var inertiaY = CalcInertiaY(from.Y);

            // Scale：1.0（0-30%）→ StartScale（100%，fast 反转）；X/Y 各一个动画实例
            shrinkTx = BuildScaleReverse(dur);
            shrinkTy = BuildScaleReverse(dur);
            // Translate：Y 轴单条 BackEase EaseIn 时间反转——开头向飞行方向顿 2px（打开"弹回"的逆）
            // 再连续垂直飞回起点，与打开侧同样无拼接接缝
            glideTy = new DoubleAnimation(0, from.Y, new Duration(dur))
            {
                EasingFunction = new BackEase
                { EasingMode = EasingMode.EaseIn, Amplitude = BackAmplitude(from.Y, inertiaY) }
            };
            // 模糊/淡出：BeginTime 延迟到后段（倒放时序；模糊匀速 = 打开线性渐清的逆，EaseIn = 打开 EaseOut 反转）
            blurIn = new DoubleAnimation(0, DefaultOpen.BlurRadius,
                new Duration(TimeSpan.FromMilliseconds(DefaultOpen.BlurMs)))
            { BeginTime = TimeSpan.FromMilliseconds(DefaultOpen.DurationMs - DefaultOpen.BlurMs) };
            fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(240)))
            { BeginTime = TimeSpan.FromMilliseconds(240),
              EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        }
        else
        {
            // ---- 原地收拢（无抛出起点时）：缩小 + 模糊 + 渐隐 ----
            var dur = TimeSpan.FromMilliseconds(options.DurationMs);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn }; // 加速收拢感
            shrinkTx = new DoubleAnimation(1, options.EndScale, new Duration(dur)) { EasingFunction = ease };
            shrinkTy = new DoubleAnimation(1, options.EndScale, new Duration(dur)) { EasingFunction = ease };
            glideTy = null;
            blurIn = new DoubleAnimation(0, options.BlurRadius, new Duration(dur)) { EasingFunction = ease };
            fadeOut = new DoubleAnimation(1, 0, new Duration(dur)) { EasingFunction = ease };
        }

        fadeOut.Completed += (_, _) =>
        {
            el.SetValue(IsClosingProperty, false);
            ResetVisual(el);
            done?.Invoke();
        };
        el.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrinkTx);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrinkTy);
        if (glideTy != null) translate.BeginAnimation(TranslateTransform.YProperty, glideTy);
        blur?.BeginAnimation(BlurEffect.RadiusProperty, blurIn);
    }

    /// <summary>恢复静止最终态：清动画时钟、清模糊、清变换、不透明。</summary>
    private static void ResetVisual(FrameworkElement el)
    {
        el.BeginAnimation(UIElement.OpacityProperty, null);
        el.RenderTransform = null;
        el.Effect = null;
        el.Opacity = 1;
    }

    /// <summary>打开动画参数档。</summary>
    public sealed class OpenOptions
    {
        public double DurationMs { get; set; } = 480;
        public double StartScale { get; set; } = 0.5;
        public double BlurRadius { get; set; } = 20;
        public double BlurMs { get; set; } = 420;
        public bool Elastic { get; set; } = true;
        public bool Fly { get; set; } = true;
        /// <summary>延迟入场（毫秒；编排主次有序用）。</summary>
        public double BeginMs { get; set; }
        /// <summary>淡入时长（毫秒；0 = 自动取 min(240, DurationMs)）。</summary>
        public double FadeMs { get; set; }
    }

    /// <summary>缩放关键帧：start→1.0（70% 与落位同步到位），此后保持——全程平滑，无过冲（惯性由位置承担）。</summary>
    private static DoubleAnimationUsingKeyFrames BuildScale(OpenOptions options, Duration dur)
    {
        var g = new DoubleAnimationUsingKeyFrames { Duration = dur };
        var fast = new KeySpline(0.2, 0.7, 0.3, 1.0);
        g.KeyFrames.Add(new SplineDoubleKeyFrame(options.StartScale, KeyTime.FromPercent(0.0), fast));
        g.KeyFrames.Add(new SplineDoubleKeyFrame(1.0, KeyTime.FromPercent(0.70), fast));
        g.KeyFrames.Add(new SplineDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0), fast));
        return g;
    }

    /// <summary>主运动轴（Y）位置动画：单条 BackEase EaseOut 全程连续——冲过落点（过冲 = 惯性 2px）
    /// 再平滑回弹。替代三段关键帧拼接（拼接点速度归零再重启产生顿挫）；过冲方向自动沿飞行方向。</summary>
    private static DoubleAnimation BuildGlideBack(double from, double inertia, Duration dur) =>
        new(from, 0, dur)
        {
            EasingFunction = new BackEase
            { EasingMode = EasingMode.EaseOut, Amplitude = BackAmplitude(from, inertia) }
        };

    /// <summary>BackEase 振幅反解：目标过冲 = |inertia|（2px），过冲比例 = |inertia/from|；
    /// BackEase 过冲 ≈ Amplitude × 0.22（实测 0.3→5.1% / 0.4→8.9% / 0.5→13.1%），clamp 防极端行程。</summary>
    private static double BackAmplitude(double from, double inertia)
    {
        double ratio = from != 0 ? Math.Abs(inertia / from) : 0.08;
        return Math.Clamp(ratio / 0.22, 0.15, 0.6);
    }

    /// <summary>缩放关键帧（倒放）：1.0（0-30%）→ StartScale（100%），缓动 = 打开 fast 段的时间反转。</summary>
    private static DoubleAnimationUsingKeyFrames BuildScaleReverse(Duration dur)
    {
        var g = new DoubleAnimationUsingKeyFrames { Duration = dur };
        var fastRev = new KeySpline(0.7, 0.0, 0.8, 0.3); // (0.2,0.7,0.3,1.0) 反转
        g.KeyFrames.Add(new SplineDoubleKeyFrame(1.0, KeyTime.FromPercent(0.0), fastRev));
        g.KeyFrames.Add(new SplineDoubleKeyFrame(1.0, KeyTime.FromPercent(0.30), fastRev));
        g.KeyFrames.Add(new SplineDoubleKeyFrame(DefaultOpen.StartScale, KeyTime.FromPercent(1.0), fastRev));
        return g;
    }

    /// <summary>惯性微动（沿飞行方向 2px，仅 Y 轴）：飞行方向 = -flyFrom.Y 归一化；打开过冲、关闭飞回前的顿点共用。</summary>
    private static double CalcInertiaY(double fromY) => fromY != 0 ? -Math.Sign(fromY) * 2 : 0;

    /// <summary>关闭动画参数档（原地收拢分支用；倒放分支参数取自 DefaultOpen）。</summary>
    public sealed class CloseOptions
    {
        public double DurationMs { get; set; } = 150;
        public double EndScale { get; set; } = 0.55;
        public double BlurRadius { get; set; } = 12;
    }
}
