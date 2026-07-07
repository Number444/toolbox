using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Toolbox.Models;

namespace Toolbox.Services;

/// <summary>
/// 已安装软件读取与卸载执行服务
/// </summary>
public static class SoftwareUninstallService
{
    private static readonly string[] RegistryPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    /// <summary>
    /// 扫描注册表获取全部已安装软件列表
    /// </summary>
    public static List<InstalledSoftware> GetInstalledSoftware()
    {
        var list = new List<InstalledSoftware>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) 本地机器（64位 + 32位视图）
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        foreach (var subPath in RegistryPaths)
        {
            using var key = hklm.OpenSubKey(subPath);
            if (key == null) continue;
            ReadEntries(key, list, seenNames);
        }

        // 2) 当前用户
        using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var cuKey = hkcu.OpenSubKey(RegistryPaths[0]);
        if (cuKey != null)
        {
            ReadEntries(cuKey, list, seenNames);
        }

        // 按名称排序
        list.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

        return list;
    }

    /// <summary>
    /// 测试专用的重载 —— 接受已打开的 RegistryKey
    /// </summary>
    public static List<InstalledSoftware> GetInstalledSoftware(RegistryKey key)
    {
        var list = new List<InstalledSoftware>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ReadEntries(key, list, seenNames);
        list.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    private static void ReadEntries(RegistryKey parentKey, List<InstalledSoftware> list, HashSet<string> seenNames)
    {
        foreach (var subKeyName in parentKey.GetSubKeyNames())
        {
            using var subKey = parentKey.OpenSubKey(subKeyName);
            if (subKey == null) continue;

            var displayName = subKey.GetValue("DisplayName") as string;
            if (string.IsNullOrWhiteSpace(displayName)) continue;

            // 过滤系统组件
            if (subKey.GetValue("SystemComponent") is int sysComp && sysComp == 1) continue;

            // 过滤父组件引用
            if (subKey.GetValue("ParentKeyName") is string parent && !string.IsNullOrWhiteSpace(parent)) continue;

            // 过滤无法卸载的条目
            var uninstallString = subKey.GetValue("UninstallString") as string ?? "";
            if (string.IsNullOrWhiteSpace(uninstallString)) continue;

            // 去重：同一名称只保留一个（首选有版本号的）
            if (seenNames.Contains(displayName))
            {
                var existing = list.Find(s => s.DisplayName == displayName);
                if (existing != null)
                {
                    var currentVersion = subKey.GetValue("DisplayVersion") as string ?? "";
                    if (!string.IsNullOrWhiteSpace(currentVersion) && string.IsNullOrWhiteSpace(existing.DisplayVersion))
                    {
                        list.Remove(existing);
                        seenNames.Remove(displayName);
                    }
                    else continue;
                }
                else continue;
            }

            var software = new InstalledSoftware
            {
                DisplayName = displayName,
                UninstallString = uninstallString,
                QuietUninstallString = subKey.GetValue("QuietUninstallString") as string ?? "",
                DisplayVersion = subKey.GetValue("DisplayVersion") as string ?? "",
                Publisher = subKey.GetValue("Publisher") as string ?? "",
                InstallDate = subKey.GetValue("InstallDate") as string ?? "",
                DisplayIcon = subKey.GetValue("DisplayIcon") as string ?? "",
                EstimatedSize = (subKey.GetValue("EstimatedSize") is int size) ? size : 0L,
                InstallLocation = subKey.GetValue("InstallLocation") as string ?? "",
            };

            list.Add(software);
            seenNames.Add(displayName);
        }
    }

    /// <summary>
    /// 从 DisplayIcon 路径提取图标，转为 WPF ImageSource
    /// </summary>
    public static ImageSource? ExtractIcon(string displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return null;

        try
        {
            var parts = displayIcon.Split(',');
            var exePath = parts[0].Trim('"');
            int iconIndex = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var idx) ? idx : 0;

            if (!File.Exists(exePath)) return null;

            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon == null) return null;

            return Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 执行卸载（触发 UAC 提权），返回是否成功启动
    /// </summary>
    public static bool UninstallSoftware(InstalledSoftware software, out string errorMessage)
    {
        errorMessage = "";

        try
        {
            if (string.IsNullOrWhiteSpace(software.UninstallString))
            {
                errorMessage = "卸载命令为空";
                return false;
            }

            var uninstallCmd = software.UninstallString.Trim();

            // 解析出可执行文件路径和参数
            string fileName;
            string arguments;

            if (uninstallCmd.StartsWith("\""))
            {
                // "C:\Program Files\App\uninstall.exe" /S 格式
                int endQuote = uninstallCmd.IndexOf('\"', 1);
                if (endQuote > 0)
                {
                    fileName = uninstallCmd.Substring(1, endQuote - 1);
                    arguments = uninstallCmd.Substring(endQuote + 1).TrimStart();
                }
                else
                {
                    fileName = uninstallCmd.Trim('\"');
                    arguments = "";
                }
            }
            else
            {
                // MsiExec.exe /X{...} 或无引号格式
                int firstSpace = uninstallCmd.IndexOf(' ');
                if (firstSpace > 0)
                {
                    fileName = uninstallCmd.Substring(0, firstSpace);
                    arguments = uninstallCmd.Substring(firstSpace + 1).TrimStart();
                }
                else
                {
                    fileName = uninstallCmd;
                    arguments = "";
                }
            }

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Normal
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                errorMessage = "无法启动卸载进程";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // Win32Exception(NativeErrorCode == 1223) = ERROR_CANCELLED = 用户在 UAC 对话框点了"否"
            if (ex is Win32Exception w32 && w32.NativeErrorCode == 1223)
            {
                errorMessage = "UAC_CANCELLED";
            }
            else
            {
                errorMessage = ex.Message;
            }
            return false;
        }
    }

    /// <summary>
    /// 轮询检测：检查指定名称的软件是否仍存在于注册表中
    /// </summary>
    public static bool IsSoftwareStillInstalled(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return false;

        // 检查所有注册表路径
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        foreach (var subPath in RegistryPaths)
        {
            if (CheckNameInKey(hklm, subPath, displayName))
                return true;
        }

        using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        if (CheckNameInKey(hkcu, RegistryPaths[0], displayName))
            return true;

        return false;
    }

    private static bool CheckNameInKey(RegistryKey baseKey, string subPath, string displayName)
    {
        using var key = baseKey.OpenSubKey(subPath);
        if (key == null) return false;

        foreach (var subKeyName in key.GetSubKeyNames())
        {
            using var subKey = key.OpenSubKey(subKeyName);
            if (subKey == null) continue;

            var name = subKey.GetValue("DisplayName") as string;
            if (name != null && name.Equals(displayName, StringComparison.OrdinalIgnoreCase))
            {
                // 确认不是系统组件，有有效卸载命令
                if (subKey.GetValue("SystemComponent") is int sysComp && sysComp == 1)
                    continue;
                if (subKey.GetValue("ParentKeyName") is string parent && !string.IsNullOrWhiteSpace(parent))
                    continue;
                var uninstallStr = subKey.GetValue("UninstallString") as string;
                if (string.IsNullOrWhiteSpace(uninstallStr))
                    continue;
                return true;
            }
        }
        return false;
    }
}