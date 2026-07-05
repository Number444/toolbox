using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Toolbox.Helpers;

/// <summary>
/// 管理系统托盘图标（NotifyIcon）的 WPF 友好封装。
/// 单例模式，负责创建/显示/隐藏/释放托盘图标。
/// </summary>
public sealed class SystemTrayHelper : IDisposable
{
    private static readonly Lazy<SystemTrayHelper> _instance = new(() => new SystemTrayHelper());
    public static SystemTrayHelper Instance => _instance.Value;

    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private bool _isDisposed;

    private SystemTrayHelper() { }

    /// <summary>托盘图标是否已创建并可见。</summary>
    public bool IsVisible => _notifyIcon != null && _notifyIcon.Visible;

    /// <summary>创建并显示托盘图标。如果已存在则不重复创建。</summary>
    public void Show(string tooltip, Action onDoubleClick, Action onExitClick)
    {
        if (_notifyIcon != null) return;

        var icon = CreateDefaultIcon();

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Text = tooltip ?? "Toolbox",
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => onDoubleClick();

        _notifyIcon.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
        _notifyIcon.ContextMenuStrip.Items.Add("显示 Toolbox", null, (_, _) => onDoubleClick());
        _notifyIcon.ContextMenuStrip.Items.Add("退出", null, (_, _) => onExitClick());
    }

    /// <summary>隐藏并释放托盘图标。</summary>
    public void Hide()
    {
        if (_notifyIcon == null) return;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIcon = null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Hide();
    }

    private static System.Drawing.Icon CreateDefaultIcon()
    {
        // 尝试从主窗口图标获取
        if (Application.Current?.MainWindow?.Icon is ImageSource imageSource)
        {
            try
            {
                using var stream = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create((BitmapSource)imageSource));
                encoder.Save(stream);
                stream.Position = 0;
                return new System.Drawing.Icon(stream);
            }
            catch { }
        }

        // 尝试从应用资源加载 Toolbox.ico
        try
        {
            var uri = new Uri("pack://application:,,,/Toolbox.ico", UriKind.Absolute);
            var resourceStream = Application.GetResourceStream(uri);
            if (resourceStream != null)
            {
                using var stream = resourceStream.Stream;
                return new System.Drawing.Icon(stream);
            }
        }
        catch { }

        return System.Drawing.SystemIcons.Application;
    }
}