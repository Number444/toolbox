using System.Windows.Media;

namespace Toolbox.Models;

/// <summary>
/// 已安装软件的数据模型
/// </summary>
public class InstalledSoftware
{
    public string DisplayName { get; init; } = "";
    public string UninstallString { get; init; } = "";
    public string QuietUninstallString { get; init; } = "";
    public string DisplayVersion { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string InstallDate { get; init; } = "";      // YYYYMMDD 格式
    public string DisplayIcon { get; init; } = "";      // 图标路径（含索引）
    public long EstimatedSize { get; init; }              // KB
    public string InstallLocation { get; init; } = "";
    public ImageSource? Icon { get; set; }

    /// <summary>友好显示的大小</summary>
    public string SizeDisplay
    {
        get
        {
            if (EstimatedSize <= 0) return "";
            if (EstimatedSize < 1024) return $"{EstimatedSize} KB";
            if (EstimatedSize < 1024 * 1024) return $"{EstimatedSize / 1024.0:F1} MB";
            return $"{EstimatedSize / (1024.0 * 1024.0):F2} GB";
        }
    }

    /// <summary>友好显示的安装日期</summary>
    public string DateDisplay
    {
        get
        {
            if (string.IsNullOrEmpty(InstallDate) || InstallDate.Length != 8)
                return "";
            if (DateTime.TryParseExact(InstallDate, "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out var dt))
                return dt.ToString("yyyy-MM-dd");
            return InstallDate;
        }
    }
}
