using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Toolbox.Core.Models;
using Toolbox.Plugins.Controls;
using Toolbox.Tools.Helpers;
using Toolbox.Tools.Services;

namespace Toolbox.Tools.Views;

/// <summary>
/// 纯透明悬浮窗（AllowsTransparency=True，无 DWM 背景效果）。
/// 毛玻璃开关关闭时使用。
/// </summary>
public partial class TransparentMusicWindow : Window
{
    private bool _isLocked;
    private EdgeDockService? _edgeDock;

    public TransparentMusicWindow()
    {
        InitializeComponent();

        MusicContent.SizeRequired += OnSizeRequired;
        MusicContent.DragRequested += OnDragRequested;
        LocationChanged += OnWindowLocationChanged;
    }

    public FloatSizeMode SizeMode
    {
        get => MusicContent.SizeMode;
        set => MusicContent.SizeMode = value;
    }

    public DockTriggerBar TriggerBar => DockTriggerBar;

    public event EventHandler? DragMoveCompleted;

    /// <summary>由 EdgeDockService 在 Attach 时设置，用于 MouseLeave 缩回检测。</summary>
    public void SetEdgeDockService(EdgeDockService service) => _edgeDock = service;

    public void SetWindowLocked(bool locked) => _isLocked = locked;

    private void OnSizeRequired(object? sender, (double Width, double Height) size)
    {
        Width = size.Width;
        Height = size.Height;
    }

    private void OnDragRequested(object? sender, EventArgs e)
    {
        if (!_isLocked && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TransparentMusicWindow] DragMove 失败: {ex.Message}");
            }
            DragMoveCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        // 按窗口所在显示器的中心判断左右侧（多显示器安全）
        var wa = MonitorHelper.GetMonitorWorkAreaDips(this);
        var isLeft = Left <= wa.Left + wa.Width / 2.0;
        MusicContent.SetAlignmentFromParent(isLeft);
    }

    // ═══════════════════════════════════════════════════════════
    // 点击穿透（游戏模式）—— 共享实现见 ClickThroughHelper
    // 本窗口是 layered（AllowsTransparency），样式 mask 含 WS_EX_TRANSPARENT。
    // ═══════════════════════════════════════════════════════════

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // HWND 在此时创建完毕，是注册钩子和修改扩展样式的最早可靠时机
        ClickThroughHelper.OnSourceInitialized(this, layered: true);
    }

    /// <summary>
    /// 开启/关闭鼠标点击穿透（游戏模式）。开启后鼠标事件直接落到下层窗口（游戏），
    /// 悬浮窗变成纯信息展示，不可拖拽、不可交互、不可被激活。
    /// </summary>
    public void SetClickThrough(bool enabled)
        => ClickThroughHelper.SetClickThrough(this, enabled, layered: true);
}
