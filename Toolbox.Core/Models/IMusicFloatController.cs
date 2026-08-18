namespace Toolbox.Core.Models;

/// <summary>
/// 悬浮窗控制器抽象 —— 主程序经此接口控制音乐悬浮窗，不编译期依赖插件具体类型。
/// 由插件层 MusicFloatWindowManager 实现，经 MusicFloatControllerHost.Register 注册。
/// </summary>
public interface IMusicFloatController
{
    /// <summary>悬浮窗当前是否可见</summary>
    bool IsVisible { get; }

    /// <summary>可见性变化事件（插件内部状态同步用）</summary>
    event EventHandler<bool>? VisibilityChanged;

    /// <summary>显示悬浮窗（可指定大小模式与模糊）</summary>
    void Show(FloatSizeMode sizeMode, bool blurEnabled);

    /// <summary>隐藏悬浮窗（保留实例）</summary>
    void Hide();

    /// <summary>关闭悬浮窗</summary>
    void Close();

    /// <summary>切换毛玻璃/透明背景</summary>
    void ToggleBlur(bool enabled);

    /// <summary>切换大小模式（紧凑/大）</summary>
    void SetSizeMode(FloatSizeMode mode);

    /// <summary>设置窗口锁定（锁定后不可拖动）</summary>
    void SetWindowLocked(bool locked);

    /// <summary>复位窗口位置</summary>
    void ResetPosition();

    /// <summary>任务栏嵌入控件当前是否可见（与桌面悬浮窗独立）</summary>
    bool IsTaskbarWidgetVisible { get; }

    /// <summary>显示任务栏嵌入播放器控件（与桌面悬浮窗独立，可同时存在）</summary>
    void ShowTaskbarWidget();

    /// <summary>隐藏任务栏嵌入播放器控件</summary>
    void HideTaskbarWidget();
}
