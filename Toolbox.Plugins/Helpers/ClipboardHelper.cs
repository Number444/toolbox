using System;
using System.Threading;
using System.Windows.Threading;

namespace Toolbox.Tools.Helpers;

/// <summary>
/// 剪贴板写入辅助。
/// 剪贴板是全局互斥资源：被其他进程占用时，WPF Clipboard.SetText 在调用线程内部自动重试
/// （CLIPBRD_E_CANT_OPEN，最多约 8 次间隔递增）——UI 线程直调会冻结界面数百毫秒（"卡一下"）。
/// 这里在专用 STA 线程执行写入（WPF 剪贴板要求 STA + OLE 公寓，CLR 对 STA 托管线程启动时
/// 自动 CoInitializeEx，new Thread 即可用），UI 线程永不阻塞；结果经 Dispatcher 回传。
/// </summary>
public static class ClipboardHelper
{
    /// <summary>后台线程写入剪贴板，完成后在 uiDispatcher 上回调</summary>
    public static void CopyText(string text, Dispatcher uiDispatcher, Action<bool> onCompleted)
    {
        var thread = new Thread(() =>
        {
            bool ok = false;
            try
            {
                System.Windows.Clipboard.SetText(text);
                ok = true;
            }
            catch
            {
                // 剪贴板被长期占用/权限异常：结果以失败回调
            }

            try
            {
                uiDispatcher.BeginInvoke(() => onCompleted(ok));
            }
            catch
            {
                // 窗口已关闭：回调无处投递，静默
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
}
