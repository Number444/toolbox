using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Toolbox.Helpers;

/// <summary>
/// 交互控件边缘发光叠加层 —— 覆盖在主窗口内容之上（IsHitTestVisible=False，不拦截输入）。
/// 规则：
/// 1. 仅模拟"控件被鼠标光源照亮"：鼠标靠近时按距离逐渐增强，远离逐渐熄灭，
///    接触（hover）时接近过曝；无常亮微发光、无呼吸/脉冲、无环境光晕。
///    鼠标移出窗口或失焦瞬时熄灭（0ms）。
/// 2. 描边严格贴合控件自身模板边缘的渲染几何（取模板内首个 Border 的边界与圆角，
///    如 ComboBox 的 CornerRadius=5 外框），绑定控件本身，不落在父容器/布局面板上。
/// 3. 界面/工具切换时调用 ClearTargets 立即销毁全部发光，0ms 残留。
/// 4. 目标清单只存元素引用，坐标/可见性/滚动裁剪每帧实时重算——
///    滚动不触发 LayoutUpdated，缓存静态矩形会导致光晕停留在原地。
/// </summary>
public class EdgeGlowLayer : FrameworkElement
{
    /// <summary>鼠标光源影响半径（超过此距离的控件不发光）</summary>
    private const double GlowRadius = 120;

    /// <summary>接触（距离为 0）时的峰值不透明度，接近过曝</summary>
    private const double MaxAlpha = 0.9;

    /// <summary>高光描边宽度（硬切线，1~2px）</summary>
    private const double StrokeThickness = 1.5;

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

    /// <summary>仅 ButtonBase 子类（Button/ToggleButton/CheckBox/RadioButton）与 ComboBox 可发光，
    /// TextBlock/Border/Grid/StackPanel/分割线/背景层一律排除；
    /// ComboBox 模板内部的 ToggleButton 除外（发光由 ComboBox 自身承担，避免重复描边）</summary>
    private static bool IsGlowTarget(FrameworkElement fe)
    {
        if (!fe.IsVisible || fe.RenderSize.IsEmpty) return false;
        if (fe is ComboBox) return true;
        return fe is ButtonBase && FindAncestor<ComboBox>(fe) == null;
    }

    private void AddTarget(FrameworkElement fe)
    {
        // 发光绑定控件自身的渲染边缘：取控件模板视觉树中的首个 Border
        // （其边界与 CornerRadius 即控件实际的玻璃边缘，如 ComboBox 的 5px 圆角外框），
        // 找不到则退回控件外框（直角）
        FrameworkElement edgeSource = fe;
        var radius = new CornerRadius(0);
        if (FindFirstBorder(fe) is Border templateEdge)
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

    /// <summary>实时计算目标当前在本层坐标系中的可见矩形（含滚动视口与层边界裁剪），
    /// 不可见/已脱离视觉树返回 null</summary>
    private Rect? ComputeVisibleBounds(GlowTarget target, Rect layerBounds)
    {
        if (!target.Owner.IsVisible) return null;

        Rect bounds;
        try
        {
            bounds = target.Edge.TransformToVisual(this).TransformBounds(
                new Rect(target.Edge.RenderSize));
        }
        catch (InvalidOperationException)
        {
            return null; // 元素已脱离视觉树（切换/销毁过程中）
        }

        // 滚动裁剪：与最近祖先 ScrollViewer 的视口矩形求交，滚出视口的部分不画
        if (target.Viewport != null)
        {
            try
            {
                var viewport = target.Viewport.TransformToVisual(this).TransformBounds(
                    new Rect(target.Viewport.RenderSize));
                bounds.Intersect(viewport);
            }
            catch (InvalidOperationException) { }
        }

        bounds.Intersect(layerBounds);
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return null;
        return bounds;
    }

    /// <summary>遮挡检测：多点采样命中测试，只有所有采样点都被非控件元素盖住才判定为遮挡。
    /// 鼠标直接在控件上时必然未遮挡（快速路径，修复鼠标接近/hover 时误杀发光的问题）</summary>
    private bool IsOccluded(GlowTarget target, Rect bounds)
    {
        if (_root == null) return false;

        // 鼠标直接在控件范围内：控件必然在最上层，无需检测
        if (bounds.Contains(_cursorPos)) return false;

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
            if (ComputeVisibleBounds(target, layerBounds) is not Rect bounds) continue;

            // 控件被鼠标光源照亮：按到边缘的距离逐渐增强，接触时接近过曝
            double d = DistanceToRect(_cursorPos, bounds);
            if (d >= GlowRadius) continue;

            double t = 1 - d / GlowRadius;
            double alpha = t * t * MaxAlpha;
            if (alpha < 0.01) continue;

            // 被上层界面盖住的控件不透出（发光层位于最顶层，需主动检测遮挡）
            if (IsOccluded(target, bounds)) continue;

            // 单条 1.5px 硬切高光线，圆角逐像素贴合控件模板边缘，无柔光无羽化
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(
                (byte)(alpha * 255), 255, 255, 255)), StrokeThickness);
            pen.Freeze();
            dc.DrawRoundedRectangle(null, pen, bounds, target.Radius.TopLeft, target.Radius.TopLeft);
        }
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
