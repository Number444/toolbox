namespace Toolbox.Models;

/// <summary>
/// 软件列表排序模式
/// </summary>
public enum SortMode
{
    /// <summary>按安装日期排序</summary>
    InstallDate,
    /// <summary>按文件大小排序（降序）</summary>
    FileSize,
    /// <summary>按首字母排序</summary>
    Alphabetical
}

/// <summary>
/// SortMode 扩展方法
/// </summary>
public static class SortModeExtensions
{
    /// <summary>
    /// 获取下一个排序模式（循环）
    /// </summary>
    public static SortMode Next(this SortMode mode)
    {
        return mode switch
        {
            SortMode.InstallDate => SortMode.FileSize,
            SortMode.FileSize => SortMode.Alphabetical,
            SortMode.Alphabetical => SortMode.InstallDate,
            _ => SortMode.InstallDate
        };
    }

    /// <summary>
    /// 获取排序模式的显示图标（统一切换图标）
    /// </summary>
    public static string GetIcon(this SortMode mode)
    {
        return "\u21C4";  // ⇄
    }

    /// <summary>
    /// 获取排序模式的中文名称
    /// </summary>
    public static string GetLabel(this SortMode mode)
    {
        return mode switch
        {
            SortMode.InstallDate => "安装时间",
            SortMode.FileSize => "文件大小",
            SortMode.Alphabetical => "首字母",
            _ => "安装时间"
        };
    }
}