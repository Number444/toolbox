using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Toolbox.Tools.Services;

namespace Toolbox.Controls;

/// <summary>
/// 悬浮窗贴边后的触发条控件。
/// 梯形圆角外形 + 方向箭头，暴露 SetDirection 方法供外部切换方向。
/// </summary>
public partial class DockTriggerBar : UserControl
{
    /// <summary>用户拖拽触发条时触发。</summary>
    public event EventHandler? DragRequested;

    public DockTriggerBar()
    {
        InitializeComponent();

        // 鼠标左键按下触发拖拽
        MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            try { DragRequested?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { Debug.WriteLine($"[DockTriggerBar] DragRequested 异常: {ex.Message}"); }
        };
    }

    /// <summary>
    /// 设置触发条方向（梯形窄边朝向屏幕外，箭头指向屏幕内）。
    /// </summary>
    public void SetDirection(DockDirection direction)
    {
        if (direction == DockDirection.Left)
        {
            // 左贴边 → 窗口可见右侧 14px → 触发条靠右对齐
            ShapePath.Data = BuildLeftDockGeometry();
            ArrowPath.Data = BuildRightArrowGeometry();
            HorizontalAlignment = HorizontalAlignment.Right;
        }
        else
        {
            // 右贴边 → 窗口可见左侧 14px → 触发条靠左对齐
            ShapePath.Data = BuildRightDockGeometry();
            ArrowPath.Data = BuildLeftArrowGeometry();
            HorizontalAlignment = HorizontalAlignment.Left;
        }
    }

    // ── 左贴梯形（窄边在左，宽边在右）──

    private static Geometry BuildLeftDockGeometry()
    {
        var geo = new StreamGeometry { FillRule = FillRule.Nonzero };
        using var ctx = geo.Open();

        const double topY = 0;
        const double bottomY = 10000;
        const double midOffset = 4;

        ctx.BeginFigure(new Point(0, topY), isFilled: true, isClosed: true);
        ctx.LineTo(new Point(3, topY), isSmoothJoin: false, isStroked: true);
        ctx.LineTo(new Point(14, topY + midOffset), isSmoothJoin: false, isStroked: true);
        ctx.LineTo(new Point(14, bottomY - midOffset), isSmoothJoin: false, isStroked: true);
        ctx.LineTo(new Point(3, bottomY), isSmoothJoin: false, isStroked: true);
        ctx.LineTo(new Point(0, bottomY), isSmoothJoin: false, isStroked: true);

        geo.Freeze();
        return geo;
    }

    // 右贴：左宽右窄，与左贴镜像
    private static Geometry BuildRightDockGeometry()
    {
        var geo = new StreamGeometry { FillRule = FillRule.Nonzero };
        using var ctx = geo.Open();

        const double topY = 0;
        const double bottomY = 10000;
        const double midOffset = 4;

        ctx.BeginFigure(new Point(14, topY), isFilled: true, isClosed: true);
        ctx.LineTo(new Point(11, topY), isSmoothJoin: false, isStroked: true);
        ctx.LineTo(new Point(0, topY + midOffset), isSmoothJoin: false, isStroked: true);
        ctx.LineTo(new Point(0, bottomY - midOffset), isSmoothJoin: false, isStroked: true);
        ctx.LineTo(new Point(11, bottomY), isSmoothJoin: false, isStroked: true);
        ctx.LineTo(new Point(14, bottomY), isSmoothJoin: false, isStroked: true);

        geo.Freeze();
        return geo;
    }

    // 右箭头（左贴时指向屏幕内侧）
    private static Geometry BuildRightArrowGeometry()
    {
        var geo = new StreamGeometry { FillRule = FillRule.Nonzero };
        using var ctx = geo.Open();

        ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
        ctx.LineTo(new Point(6, 3), isSmoothJoin: false, isStroked: true);
        ctx.LineTo(new Point(0, 6), isSmoothJoin: false, isStroked: true);

        geo.Freeze();
        return geo;
    }

    // 左箭头（右贴时指向屏幕内侧）
    private static Geometry BuildLeftArrowGeometry()
    {
        var geo = new StreamGeometry { FillRule = FillRule.Nonzero };
        using var ctx = geo.Open();

        ctx.BeginFigure(new Point(6, 0), isFilled: true, isClosed: true);
        ctx.LineTo(new Point(0, 3), isSmoothJoin: false, isStroked: true);
        ctx.LineTo(new Point(6, 6), isSmoothJoin: false, isStroked: true);

        geo.Freeze();
        return geo;
    }
}