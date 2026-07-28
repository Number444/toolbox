# Toolbox 新增快捷工具分析报告

> 基于现有 Toolbox 项目架构（WPF .NET 9 + Mica 深色主题 + 插件热插拔）的分析建议。
> 排除 Windows 已自带的功能（截图、计算器、时钟、剪贴板历史等）。
> 每个工具附完整实施指南。

---

## 一、项目现状

| 项目 | 内容 |
|------|------|
| 架构 | Toolbox.Core（接口层）→ Toolbox.Plugins（工具实现，热插拔）→ Toolbox（主程序） |
| 当前工具 | 定时关机、屏保启动（共 2 个） |
| 新增方式 | `Toolbox.Plugins/` 下新建 `{工具名}.cs`，实现 `ITool` 接口 → 重启即自动加载 |
| 定位 | 一键式系统级快捷操作，补 Win 短板 |
| 编译命令 | `dotnet build Toolbox.Plugins.csproj`，将 `Toolbox.Plugins.dll` 放入 `plugins/` 目录 |
| 主题色 | Accent: `#60CDFF`, Text: `#F0F0F0`, BgDark: `#1C1C1C`, Danger: `#F07070`, Success: `#63D47E` |

---

## 二、推荐新增工具清单

### A. 系统维护类（一键系统操作）

| # | 工具 | 文件名 | 痛点 | 优先级 | 复杂度 |
|---|------|--------|-----|--------|--------|
| 1 | **重启资源管理器** | `RestartExplorerTool.cs` | 任务栏/桌面卡死时需 3 步操作 | ⭐⭐⭐ | 低 |
| 2 | **重建图标缓存** | `RebuildIconCacheTool.cs` | 图标显示错乱，Win 无 GUI 修复 | ⭐⭐ | 低 |
| 3 | **刷新 DNS 缓存** | `FlushDnsTool.cs` | hosts 修改后不生效/解析异常 | ⭐⭐ | 低 |
| 4 | **电源计划切换** | `PowerPlanSwitchTool.cs` | 性能/省电切换需进控制面板 | ⭐⭐ | 中 |

### B. 网络/开发类（开发者刚需，Win 无 GUI）

| # | 工具 | 文件名 | 痛点 | 优先级 | 复杂度 |
|---|------|--------|-----|--------|--------|
| 5 | **端口占用查询+释放** | `PortCheckerTool.cs` | "端口被占用"需 cmd 排查 + taskkill | ⭐⭐⭐ | 中 |
| 6 | **Hosts 快速编辑** | `HostsEditorTool.cs` | 改 hosts 要找路径 + 管理员权限 | ⭐⭐ | 低 |
| 7 | **DNS 一键切换** | `DnsSwitchTool.cs` | 改 DNS 需进网卡属性多层菜单 | ⭐⭐ | 中 |
| 8 | **文件 Hash 校验** | `FileHashTool.cs` | 验证下载完整性只有 `certutil` | ⭐⭐⭐ | 低 |
| 9 | **系统代理一键开关** | `ProxyToggleTool.cs` | 开发调试需进设置-网络 | ⭐⭐ | 低 |

### C. 窗口/桌面增强类

| # | 工具 | 文件名 | 痛点 | 优先级 | 复杂度 |
|---|------|--------|-----|--------|--------|
| 10 | **窗口置顶** | `AlwaysOnTopTool.cs` | 参考资料常驻前，Win 无原生功能 | ⭐⭐⭐ | 低 |
| 11 | **取色器** | `ColorPickerTool.cs` | UI 开发需拿屏幕颜色值 | ⭐⭐ | 中 |
| 12 | **桌面图标一键显隐** | `DesktopIconsToggleTool.cs` | 录屏/演示需干净桌面 | ⭐⭐ | 低 |

### D. 文本/数据小工具

| # | 工具 | 文件名 | 痛点 | 优先级 | 复杂度 |
|---|------|--------|-----|--------|--------|
| 13 | **文本处理工具箱** | `TextToolbox.cs` | 大小写/全半角/Base64 每次找网站 | ⭐⭐⭐ | 低 |
| 14 | **二维码生成器** | `QrCodeTool.cs` | 传内容到手机，Win 无原生生成 | ⭐⭐ | 低 |

### E. 文件类

| # | 工具 | 文件名 | 痛点 | 优先级 | 复杂度 |
|---|------|--------|-----|--------|--------|
| 15 | **强制删除被占用文件** | `ForceDeleteTool.cs` | "文件正使用"无法删除 | ⭐⭐ | 低 |
| 16 | **空文件夹查找清理** | `EmptyFolderCleanerTool.cs` | 找空目录靠第三方工具 | ⭐⭐ | 低 |

---

## 三、逐个工具实施指南

---

### 1. 重启资源管理器 (`RestartExplorerTool.cs`)

**IconGlyph**: 🔄

**功能描述**: 当任务栏或桌面卡死时，一键结束并重启 explorer.exe 进程。比手动打开任务管理器→找到 explorer→结束任务→新建任务→输入 explorer 快 10 倍。

**核心逻辑**:
```csharp
// Step 1: 结束 explorer.exe
Process.Start(new ProcessStartInfo
{
    FileName = "taskkill",
    Arguments = "/f /im explorer.exe",
    UseShellExecute = true,
    CreateNoWindow = true,
    WindowStyle = ProcessWindowStyle.Hidden
})?.WaitForExit();

// Step 2: 等待 500ms 确保进程已退出
Thread.Sleep(500);

// Step 3: 重新启动 explorer
Process.Start(new ProcessStartInfo
{
    FileName = "explorer.exe",
    UseShellExecute = true
});
```

**UI 布局**:
- 警告 TextBlock（黄色/橙色文字：⚠️ 此操作会关闭所有文件资源管理器窗口，请先保存工作）
- 一个按钮「🔄 重启资源管理器」
- 结果反馈 TextBlock（成功/失败）

**实施注意事项**:
- `taskkill /f` 会强制结束所有 explorer 窗口，用户需知晓风险
- 等待 `taskkill` 退出后再启动 explorer，避免竞争条件
- 可在按钮点击前加 `MessageBox` 二次确认
- 估计代码量: **~60 行**

---

### 2. 重建图标缓存 (`RebuildIconCacheTool.cs`)

**IconGlyph**: 🖼️

**功能描述**: Windows 图标缓存损坏时（显示白块/错误图标），一键重建。比手动进 `%LocalAppData%` 找 `IconCache.db` 删除再重启 explorer 简单得多。

**核心逻辑**:
```csharp
// Step 1: 结束 explorer（因为 iconcache 文件被 explorer 锁定）
Process.Start(new ProcessStartInfo
{
    FileName = "taskkill",
    Arguments = "/f /im explorer.exe",
    CreateNoWindow = true
})?.WaitForExit();
Thread.Sleep(300);

// Step 2: 删除所有 iconcache*.db 文件
string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
string iconCachePath = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer");
foreach (var file in Directory.GetFiles(iconCachePath, "iconcache*.db"))
    File.Delete(file);

// Step 3: 重启 explorer
Process.Start("explorer.exe");
```

**UI 布局**:
- 警告文字
- 按钮「🖼️ 重建图标缓存」
- 结果反馈

**实施注意事项**:
- `IconCache.db` 路径: `%LocalAppData%\Microsoft\Windows\Explorer\`
- 可能有多个 `iconcache_*.db` 文件，全部删除
- 删除操作通常很快，延迟 300ms 足够
- 估计代码量: **~70 行**

---

### 3. 刷新 DNS 缓存 (`FlushDnsTool.cs`)

**IconGlyph**: 🌐

**功能描述**: 修改 hosts 后不生效、网页解析异常时，一键执行 `ipconfig /flushdns`。无需打开管理员终端。

**核心逻辑**:
```csharp
// 执行 ipconfig /flushdns（需管理员权限）
using var proc = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = "ipconfig",
        Arguments = "/flushdns",
        UseShellExecute = true,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden,
        Verb = "runas"  // 触发 UAC 提权
    }
};
proc.Start();
proc.WaitForExit(5000);

if (proc.ExitCode == 0)
    result = "✅ DNS 缓存已刷新";
else
    result = $"⚠️ 执行失败 (退出码: {proc.ExitCode})";
```

**UI 布局**:
- 说明文字
- 按钮「🌐 刷新 DNS 缓存」
- 结果反馈
- 补充说明 TextBlock（小字灰色：此操作需要管理员权限）

**实施注意事项**:
- 使用 `Verb = "runas"` 自动触发 UAC 弹窗
- 设置合理超时（5 秒），避免 UI 卡死
- 估计代码量: **~50 行**

---

### 4. 电源计划切换 (`PowerPlanSwitchTool.cs`)

**IconGlyph**: ⚡

**功能描述**: 一键切换 Windows 电源计划（平衡/高性能/节能），无需进控制面板→电源选项。

**核心逻辑**:
```csharp
// Step 1: 枚举所有电源计划
// powercfg /L 输出格式：
//   Power Scheme GUID: xxx  (计划名称)
using var proc = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = "powercfg",
        Arguments = "/L",
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    }
};
proc.Start();
string output = proc.StandardOutput.ReadToEnd();
proc.WaitForExit();

// Step 2: 解析 GUID 和名称
var regex = new Regex(@"GUID:\s*([\w-]+)\s+\(([^)]+)\)");
var plans = regex.Matches(output)
    .Select(m => new { Guid = m.Groups[1].Value, Name = m.Groups[2].Value })
    .ToList();

// Step 3: 切换计划
// powercfg /S <GUID>
Process.Start(new ProcessStartInfo
{
    FileName = "powercfg",
    Arguments = $"/S {selectedPlanGuid}",
    CreateNoWindow = true
});
```

**UI 布局**:
- 说明文字
- ComboBox（枚举所有电源计划）
- 按钮「⚡ 切换电源计划」
- 当前激活计划高亮标记
- 常见计划识别：平衡 (`381b4222-*`)、高性能 (`8c5e7fda-*`)、节能 (`a1841308-*`)

**实施注意事项**:
- 用 `powercfg /L` 枚举计划，`powercfg /S` 设置
- GUID 格式：`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`
- 内置三个计划 GUID 前缀固定，可用于识别标签
- 可选增加 `powercfg /GETACTIVESCHEME` 获取当前计划
- 估计代码量: **~130 行**

---

### 5. 端口占用查询+释放 (`PortCheckerTool.cs`)

**IconGlyph**: 🔍

**功能描述**: 输入端口号，显示占用该端口的进程详情（PID、进程名），一键结束进程释放端口。替代 `netstat -ano | findstr :8080` + 手动查 PID + `taskkill` 的繁琐流程。

**核心逻辑**:
```csharp
// Step 1: 解析 netstat -ano 输出
// 格式: TCP    0.0.0.0:8080    0.0.0.0:0    LISTENING    12345
using var proc = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = "netstat",
        Arguments = "-ano",
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    }
};
proc.Start();
string output = proc.StandardOutput.ReadToEnd();
proc.WaitForExit();

// Step 2: 解析端口→PID 映射
var regex = new Regex(@":(\d+)\s+.*LISTENING\s+(\d+)");
var portMap = regex.Matches(output)
    .ToDictionary(m => int.Parse(m.Groups[1].Value), m => int.Parse(m.Groups[2].Value));

// Step 3: 查 PID 对应的进程名
if (portMap.TryGetValue(targetPort, out int pid))
{
    var process = Process.GetProcessById(pid);
    processName = process.ProcessName;
}

// Step 4: Kill 进程
process.Kill();
```

**UI 布局**:
- 描述文字
- 输入行：TextBox（端口号）+ 按钮「🔍 查询」
- 结果展示区域（默认隐藏）：
  - TextBlock：进程名 + PID
  - 按钮「🛑 结束进程」（红色 DangerBrush 背景）
  - 未占用时：绿色「✅ 端口未被占用」
- 错误反馈

**实施注意事项**:
- `netstat -ano` 输出行尾可能有空格，解析时 trim
- `LISTENING` 状态是可关的，`ESTABLISHED` 是活跃连接，列出时区分
- Kill 操作前建议二次确认（MessageBox）
- Kill 系统进程可能需要管理员权限，失败时提示
- 估计代码量: **~150 行**

---

### 6. Hosts 快速编辑 (`HostsEditorTool.cs`)

**IconGlyph**: 📝

**功能描述**: 一键以管理员权限打开 hosts 文件编辑。高级模式可做 hosts 方案备份/恢复（开发环境/生产环境快速切换）。

**核心逻辑（基础版）**:
```csharp
// 以管理员权限用记事本打开 hosts
string hostsPath = @"C:\Windows\System32\drivers\etc\hosts";
Process.Start(new ProcessStartInfo
{
    FileName = "notepad.exe",
    Arguments = hostsPath,
    Verb = "runas",  // UAC 提权
    UseShellExecute = true
});
```

**核心逻辑（进阶版 - 方案切换）**:
```csharp
// 备份 hosts
string backupDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Toolbox", "HostsBackups");
Directory.CreateDirectory(backupDir);

// 保存当前 hosts
string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
File.Copy(hostsPath, $"{backupDir}\\hosts_{timestamp}", overwrite: true);

// 恢复 hosts
File.Copy(selectedBackup, hostsPath, overwrite: true);
// 刷新 DNS
Process.Start(new ProcessStartInfo
{
    FileName = "ipconfig", Arguments = "/flushdns", Verb = "runas"
});
```

**UI 布局（基础版）**:
- 说明文字
- 按钮「📝 以管理员编辑 Hosts」
- 提示文字（小字灰色：保存后建议刷新 DNS 缓存）

**UI 布局（进阶版 - 方案切换器）**:
- 新增备份列表（ListView / ItemsControl）
- 按钮组：备份当前、恢复选中、删除选中
- 注意：备份/恢复都需要管理员权限

**实施注意事项**:
- hosts 路径为 System32 下，64 位机器上 32 位进程可能被重定向到 SysWOW64
- 可用 `Environment.SystemDirectory` 替代硬编码路径
- 方案切换器可独立拆成高级版工具
- 估计代码量: 基础版 **~60 行** / 进阶版 **~130 行**

---

### 7. DNS 一键切换 (`DnsSwitchTool.cs`)

**IconGlyph**: 🌍

**功能描述**: 一键切换网卡 DNS 服务器。预置 114DNS、阿里、Cloudflare、Google 等公共 DNS，无需进网络适配器属性多层菜单。

**核心逻辑**:
```csharp
// 获取活跃网卡名称
var activeAdapter = NetworkInterface.GetAllNetworkInterfaces()
    .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up
        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

// 设置 DNS（需要管理员权限）
// netsh interface ip set dns "<网卡名>" static <DNS地址>
string setDns = $"interface ip set dns \"{adapterName}\" static {dnsIp}";
string setDns2 = $"interface ip add dns \"{adapterName}\" {dnsIp2} index=2";

var proc = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = "netsh",
        Arguments = setDns,
        Verb = "runas",
        UseShellExecute = true,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden
    }
};
// ... 等待完成后设置备用 DNS
```

**UI 布局**:
- 当前网卡信息（名称 + 当前 DNS，通过 `nslookup` 或 `ipconfig /all` 解析）
- 预置 DNS 列表（RadioButton 或 ComboBox）：

| DNS 名称 | 首选 IP | 备用 IP |
|----------|---------|---------|
| 114DNS | `114.114.114.114` | `114.114.115.115` |
| 阿里 DNS | `223.5.5.5` | `223.6.6.6` |
| Cloudflare | `1.1.1.1` | `1.0.0.1` |
| Google | `8.8.8.8` | `8.8.4.4` |
| 自动获取 | DHCP | — |

- 按钮「🌍 切换 DNS」
- 结果反馈
- 恢复自动获取按钮（可选）：`netsh interface ip set dns "<网卡>" dhcp`

**实施注意事项**:
- 使用 `NetworkInterface.GetAllNetworkInterfaces()` 获取所有网卡，筛选活跃的
- `netsh` 需要管理员权限，使用 `Verb = "runas"`
- 设置 DNS 和备用 DNS 需分两次 netsh 调用
- 恢复 DHCP 需先 `set dns dhcp` 再清除备用 DNS
- 估计代码量: **~140 行**

---

### 8. 文件 Hash 校验 (`FileHashTool.cs`)

**IconGlyph**: 🔐

**功能描述**: 拖入或选择文件，一键计算 MD5 / SHA1 / SHA256 哈希值，验证下载文件完整性。替代 `certutil -hashfile` 命令行。

**核心逻辑**:
```csharp
// 使用 .NET 内置 HashAlgorithm（纯 C#，零命令行依赖）
using var stream = File.OpenRead(filePath);

// SHA256（最常用）
using var sha256 = SHA256.Create();
byte[] hashBytes = sha256.ComputeHash(stream);
string sha256Hash = Convert.ToHexStringLower(hashBytes);

// MD5
using var md5 = MD5.Create();
string md5Hash = Convert.ToHexStringLower(md5.ComputeHash(
    File.OpenRead(filePath)));

// SHA1
using var sha1 = SHA1.Create();
string sha1Hash = Convert.ToHexStringLower(sha1.ComputeHash(
    File.OpenRead(filePath)));
```

**UI 布局**:
- 描述文字「拖入文件或点击选择，自动计算哈希」
- 文件选择区：
  - TextBox（文件路径，只读）
  - 按钮「📂 选择文件」
  - 支持拖拽（`AllowDrop = true` + Drop 事件）
- 结果区（三行）：
  - MD5: `xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx`
  - SHA1: `xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx`
  - SHA256: `xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx`
- 每行右侧一个 📋 复制按钮
- 可选：Hex 输入框 +「对比」按钮（与目标哈希比对，标红/绿）

**实施注意事项**:
- 使用 .NET `Cryptography` 命名空间，纯 C# 无外部命令
- 大文件流式读取避免内存溢出
- 拖拽支持：`DragEnter` 设 `e.Effects`，`Drop` 取文件路径
- 复制到剪贴板：`Clipboard.SetText(hashString)`
- 估计代码量: **~120 行**

---

### 9. 系统代理一键开关 (`ProxyToggleTool.cs`)

**IconGlyph**: 🔀

**功能描述**: 一键开启/关闭 Windows 系统代理（Internet 选项 → 连接 → 局域网设置），无需进多层设置菜单。

**核心逻辑**:
```csharp
// 读取当前代理状态
const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
using var key = Registry.CurrentUser.OpenSubKey(keyPath);
int proxyEnable = (int)key.GetValue("ProxyEnable", 0);
string proxyServer = (string)key.GetValue("ProxyServer", "");

bool isEnabled = proxyEnable == 1;

// 切换代理
using var writeKey = Registry.CurrentUser.OpenSubKey(keyPath, true);
if (isEnabled)
{
    // 关闭代理
    writeKey.SetValue("ProxyEnable", 0);
}
else
{
    // 开启代理（使用预设或已配置的地址）
    if (string.IsNullOrEmpty(proxyServer))
    {
        // 如果没有已配置地址，设为 127.0.0.1:7890（常见代理默认端口）
        writeKey.SetValue("ProxyServer", "127.0.0.1:7890");
    }
    writeKey.SetValue("ProxyEnable", 1);
}

// 注册表修改后刷新系统设置（广播 WM_SETTINGCHANGE）
[DllImport("user32.dll")]
static extern IntPtr SendMessageTimeout(
    IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam,
    uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

const uint HWND_BROADCAST = 0xFFFF;
const uint WM_SETTINGCHANGE = 0x001A;
SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero,
    "Environment", 2, 5000, out _);
```

**UI 布局**:
- 当前状态大字显示（绿色「✅ 代理已开启：xxx」 / 灰色「⭕ 代理已关闭」）
- 按钮「🔀 切换代理状态」
- 可选：TextBox 自定义代理地址 + 端口
- 提示文字（更改后需刷新浏览器等说明）

**实施注意事项**:
- 需要 `Microsoft.Win32` 命名空间
- `Broadcast SystemSettingChange` 通知系统刷新，但不保证所有程序立即感知
- 可扩展预设多代理方案（公司 VPN 代理 / 开发代理 / 直连）
- 估计代码量: **~100 行**

---

### 10. 窗口置顶 (`AlwaysOnTopTool.cs`)

**IconGlyph**: 📌

**功能描述**: 一键将当前前台窗口置顶（Always On Top），再次点击取消。替代 PowerToys 同名功能，纯 Win32 API 实现。

**核心逻辑**:
```csharp
// P/Invoke 声明
[DllImport("user32.dll")]
static extern IntPtr GetForegroundWindow();

[DllImport("user32.dll")]
static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
    int X, int Y, int cx, int cy, uint uFlags);

static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
const uint SWP_NOMOVE = 0x0002;
const uint SWP_NOSIZE = 0x0001;

// 检查当前前台窗口是否已置顶
[DllImport("user32.dll")]
static extern int GetWindowLong(IntPtr hWnd, int nIndex);

const int GWL_EXSTYLE = -20;
const int WS_EX_TOPMOST = 0x0008;

bool IsTopMost(IntPtr hwnd)
{
    return (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
}

// 切换置顶
void ToggleTopMost(IntPtr hwnd)
{
    if (IsTopMost(hwnd))
        SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
    else
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
}
```

**UI 布局**:
- 当前前台窗口标题大字显示
- 置顶状态指示器（🔴 未置顶 / 🟢 已置顶）
- 按钮「📌 切换置顶」
- 说明文字（提醒用户切换窗口后再点击）

**实施注意事项**:
- 需要 P/Invoke `user32.dll`（不能只用插件层的代码？当前 `Win32Helper` 在主项目层）
- **架构建议**：可在 `Toolbox.Core` 中新增 `IWin32Tool` 接口提供常见 P/Invoke 封装，或直接在插件中声明
- `GetForegroundWindow()` 取的是当前前台窗口，不是 Toolbox 自身
- 建议增加一个「刷新」按钮重新获取前台窗口信息
- 估计代码量: **~90 行**

---

### 11. 取色器 (`ColorPickerTool.cs`)

**IconGlyph**: 🎨

**功能描述**: 在屏幕上任意位置取色，获取精确的 HEX/RGB 颜色值。UI 开发、设计时无需打开外部软件。

**核心逻辑**:
```csharp
// Step 1: 捕获屏幕某区域（用 Win32 GetDC + BitBlt）
[DllImport("user32.dll")]
static extern IntPtr GetDC(IntPtr hwnd);
[DllImport("gdi32.dll")]
static extern uint GetPixel(IntPtr hdc, int x, int y);

// 取鼠标位置的颜色
Point mousePos = GetMousePosition();
IntPtr hdc = GetDC(IntPtr.Zero);
uint pixel = GetPixel(hdc, (int)mousePos.X, (int)mousePos.Y);

// 解析 RGB
byte r = (byte)(pixel & 0xFF);
byte g = (byte)((pixel >> 8) & 0xFF);
byte b = (byte)((pixel >> 16) & 0xFF);

// 格式化
string hex = $"#{r:X2}{g:X2}{b:X2}";   // #60CDFF
string rgb = $"rgb({r}, {g}, {b})";     // rgb(96, 205, 255)
```

**UI 布局**:
- 大色块预览（Border 填充取到的颜色，100x60）
- 颜色值显示：
  - HEX: `#60CDFF` + 📋 复制按钮
  - RGB: `rgb(96, 205, 255)` + 📋 复制按钮
- 按钮「🎯 点击取色」
  - 点击后：监听全局鼠标点击事件，点击任意位置取色
  - 取色时显示十字准星光标
- 取色历史（可选，显示最近 5 个颜色小块）

**实施注意事项**:
- 全局鼠标钩子需 P/Invoke `SetWindowsHookEx`（`WH_MOUSE_LL = 14`），比取色逻辑本身更复杂
- **架构建议**：这个工具突破了"纯 UIElement 返回"的插件模式，需要全局钩子
  - 方案 1：在 `ITool` 加 `OnActivated()` 钩子方法，主程序注册/注销钩子
  - 方案 2：取色按钮点击后让 Toolbox 最小化，用户点击屏幕任意位置后弹出小窗口显示结果
- 取色逻辑（`GetPixel`）很简单，钩子管理是核心难点
- 估计代码量: **~180 行**（含钩子管理 ~100 行）

---

### 12. 桌面图标一键显隐 (`DesktopIconsToggleTool.cs`)

**IconGlyph**: 👁️

**功能描述**: 一键隐藏或显示桌面图标。录屏、演示需干净桌面时，再也不用右键→查看→显示桌面图标三步操作。

**核心逻辑**:
```csharp
// 方法 1：修改注册表 + 刷新 explorer
// 路径: HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced
// 键: HideIcons (DWORD, 0=显示, 1=隐藏)

const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
using var key = Registry.CurrentUser.OpenSubKey(keyPath, true);

int current = (int)(key.GetValue("HideIcons", 0) ?? 0);

if (current == 0)
    key.SetValue("HideIcons", 1);  // 隐藏
else
    key.SetValue("HideIcons", 0);  // 显示

// 刷新桌面使更改立即生效
// 方法 A: 广播 WM_SETTINGCHANGE
SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE,
    UIntPtr.Zero, "Policy", 2, 5000, out _);

// 方法 B（更可靠）: 重启 explorer（激进）
// taskkill /f /im explorer.exe & start explorer.exe
```

**UI 布局**:
- 当前状态大字（👁️ 桌面图标可见 / 🙈 桌面图标隐藏）
- 按钮「👁️ 切换桌面图标显示」
- 状态实时更新

**实施注意事项**:
- 注册表路径确认：`HideIcons` 值，0=显示，1=隐藏
- `WM_SETTINGCHANGE` 广播可能延迟几秒，给用户明确反馈
- 可选：不需要重启 explorer，广播足够
- 注册表操作无需管理员权限（HKCU）
- 估计代码量: **~60 行**

---

### 13. 文本处理工具箱 (`TextToolbox.cs`)

**IconGlyph**: 🔤

**功能描述**: 在输入框粘贴/输入文本，点击按钮立即处理。覆盖大小写转换、全半角转换、去换行、去空行、Base64 编解码、去首尾空格等日常文本处理需求。纯 C# 字符串操作，零外部依赖。

**核心逻辑**:
```csharp
// 大小写
string ToUpper(string s) => s.ToUpper();
string ToLower(string s) => s.ToLower();

// 全半角转换（ASCII ↔ 全角）
string ToFullWidth(string s)  // 半角 0-9,A-Z,a-z → 全角 ０-９,Ａ-Ｚ,ａ-ｚ
string ToHalfWidth(string s)  // 反之

// 去空行
string RemoveEmptyLines(string s)
    => string.Join("\n",
        s.Split('\n', StringSplitOptions.RemoveEmptyEntries));

// 去换行（把多行合并为一行）
string RemoveLineBreaks(string s) => s.Replace("\r\n", " ").Replace("\n", " ");

// Base64
string ToBase64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
string FromBase64(string s) => Encoding.UTF8.GetString(Convert.FromBase64String(s));

// 去首尾空格 + 去重复空格
string TrimAll(string s)
{
    s = s.Trim();
    while (s.Contains("  ")) s = s.Replace("  ", " ");
    return s;
}
```

**UI 布局**:
- 多行 TextBox（输入区，AcceptsReturn=true，Height=120）
- 按钮网格（2 行 × 4 列）:
  - 全部大写 | 全部小写 | 首字母大写 | 反转大小写
  - 转全角 | 转半角 | 去空行 | 去除换行
  - To Base64 | From Base64 | 去首尾空格 | 清空输入
- 多行 TextBox（输出区，只读，TextWrapping=Wrap）
- 按钮「📋 复制结果」
- 字符统计（输入/输出各显示字符数）

**实施注意事项**:
- 全半角转换：`0x20` 到 `0x7E` 对应 `0xFF00` 到 `0xFF5E`（偏移 0xFEE0）
- Base64 输入非 UTF8 时会抛异常，需 try/catch
- 输出区设为只读避免用户误解为编辑区
- 按钮用 `UniformGrid` 排列，与 ShutdownTool 快捷按钮样式一致
- 估计代码量: **~200 行**

---

### 14. 二维码生成器 (`QrCodeTool.cs`)

**IconGlyph**: 📱

**功能描述**: 输入文本或 URL，实时生成二维码图片，可右键保存或复制。传文字/链接到手机无需再找在线工具。

**核心逻辑**:
```csharp
// 需要 NuGet 包: QRCoder (纯 .NET 实现，零原生依赖)
// dotnet add Toolbox.Plugins.csproj package QRCoder
using QRCoder;

string GenerateQr(string content)
{
    using var generator = new QRCodeGenerator();
    using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
    using var qrCode = new PngByteQRCode(data);
    byte[] pngBytes = qrCode.GetGraphic(20); // 20 = 模块大小

    // 将 byte[] 转为 Base64 嵌入 Image 控件
    return Convert.ToBase64String(pngBytes);
}

// WPF 显示：用 Image 控件 + BitmapImage from MemoryStream
var bitmap = new BitmapImage();
bitmap.BeginInit();
bitmap.StreamSource = new MemoryStream(pngBytes);
bitmap.EndInit();
qrImage.Source = bitmap;
```

**UI 布局**:
- 上半区：多行 TextBox（输入文本/URL）
- 下半区：Image 控件显示二维码（固定 200x200）
- 按钮组：
  - 「💾 保存为 PNG」（SaveFileDialog）
  - 「📋 复制到剪贴板」（`Clipboard.SetImage`）
- 实时生成（TextChanged 事件 + 300ms 防抖 Timer）

**实施注意事项**:
- QRCoder 是纯 .NET 库，MIT 协议，NuGet 安装即可
- 实时生成需加防抖（输入停止 300ms 后生成），避免频繁计算
- `Clipboard.SetImage` 复制二维码到剪贴板可直接粘贴到微信/QQ
- 二维码内容过长时需要更高容错级别（ECCLevel.H）
- 保存 PNG 用 `SaveFileDialog` 获取目标路径
- 估计代码量: **~110 行**

---

### 15. 强制删除被占用文件 (`ForceDeleteTool.cs`)

**IconGlyph**: 🗑️

**功能描述**: 删除被其他程序锁定的文件（"文件正在使用，无法删除"）。使用 `MoveFileEx` 标记在下次重启时删除，或以管理员权限强制解除句柄。

**核心逻辑（基础版 - 重启删除）**:
```csharp
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName,
    uint dwFlags);

const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

// 标记文件在重启后删除（lpNewFileName 传 null 表示删除）
if (!MoveFileEx(filePath, null, MOVEFILE_DELAY_UNTIL_REBOOT))
{
    int error = Marshal.GetLastWin32Error();
    result = $"❌ 标记失败 (错误码: {error})";
}
else
{
    result = $"✅ 已标记为重启后删除: {Path.GetFileName(filePath)}";
}
```

**核心逻辑（进阶版 - 解除句柄）**:
```csharp
// 使用 handle.exe（Sysinternals）或 Restart Manager API
// 复杂的句柄枚举方式，基础版已覆盖 90% 场景
```

**UI 布局**:
- 描述文字
- 文件选择区（TextBox 路径 + 按钮「📂 选择文件」 + 拖拽支持）
- 按钮「🗑️ 标记重启后删除」（橙色警告色）
- 警告 TextBlock（此操作将在下次重启时生效）
- 结果反馈
- 可选：已标记列表（ListView，管理多个待删文件 + 取消标记）

**实施注意事项**:
- `MoveFileEx` 的 `MOVEFILE_DELAY_UNTIL_REBOOT` 需要 SE_SHUTDOWN_NAME 权限（管理员）
- 使用 `Verb = "runas"` 重新以管理员身份调用
- 取消标记：再次调用 `MoveFileEx(filePath, null, 0)` 不带 DELAY_UNTIL_REBOOT 标志可清除
- 进阶版需 Sysinternals `handle.exe` 或 Windows Restart Manager API（`RmStartSession` 等）
- 估计代码量: 基础版 **~80 行** / 进阶版 **~160 行**

---

### 16. 空文件夹查找清理 (`EmptyFolderCleanerTool.cs`)

**IconGlyph**: 📁

**功能描述**: 递归扫描指定目录，找出所有空文件夹，勾选后一键删除。清理项目残留、卸载后遗留空目录。

**核心逻辑**:
```csharp
// Step 1: 递归查找空文件夹
List<string> FindEmptyFolders(string rootPath, CancellationToken token)
{
    var emptyFolders = new List<string>();
    foreach (var dir in Directory.EnumerateDirectories(rootPath, "*",
        SearchOption.AllDirectories))
    {
        token.ThrowIfCancellationRequested();

        // 目录为空 = 无文件 + 无子目录（或子目录全是空的）
        if (!Directory.EnumerateFileSystemEntries(dir).Any())
            emptyFolders.Add(dir);
    }
    return emptyFolders;
}

// Step 2: 批量删除
foreach (var dir in selectedFolders)
{
    try
    {
        Directory.Delete(dir);
        deleted++;
    }
    catch (Exception ex)
    {
        failed.Add($"{dir}: {ex.Message}");
    }
}
```

**UI 布局**:
- 目录选择（TextBox + 「📂 浏览」按钮）
- 「🔍 扫描」按钮（带进度/取消支持，用 BackgroundWorker 或 async）
- 进度提示（"正在扫描...已找到 N 个空文件夹"）
- 结果列表（ListView / ItemsControl，每个项有 CheckBox）+ 全选/取消全选
  - 每行：☐ `C:\xxx\empty_folder`  大小: 0 B
- 按钮「🗑️ 删除已选（N 个）」（红色警告色）
- 结果反馈（"成功删除 X 个，失败 Y 个"）
- 失败列表（可选，展开显示哪些删除失败及原因）

**实施注意事项**:
- 扫描大目录时需异步 + 取消支持，避免 UI 卡死
- 使用 `CancellationTokenSource` + `Task.Run` 实现可取消的异步扫描
- 扫描结果用 `ObservableCollection` 实时更新到 UI
- 注意排除系统目录（`C:\Windows`、`C:\Program Files` 等），加安全确认
- "空文件夹"定义：无任何文件、无任何非空子目录（递归判断）
- 估计代码量: **~180 行**

---

## 四、Top 5 优先实施推荐

如果优先挑选 **收益高、实现简单、与现有架构契合度最好** 的 5 个：

| 排名 | 工具 | 文件名 | 理由 | 难度 | 代码量 |
|:----:|------|--------|------|:----:|:------:|
| 🥇 | **重启资源管理器** | `RestartExplorerTool.cs` | 延续"定时关机"的一键系统操作范式，无外部依赖 | 低 | ~60 行 |
| 🥇 | **文本处理工具箱** | `TextToolbox.cs` | 纯 C# 字符串操作，零外部依赖，展示插件能力天花板 | 低 | ~200 行 |
| 🥇 | **文件 Hash 校验** | `FileHashTool.cs` | .NET 内置 Cryptography，一条 Stream + 一次 ComputeHash | 低 | ~120 行 |
| 🥇 | **端口占用查询+释放** | `PortCheckerTool.cs` | 开发者每天遇到的痛点，只需 netstat 解析 | 中 | ~150 行 |
| 🥇 | **窗口置顶** | `AlwaysOnTopTool.cs` | Win 原生缺失的高频功能，3 个 P/Invoke 函数即可 | 低 | ~90 行 |

**实施顺序建议**：先做 1/3/2（零外部依赖、一天搞定三个），再做 5（需 P/Invoke），最后做 4（正则解析稍复杂）。

---

## 五、架构提醒与风险预判

### 5.1 与现有架构完全兼容（开箱即做）

以下工具只需在 `Toolbox.Plugins/` 中新建 `.cs` 文件，不涉及 P/Invoke 或全局钩子：

| 工具 # | 文件名 | 依赖 |
|--------|--------|------|
| 1 | `RestartExplorerTool.cs` | Process.Start |
| 2 | `RebuildIconCacheTool.cs` | Process.Start + File IO |
| 3 | `FlushDnsTool.cs` | Process.Start (runas) |
| 4 | `PowerPlanSwitchTool.cs` | Process.Start (RedirectStdOut) |
| 5 | `PortCheckerTool.cs` | Process.Start (RedirectStdOut) + Regex |
| 8 | `FileHashTool.cs` | .NET Cryptography (SHA256/MD5/SHA1) |
| 12 | `DesktopIconsToggleTool.cs` | Registry (HKCU) |
| 13 | `TextToolbox.cs` | 纯字符串操作 |
| 14 | `QrCodeTool.cs` | NuGet: QRCoder |
| 16 | `EmptyFolderCleanerTool.cs` | Directory IO + async/await |

### 5.2 需要 P/Invoke 但可封装在插件内

以下工具需要在插件 `.cs` 中自行声明 `DllImport`，不穿透插件边界：

| 工具 # | 文件名 | 所需 Win32 API |
|--------|--------|---------------|
| 6 | `HostsEditorTool.cs` | Process.Start (runas) — 无额外 P/Invoke |
| 9 | `ProxyToggleTool.cs` | `SendMessageTimeout` (user32.dll) + Registry |
| 10 | `AlwaysOnTopTool.cs` | `GetForegroundWindow / SetWindowPos / GetWindowLong` |
| 15 | `ForceDeleteTool.cs` | `MoveFileEx` (kernel32.dll) |

### 5.3 需要全局钩子 — 建议 `ITool` 增加生命周期方法

以下工具需要全局级别的钩子（鼠标/键盘），当前 `ITool` 的 `CreateContent()` 模型无法覆盖：

| 工具 # | 文件名 | 所需突破 |
|--------|--------|---------|
| 11 | `ColorPickerTool.cs` | 全局鼠标钩子 `SetWindowsHookEx(WH_MOUSE_LL)` |

**建议**：在 `ITool` 接口中新增可选的虚拟方法：

```csharp
public interface ITool
{
    string Name { get; }
    string Description { get; }
    string IconGlyph { get; }
    UIElement CreateContent();

    // 新增可选生命周期方法（有默认空实现）
    void OnActivated() { }    // 用户切换到此工具时调用
    void OnDeactivated() { }  // 用户切换到其他工具时调用
}
```

取色器在 `OnActivated()` 中注册全局钩子，在 `OnDeactivated()` 中注销。主程序 `MainViewModel.SelectedTool` setter 中自动调用。这个改动量小（+4 行接口 + 2 行 ViewModel），但为未来取色器、快捷键监听、屏幕测量等全局工具铺平道路。

### 5.4 需管理员权限的工具

| 工具 # | 需要 UAC 的操作 |
|--------|----------------|
| 3 | `ipconfig /flushdns` |
| 4 | `powercfg /S` |
| 6 | 备份/恢复 hosts 文件 |
| 7 | `netsh interface ip set dns` |
| 15 | `MoveFileEx` 标记重启删除 |

**统一策略**：所有需提权的操作使用 `Process.Start` + `Verb = "runas"` 触发 UAC，不尝试静默提权。

### 5.5 需要引入 NuGet 包的工具

| 工具 # | NuGet 包 | 用途 |
|--------|----------|------|
| 14 | QRCoder | 二维码生成（MIT 协议，纯 .NET） |

在 `Toolbox.Plugins.csproj` 中添加：

```xml
<ItemGroup>
  <PackageReference Include="QRCoder" Version="1.*" />
</ItemGroup>
```

---

## 六、汇总总表

| # | 工具 | 文件 | 难度 | 代码量 | P/Invoke | UAC | NuGet | 可立即做 |
|---|------|------|:---:|:------:|:--------:|:---:|:-----:|:--------:|
| 1 | 重启资源管理器 | `RestartExplorerTool.cs` | 低 | 60 | ❌ | ❌ | ❌ | ✅ |
| 2 | 重建图标缓存 | `RebuildIconCacheTool.cs` | 低 | 70 | ❌ | ❌ | ❌ | ✅ |
| 3 | 刷新 DNS 缓存 | `FlushDnsTool.cs` | 低 | 50 | ❌ | ✅ | ❌ | ✅ |
| 4 | 电源计划切换 | `PowerPlanSwitchTool.cs` | 中 | 130 | ❌ | ✅ | ❌ | ✅ |
| 5 | 端口占用查询+释放 | `PortCheckerTool.cs` | 中 | 150 | ❌ | ❌ | ❌ | ✅ |
| 6 | Hosts 快速编辑 | `HostsEditorTool.cs` | 低 | 60 | ❌ | ✅ | ❌ | ✅ |
| 7 | DNS 一键切换 | `DnsSwitchTool.cs` | 中 | 140 | ❌ | ✅ | ❌ | ✅ |
| 8 | 文件 Hash 校验 | `FileHashTool.cs` | 低 | 120 | ❌ | ❌ | ❌ | ✅ |
| 9 | 系统代理一键开关 | `ProxyToggleTool.cs` | 低 | 100 | ✅ | ❌ | ❌ | ✅ |
| 10 | 窗口置顶 | `AlwaysOnTopTool.cs` | 低 | 90 | ✅ | ❌ | ❌ | ✅ |
| 11 | 取色器 | `ColorPickerTool.cs` | 中 | 180 | ✅ | ❌ | ❌ | ⚠️ 需钩子 |
| 12 | 桌面图标一键显隐 | `DesktopIconsToggleTool.cs` | 低 | 60 | ❌ | ❌ | ❌ | ✅ |
| 13 | 文本处理工具箱 | `TextToolbox.cs` | 低 | 200 | ❌ | ❌ | ❌ | ✅ |
| 14 | 二维码生成器 | `QrCodeTool.cs` | 低 | 110 | ❌ | ❌ | ✅ | ✅ |
| 15 | 强制删除被占用文件 | `ForceDeleteTool.cs` | 低 | 80 | ✅ | ✅ | ❌ | ✅ |
| 16 | 空文件夹查找清理 | `EmptyFolderCleanerTool.cs` | 低 | 180 | ❌ | ❌ | ❌ | ✅ |

- ✅ **可立即做** = 不需要改 `ITool` 接口，纯在插件层加文件即可
- ⚠️ **需钩子** = 需要先给 `ITool` 加生命周期方法

---

*生成日期：2026-06-27*