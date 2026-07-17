using System;
using System.Windows;
using System.Windows.Input;
using Toolbox.Controls;
using Toolbox.Tools.Services;

namespace Toolbox.Tools.Views;

/// <summary>
/// 纯透明悬浮窗（AllowsTransparency=True，无 DWM 背景效果）。
/// 毛玻璃开关关闭时使用。
/// </summary>
public partial class TransparentMusicWindow : Window
{
    private bool _isLocked;

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
    public void SetEdgeDockService(EdgeDockService service) { }

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
            DragMove();
            DragMoveCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var isLeft = Left <= screenWidth / 2.0;
        MusicContent.SetAlignmentFromParent(isLeft);
    }
}
