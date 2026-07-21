using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Toolbox.Models;

namespace Toolbox.Core.Helpers;

/// <summary>
/// 交互控件边缘发光叠加层 —— 覆盖在窗口内容之上（IsHitTestVisible=False，不拦截输入）。
/// 位于 Toolbox.Core：主窗口与插件内的悬浮窗（MusicContentControl）共用。
/// 规则：
/// 1. 仅模拟"控件被鼠标光源照亮"：鼠标靠近时按距离逐渐增强，远离逐渐熄灭，
///    接触（hover）时接近过曝；无常亮微发光、无呼吸/脉冲、无环境光晕。
///    鼠标移出窗口或失焦瞬时熄灭（0ms）。
///    描边亮度沿边框按到鼠标的距离线性衰减——只显示被鼠标照亮的部分边框，
///    背光侧完全熄灭，模拟灯光扫过物体（径向渐变画笔，中心即光标）。
/// 2. 描边严格贴合控件自身模板边缘的渲染几何（取模板内首个 Border 的边界与圆角，
///    如 ComboBox 的 CornerRadius=5 外框），绑定控件本身，不落在父容器/布局面板上。
/// 3. 界面/工具切换时调用 ClearTargets 立即销毁全部发光，0ms 残留。
/// 4. 目标清单只存元素引用，坐标/可见性/滚动裁剪每帧实时重算——
///    滚动不触发 LayoutUpdated，缓存静态矩形会导致光晕停留在原地。
/// 5. 绘制的是控件【完整】边缘几何，再用视口裁剪区域 PushClip 裁掉不可见部分——
///    绝不把视口边缘误当成控件边缘描出"假边"（长卡片滚出视口时底部假亮边的修复）。
/// </summary>
public class EdgeGlowLayer : FrameworkElement
{
    /// <summary>鼠标光源影响半径（超过此距离的控件不发光）</summary>
    private const double GlowRadius = 120;

    /// <summary>接触（距离为 0）时的峰值不透明度，完全过曝</summary>
    private const double MaxAlpha = 1.0;

    /// <summary>高光描边宽度（硬切线，1~2px）</summary>
    private const double StrokeThickness = 2;

    /// <summary>高光环亮度衰减半径系数：以小控件长边为基准（大控件由 MaxLitRadius 截断）</summary>
    private const double LitRadiusFactor = 0.9;

    /// <summary>照亮半径上限（px）——与鼠标光晕/控件感应半径（GlowRadius=120）同一量级，
    /// 大卡片/大输入框的被照亮弧段不会超过这个范围，亮边大小与小控件一致</summary>
    private const double MaxLitRadius = 100;

    /// <summary>径向渐变色标数量（线性衰减，分段越多越细腻）</summary>
    private const int GradientStopCount = 10;

    /// <summary>渐变衰减指数：小于 1 时衰减前段放缓，被照亮的弧段整体更亮</summary>
    private const double GradientFalloffExponent = 0.6;

    /// <summary>渐变亮度增益：大于 1 时近光心一段形成过曝平台（截断到 255），峰值更亮</summary>
    private const double GradientBoost = 1.3;

    /// <summary>发光目标：只存元素引用，几何每帧实时换算（滚动/显隐变化即时跟随）</summary>
    private sealed class GlowTarget
    {
        public required FrameworkElement Owner;   // 控件本身（可见性判断）
        public required FrameworkElement Edge;    // 模板边缘元素（坐标 + 圆角来源）
        public CornerRadius Radius;
        public ScrollViewer? Viewport;            // 最近滚动容器（裁剪用）
    }

    private readonly List<GlowTarget> _targets = new();
    private Point _cursorPos;
    private bool _cursorInside;
    private Visual? _root;   // 命中测试根（遮挡检测用）

    /// <summary>每帧更新鼠标原始位置（无插值滞后，保证移出窗口瞬时熄灭），触发重绘</summary>
    public void UpdateCursor(Point pos, bool inside)
    {
        _cursorPos = pos;
        _cursorInside = inside;
        InvalidateVisual();
    }

    /// <summary>立即销毁全部发光目标（界面/工具切换时调用，0ms 残留）</summary>
    public void ClearTargets()
    {
        _targets.Clear();
        InvalidateVisual();
    }

    /// <summary>遍历视觉树，重建可发光目标清单（只存引用，不存坐标）</summary>
    public void RebuildTargets(Visual root)
    {
        _root = root;
        _targets.Clear();
        CollectTargets(root);
    }

    private void CollectTargets(Visual node)
    {
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i) as Visual;
            if (child == null) continue;

            // 跳过本层自身，避免收集自己
            if (child is EdgeGlowLayer) continue;

            if (child is FrameworkElement fe && IsGlowTarget(fe))
            {
                AddTarget(fe);
            }

            CollectTargets(child);
        }
    }

    /// <summary>仅 ButtonBase 子类（Button/ToggleButton/CheckBox/RadioButton）、ComboBox、
    /// TextBox 与被 GlowCardMarker 显式标记的卡片 Border 可发光，
    /// TextBlock/Grid/StackPanel/分割线/背景层一律排除；
    /// ComboBox 模板内部的 ToggleButton 除外（发光由 ComboBox 自身承担，避免重复描边）；
    /// 控件模板内的 Border 与卡片 Border 不会混淆——只有显式标记的卡片才被收录</summary>
    private static bool IsGlowTarget(FrameworkElement fe)
    {
        if (!fe.IsVisible || fe.RenderSize.IsEmpty) return false;
        if (fe is ComboBox) return true;
        if (fe is TextBox) return true;
        if (fe is Border border && GlowCardMarker.GetIsGlowCard(border)) return true;
        return fe is ButtonBase && FindAncestor<ComboBox>(fe) == null;
    }

    private void AddTarget(FrameworkElement fe)
    {
        // 发光绑定控件自身的渲染边缘：取控件模板视觉树中的首个 Border
        // （其边界与 CornerRadius 即控件实际的玻璃边缘，如 ComboBox 的 5px 圆角外框），
        // 找不到则退回控件外框（直角）；被标记的卡片 Border 自身即边缘元素
        FrameworkElement edgeSource = fe;
        var radius = new CornerRadius(0);
        if (fe is Border cardBorder && GlowCardMarker.GetIsGlowCard(cardBorder))
        {
            radius = cardBorder.CornerRadius;
        }
        else if (FindFirstBorder(fe) is Border templateEdge)
        {
            edgeSource = templateEdge;
            radius = templateEdge.CornerRadius;
        }

        _targets.Add(new GlowTarget
        {
            Owner = fe,
            Edge = edgeSource,
            Radius = radius,
            Viewport = FindAncestor<ScrollViewer>(fe)
        });
    }

    /// <summary>在控件模板视觉树中广度优先找首个 Border（模板的边缘元素）</summary>
    private static Border? FindFirstBorder(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is Border border) return border;
                queue.Enqueue(child);
            }
        }
        return null;
    }

    /// <summary>沿视觉树向上找最近的 T 类型祖先</summary>
    private static T? FindAncestor<T>(DependencyObject node) where T : class
    {
        while (node is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            node = VisualTreeHelper.GetParent(node);
            if (node is T match) return match;
        }
        return null;
    }

    /// <summary>实时计算目标在本层坐标系中的【完整】矩形与可见裁剪区域（滚动视口 ∩ 层边界）。
    /// full 用于描边（真实控件边缘），clip 用于裁剪绘制——滚出视口的部分被裁掉，
    /// 但视口边缘绝不会被误当成控件边缘描出假边。控件不可见/脱离视觉树返回 null</summary>
    private (Rect full, Rect clip, Rect visible)? ComputeVisibleBounds(GlowTarget target, Rect layerBounds)
    {
        if (!target.Owner.IsVisible) return null;

        Rect full;
        try
        {
            full = target.Edge.TransformToVisual(this).TransformBounds(
                new Rect(target.Edge.RenderSize));
        }
        catch (InvalidOperationException)
        {
            return null; // 元素已脱离视觉树（切换/销毁过程中）
        }

        // 可见裁剪区域：层边界 ∩ 最近祖先 ScrollViewer 的视口矩形
        var clip = layerBounds;
        if (target.Viewport != null)
        {
            try
            {
                var viewport = target.Viewport.TransformToVisual(this).TransformBounds(
                    new Rect(target.Viewport.RenderSize));
                clip.Intersect(viewport);
            }
            catch (InvalidOperationException) { }
        }

        // 完整矩形与裁剪区域无交集：目标完全不可见
        var visible = full;
        visible.Intersect(clip);
        if (visible.IsEmpty || visible.Width <= 0 || visible.Height <= 0) return null;
        return (full, clip, visible);
    }

    /// <summary>遮挡检测：多点采样命中测试，只有所有采样点都被非控件元素盖住才判定为遮挡。
    /// 鼠标在控件的【可见区域】内时必然未遮挡（快速路径，修复鼠标接近/hover 时误杀发光的问题）；
    /// 注意快速路径用可见区域而非完整矩形——长卡片滚出视口后，鼠标在视口外
    /// （如状态栏）时不能跳过遮挡检测</summary>
    private bool IsOccluded(GlowTarget target, Rect bounds, Rect visible)
    {
        if (_root == null) return false;

        // 鼠标直接在控件可见区域内：控件必然在最上层，无需检测
        if (visible.Contains(_cursorPos)) return false;

        // 中心 + 四角（20% 内缩）多点采样：任意一点能看到控件即未遮挡，
        // 全部命中其他元素（弹窗/设置层等）才判定遮挡
        double ix = bounds.Width * 0.2, iy = bounds.Height * 0.2;
        Point[] samples =
        [
            new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2),
            new(bounds.Left + ix, bounds.Top + iy),
            new(bounds.Right - ix, bounds.Top + iy),
            new(bounds.Left + ix, bounds.Bottom - iy),
            new(bounds.Right - ix, bounds.Bottom - iy),
        ];

        int covered = 0;
        foreach (var ptLayer in samples)
        {
            var hit = HitTestAt(ptLayer);
            if (hit == null) continue;                          // 未命中：无遮挡证据
            if (IsSelfOrDescendant(hit, target.Owner)) return false;  // 能看到控件
            covered++;
        }
        return covered > 0;
    }

    /// <summary>在根视觉树上对指定点（本层坐标系）做命中测试，返回最上层命中元素</summary>
    private DependencyObject? HitTestAt(Point ptLayer)
    {
        Point ptRoot;
        try
        {
            ptRoot = TransformToVisual(_root!).Transform(ptLayer);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        DependencyObject? hit = null;
        VisualTreeHelper.HitTest(_root,
            r =>
            {
                // 跳过不可见或非交互元素的【整个子树】（EdgeGlowLayer / HaloLayer 及其
                // 内部的跟随光晕椭圆等）。必须 AndChildren：ContinueSkipSelf 只跳过自身、
                // 子树仍会被遍历——光晕椭圆（IsHitTestVisible 默认 true、跟随鼠标、z 序最高）
                // 会成为命中结果，污染遮挡判定（控件被误判为"被光晕遮挡"而熄灭）。
                if (r is UIElement ue && (!ue.IsVisible || !ue.IsHitTestVisible))
                    return HitTestFilterBehavior.ContinueSkipSelfAndChildren;
                return HitTestFilterBehavior.Continue;
            },
            r => { hit = r.VisualHit; return HitTestResultBehavior.Stop; },
            new PointHitTestParameters(ptRoot));
        return hit;
    }

    /// <summary>判断命中元素是否为指定控件自身或其视觉子元素</summary>
    private static bool IsSelfOrDescendant(DependencyObject hit, DependencyObject owner)
    {
        DependencyObject? node = hit;
        while (node != null)
        {
            if (node == owner) return true;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node) : null;
        }
        return false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        // 鼠标不在窗口内/窗口失焦：无任何发光（0ms 熄灭）
        if (!_cursorInside || _targets.Count == 0) return;

        var layerBounds = new Rect(0, 0, ActualWidth, ActualHeight);

        foreach (var target in _targets)
        {
            // 每帧实时换算坐标与裁剪：滚动/折叠/显隐变化即时生效，光晕不会滞留原地
            if (ComputeVisibleBounds(target, layerBounds) is not var (bounds, clip, visible)) continue;

            // 控件被鼠标光源照亮：按到边缘的距离逐渐增强，接触时接近过曝
            double d = DistanceToRect(_cursorPos, bounds);
            if (d >= GlowRadius) continue;

            double t = 1 - d / GlowRadius;
            double alpha = t * t * MaxAlpha;
            if (alpha < 0.01) continue;

            // 被上层界面盖住的控件不透出（发光层位于最顶层，需主动检测遮挡）
            if (IsOccluded(target, bounds, visible)) continue;

            // 只照亮被鼠标照到的部分边框：描边画笔改为以鼠标为中心的径向渐变
            // （绝对坐标映射，渐变中心即本层坐标系中的光标位置），离鼠标近的边框段亮、
            // 沿边框向两侧线性衰减，背光侧完全熄灭——模拟灯光扫过物体的局部照亮。
            // alpha 作为控件级整体强度（随鼠标距离二次衰减）乘到每个色标上。
            // 衰减取 (1-offset)^0.6 × 1.3 增益（截断到 255）：近光心形成过曝平台，
            // 亮弧更亮，尾端仍归零（背光侧熄灭）。
            // 照亮半径取控件尺寸与 MaxLitRadius 的较小值：大卡片/大输入框的亮弧
            // 与小控件同样只覆盖鼠标光晕大小的一段，不会整圈被照亮。
            double litRadius = Math.Min(
                Math.Max(bounds.Width, bounds.Height) * LitRadiusFactor + 24, MaxLitRadius);
            var brush = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = _cursorPos,
                GradientOrigin = _cursorPos,
                RadiusX = litRadius,
                RadiusY = litRadius
            };
            for (int i = 0; i < GradientStopCount; i++)
            {
                double offset = (double)i / (GradientStopCount - 1);
                byte a = (byte)Math.Min(255,
                    alpha * Math.Pow(1 - offset, GradientFalloffExponent) * GradientBoost * 255);
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(a, 255, 255, 255), offset));
            }
            // 单条 2px 硬切高光线，圆角逐像素贴合控件模板边缘，无柔光无羽化；
            // 逐角取半径构造几何（支持上直下圆等异径圆角，如标题栏按钮）。
            // 绘制【完整】控件边缘，用 PushClip 裁到可见区域：滚出视口的边缘被裁掉，
            // 视口边界处不会出现假描边（长卡片底部被遮挡区域误照亮的修复）。
            // 对"滚出视口"的那一侧再叠加渐隐遮罩：侧边在没入视口边界前逐渐变暗，
            // 不会以全亮度硬切在遮挡边界上（被遮挡部分侧边透出的修复）
            var pen = new Pen(brush, StrokeThickness);
            pen.Freeze();
            var geometry = BuildRoundedRectGeometry(bounds, target.Radius);
            geometry.Freeze();
            dc.PushClip(new RectangleGeometry(clip));
            var mask = BuildClipFadeMask(bounds, clip);
            if (mask != null) dc.PushOpacityMask(mask);
            dc.DrawGeometry(null, pen, geometry);
            if (mask != null) dc.Pop();
            dc.Pop();
        }
    }

    /// <summary>视口边缘渐隐区高度（px）：发光在没入视口边界前这段距离内线性淡出</summary>
    private const double ClipFadeLength = 32;

    /// <summary>构造视口被滚出侧的垂直渐隐遮罩：控件上/下边缘滚出视口时，
    /// 靠近该侧视口边界的描边逐渐透明，避免全亮度硬切透出；未滚出则返回 null</summary>
    private static LinearGradientBrush? BuildClipFadeMask(Rect full, Rect clip)
    {
        bool fadeTop = full.Top < clip.Top - 0.5;
        bool fadeBottom = full.Bottom > clip.Bottom + 0.5;
        if (!fadeTop && !fadeBottom) return null;
        if (clip.Height <= 0) return null;

        double fadeRatio = Math.Min(ClipFadeLength / clip.Height, 0.5);
        var mask = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, clip.Top),
            EndPoint = new Point(0, clip.Bottom)
        };
        var white = Color.FromArgb(255, 255, 255, 255);
        var clear = Color.FromArgb(0, 255, 255, 255);
        // 顶部：滚出则 透明→白 渐隐，未滚出则全程白
        mask.GradientStops.Add(new GradientStop(fadeTop ? clear : white, 0));
        if (fadeTop) mask.GradientStops.Add(new GradientStop(white, fadeRatio));
        // 底部：滚出则 白→透明 渐隐，未滚出则全程白
        if (fadeBottom) mask.GradientStops.Add(new GradientStop(white, 1 - fadeRatio));
        mask.GradientStops.Add(new GradientStop(fadeBottom ? clear : white, 1));
        mask.Freeze();
        return mask;
    }

    /// <summary>构造逐角异径的圆角矩形轮廓几何（某角半径为 0 时该角为直角）</summary>
    private static StreamGeometry BuildRoundedRectGeometry(Rect r, CornerRadius cr)
    {
        // 圆角半径不得超过边长的一半（与 WPF Border 的裁剪行为一致）
        double maxR = Math.Min(r.Width, r.Height) / 2;
        double tl = Math.Min(cr.TopLeft, maxR);
        double tr = Math.Min(cr.TopRight, maxR);
        double br = Math.Min(cr.BottomRight, maxR);
        double bl = Math.Min(cr.BottomLeft, maxR);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            // 从左上角弧终点开始，顺时针：上边 → 右上角 → 右边 → 右下角 → 底边 → 左下角 → 左边 → 左上角
            ctx.BeginFigure(new Point(r.Left + tl, r.Top), true, true);
            // 上边 → 右上角
            ctx.LineTo(new Point(r.Right - tr, r.Top), true, false);
            if (tr > 0) ctx.ArcTo(new Point(r.Right, r.Top + tr), new Size(tr, tr), 0, false, SweepDirection.Clockwise, true, false);
            // 右边 → 右下角
            ctx.LineTo(new Point(r.Right, r.Bottom - br), true, false);
            if (br > 0) ctx.ArcTo(new Point(r.Right - br, r.Bottom), new Size(br, br), 0, false, SweepDirection.Clockwise, true, false);
            // 底边 → 左下角
            ctx.LineTo(new Point(r.Left + bl, r.Bottom), true, false);
            if (bl > 0) ctx.ArcTo(new Point(r.Left, r.Bottom - bl), new Size(bl, bl), 0, false, SweepDirection.Clockwise, true, false);
            // 左边 → 左上角
            ctx.LineTo(new Point(r.Left, r.Top + tl), true, false);
            if (tl > 0) ctx.ArcTo(new Point(r.Left + tl, r.Top), new Size(tl, tl), 0, false, SweepDirection.Clockwise, true, false);
        }
        return geo;
    }

    /// <summary>点到矩形最近点距离（点在矩形内部时为 0）</summary>
    private static double DistanceToRect(Point p, Rect r)
    {
        double dx = Math.Max(r.Left - p.X, 0) + Math.Max(p.X - r.Right, 0);
        double dy = Math.Max(r.Top - p.Y, 0) + Math.Max(p.Y - r.Bottom, 0);
        // 内部：dx=dy=0；外部：欧氏距离
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
