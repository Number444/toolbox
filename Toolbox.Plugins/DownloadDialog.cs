using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Toolbox.Core.Controls;
using Toolbox.Models;

namespace Toolbox.Tools;

/// <summary>
/// 自绘下载弹窗（无边框深色风格，与 ConfirmDialog 一致）。
/// 用于需要显示下载进度、成功/失败反馈的场景。
/// 用法：
///   var dialog = new DownloadDialog("标题", "说明文字");
///   dialog.Show();
///   // 在下载循环中调用 dialog.ReportProgress(percent, status);
///   // 完成后调用 dialog.SetResult(success, message);
///   // success=true 时弹窗自动关闭；失败时用户需点"关闭"手动关闭
/// </summary>
public sealed class DownloadDialog : Window
{
    /// <summary>下载是否成功完成</summary>
    public bool Success { get; private set; }

    /// <summary>用户是否取消了下载</summary>
    public bool Cancelled { get; private set; }

    /// <summary>下载取消令牌：点"取消"或按 Esc 时触发，由调用方传给下载方法使取消真正生效</summary>
    public CancellationToken Token => _cts.Token;

    // 不显式 Dispose：取消信号传播期间令牌仍可能被异步下载读取，显式释放有 ObjectDisposedException 竞争；
    // 该 CTS 无定时器/句柄，随弹窗一起被 GC 回收
    private readonly CancellationTokenSource _cts = new();

    private readonly ProgressBar _progressBar;
    private readonly TextBlock _statusText;
    private readonly TextBlock _resultText;
    private readonly Button _actionButton;
    private bool _isComplete;

    /// <summary>开/关动画抛出起点（dsh-app 菜单同款：上方 24px 抛出落位，与 ConfirmDialog 一致）。</summary>
    private static readonly Point FlyFrom = new(0, -24);

    /// <summary>动画安全区（与 ConfirmDialog 同款）：抛出位移/过冲超出窗口客户区的部分会被
    /// AllowsTransparency 分层窗口裁切，四周留 40px 透明余量（视觉不可见）。</summary>
    private const double AnimSafePad = 40;

    private readonly Border _mainBorder;
    /// <summary>关闭动画播完置位后真正关窗（OnClosing 首次拦截播倒放动画）。</summary>
    private bool _allowClose;

    public DownloadDialog(string title, string description)
    {
        Title = title;
        Width = 500; // 卡片 420 + 两侧动画安全区 40×2
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current?.MainWindow;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        var darkBg = ThemeColors.BgDark;
        var textPrimary = ThemeColors.TextPrimary;
        var textSecondary = Color.FromRgb(0xC0, 0xC0, 0xC0); // 与 ConfirmDialog 一致，比全局次要文本更亮
        var dangerColor = ThemeColors.Danger;
        var successColor = ThemeColors.Success;
        var borderColor = ThemeColors.BorderSubtle;

        var mainBorder = new Border
        {
            Background = new SolidColorBrush(darkBg),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(AnimSafePad),
        };
        _mainBorder = mainBorder;

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

        // 标题
        root.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(textPrimary),
            Margin = new Thickness(0, 0, 0, 10)
        });

        // 说明
        root.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 13,
            Foreground = new SolidColorBrush(textSecondary),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            Margin = new Thickness(0, 0, 0, 16)
        });

        // 进度条
        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 6,
            Foreground = new SolidColorBrush(successColor),
            Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 8)
        };
        root.Children.Add(_progressBar);

        // 进度百分比 / 状态详情
        _statusText = new TextBlock
        {
            Text = "准备下载…",
            FontSize = 12,
            Foreground = new SolidColorBrush(textSecondary),
            Margin = new Thickness(0, 0, 0, 6)
        };
        root.Children.Add(_statusText);

        // 结果提示（成功/失败，初始隐藏）
        _resultText = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(_resultText);

        // 按钮行
        _actionButton = new Button
        {
            Content = "取消下载",
            Width = 90,
            Height = 32,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
            Foreground = new SolidColorBrush(textPrimary),
            BorderBrush = new SolidColorBrush(borderColor),
            IsCancel = true
        };
        _actionButton.Click += OnActionButtonClick;
        root.Children.Add(_actionButton);

        mainBorder.Child = root;
        Content = mainBorder;

        // 打开动画（dsh-app 菜单同款：抛出回弹 + 放大 + 模糊渐清）。
        // Loaded 再开播：构造内开播时钟会先行消耗几十毫秒，窗口首帧看不到抛出起点；Loaded 开播首帧即起点不闪帧
        Loaded += (_, _) => PopupAnimator.PlayOpen(mainBorder, FlyFrom);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !_isComplete)
                CancelAndClose();
        };
    }

    /// <summary>
    /// 关闭先播倒放动画（打开的严格时间反转，与 ConfirmDialog 一致）：首次 Closing 取消关窗、
    /// 播收拢动画，完成后回调里置 _allowClose 真正关窗。系统关闭"菜单动画"时 PlayClose 立即回调 = 直接关。
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        // Dispatcher.HasShutdownStarted：应用退出正在关窗时必须放行，否则动画拦截会中止 Shutdown
        if (!_allowClose && IsLoaded
            && !Application.Current!.Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            PopupAnimator.PlayClose(_mainBorder, () =>
            {
                _allowClose = true;
                Close();
            }, null, FlyFrom);
        }
        base.OnClosing(e);
    }

    /// <summary>
    /// 更新下载进度（线程安全，可在后台线程调用）。
    /// percent：0-100；status：简短状态文字
    /// </summary>
    public void ReportProgress(int percent, string status)
    {
        Dispatcher.Invoke(() =>
        {
            if (_isComplete) return;
            _progressBar.Value = percent;
            _statusText.Text = status;
        });
    }

    /// <summary>
    /// 设置下载结果。
    /// success=true：绿色提示，约 1.5 秒后自动关闭；
    /// success=false：红色提示，按钮变为"关闭"，用户手动关闭。
    /// </summary>
    public void SetResult(bool success, string message)
    {
        Dispatcher.Invoke(async () =>
        {
            _isComplete = true;
            _progressBar.Value = success ? 100 : 0;
            _statusText.Visibility = Visibility.Collapsed;
            _resultText.Text = message;
            _resultText.Foreground = new SolidColorBrush(success ? ThemeColors.Success : ThemeColors.Danger);
            _resultText.Visibility = Visibility.Visible;
            _actionButton.Content = "关闭";

            Success = success;

            if (success)
            {
                // 成功时自动关闭
                await Task.Delay(1500);
                if (IsLoaded)
                    Close();
            }
        });
    }

    private void OnActionButtonClick(object sender, RoutedEventArgs e)
    {
        if (!_isComplete)
            CancelAndClose();
        else
            Close();
    }

    private void CancelAndClose()
    {
        Cancelled = true;
        _isComplete = true; // 停止后续进度更新，保持"正在取消…"
        _cts.Cancel();
        _statusText.Text = "正在取消…";
        Close();
    }
}
