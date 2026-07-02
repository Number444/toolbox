# Toolbox 安装包构建实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Toolbox 创建一个专业的 Windows 安装包（~2-3MB），用户下载后双击即可安装，无需手动安装 .NET 9 运行时。

**架构：** 使用 Inno Setup 6 构建安装器。应用以 **framework-dependent（框架依赖）** 模式发布（exe 约 100KB，不含 .NET 运行时），安装器负责检测目标机器是否已装 .NET 9 Desktop Runtime x64，未装则静默下载安装。最终交付一个 `Toolbox_Setup.exe`。

**Tech Stack:** Inno Setup 6 / .NET 9 framework-dependent publish / Pascal Script (Inno Setup Code section)

**涉及文件：**
- Create: `setup/ToolboxSetup.iss`（Inno Setup 脚本）
- Modify: 无（仅新增）

---

### Task 1: 安装 Inno Setup 6

- [ ] **Step 1: 通过 winget 安装 Inno Setup 6**

```powershell
winget install --id JRSoftware.InnoSetup -e --accept-source-agreements
```

Expected: 安装完成后 `iscc.exe` 可用。

- [ ] **Step 2: 验证安装成功**

```powershell
where.exe iscc
```

Expected: 输出类似 `C:\Program Files (x86)\Inno Setup 6\iscc.exe`

- [ ] **Step 3: 提交**

```bash
git add .
git commit -m "chore: install Inno Setup 6 for installer building"
```

---

### Task 2: 以 framework-dependent 模式发布 Toolbox

- [ ] **Step 1: 执行 framework-dependent 发布**

```powershell
cd "d:\Agent Space\Toolbox"
dotnet publish Toolbox.csproj -c Release -p:RuntimeIdentifier=win-x64 -o setup\publish
```

Expected: 输出到 `setup\publish\`，包含 `Toolbox.exe`（约 100KB）+ 所有依赖 DLL + `Toolbox.runtimeconfig.json`。

- [ ] **Step 2: 验证输出结构**

```powershell
Get-ChildItem "setup\publish\" -Name | Sort-Object
```

Expected:
- `Toolbox.exe` — 主程序（约 100KB，不含运行时）
- `Toolbox.dll` — 主程序集
- `Toolbox.Core.dll` — Core 库
- `Toolbox.Plugins.dll` — 插件库
- `QRCoder.dll` — NuGet 依赖
- `Toolbox.runtimeconfig.json` — 运行时配置
- 以及其他 .NET 系统 DLL（`System*.dll`、`PresentationFramework.dll` 等）

注意：这次没有 `win-x64\publish\` 中间层，文件直接输出到 `setup\publish\`。

- [ ] **Step 3: 提交**

```bash
git add setup/publish/
git commit -m "build: publish Toolbox as framework-dependent (Release, win-x64)"
```

---

### Task 3: 创建 Inno Setup 脚本

注意：Inno Setup 脚本必须手动编写（无模板），以下为完整内容。

- [ ] **Step 1: 创建 `setup\ToolboxSetup.iss`**

```iss
; Toolbox 安装程序 —— Inno Setup 6 脚本
; 检测 .NET 9 Desktop Runtime，未装则静默下载安装

#define MyAppName "Toolbox"
#define MyAppVersion "1.0"
#define MyAppPublisher "Toolbox"
#define MyAppURL "https://github.com/toolbox"
#define MyAppExeName "Toolbox.exe"

[Setup]
AppId={{B8A3C8E2-1F4D-4A6D-9C3E-7D5F2B1E4A8C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=.
OutputBaseFilename=Toolbox_Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
CloseApplications=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: checkedonce

[Files]
; 发布目录的全部文件（递归包含所有 DLL 和配置文件）
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 安装完成后可选启动
Filename: "{app}\{#MyAppExeName}"; Description: "启动 Toolbox"; Flags: nowait postinstall skipifsilent

[Code]
// ── .NET 9 Desktop Runtime 检测 ─────────────────────────────────────────

// Windows 注册表中 .NET 9 的版本检查路径
// .NET 9 Desktop Runtime x64 会写入：
// HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App\9.0.0
const
  Net9RegPath = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  Net9RegKey = '9.0.0';
  DotnetDownloadUrl = 'https://dotnet.microsoft.com/en-us/download/dotnet/9.0';
  WindowsDesktopRuntimeUrl = 'https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-9.0.0-windows-x64';

/// <summary>
/// 检查 .NET 9 Desktop Runtime x64 是否已安装。
/// 读取注册表 HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App\9.0.0
/// </summary>
function IsNet9DesktopInstalled: Boolean;
var
  Version: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM, Net9RegPath, Net9RegKey, Version) then
  begin
    // 如果版本号非空，认为已安装
    Result := (Version <> '');
  end;
end;

/// <summary>
/// 获取 .NET 9 Desktop Runtime 下载 URL。
/// 使用官方下载页面，Inno Setup 的 DownloadTemporaryFile 会跟随重定向到实际下载链接。
/// </summary>
function GetNet9DownloadUrl: String;
begin
  Result := WindowsDesktopRuntimeUrl;
end;

/// <summary>
/// 准备安装前检测 .NET 运行时。
/// 如果未安装，询问用户是否下载安装。
/// </summary>
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  DownloadUrl: String;
  DownloadedInstaller: String;
  ResultCode: Integer;
begin
  if IsNet9DesktopInstalled then
  begin
    // .NET 9 Desktop Runtime 已安装，无需额外操作
    Result := '';
    Exit;
  end;

  // .NET 9 Desktop Runtime 未安装 -> 询问用户
  if SuppressibleMsgBox(
    'Toolbox 需要 .NET 9 Desktop Runtime (x64) 才能运行。' + #13#10 +
    '是否现在下载并安装？（约 60MB）' + #13#10 + #13#10 +
    '选择"是"：自动下载并静默安装' + #13#10 +
    '选择"否"：跳过，但 Toolbox 将无法启动',
    mbConfirmation,
    MB_YESNO,
    IDYES
  ) <> IDYES then
  begin
    // 用户选择跳过
    Result := '';
    Exit;
  end;

  // 下载 .NET 9 Desktop Runtime 安装包
  DownloadUrl := GetNet9DownloadUrl();
  DownloadedInstaller := ExpandConstant('{tmp}{\}windowsdesktop-runtime-9.0.0-win-x64.exe');

  // 显示下载进度
  if not DownloadTemporaryFile(DownloadUrl, 'windowsdesktop-runtime-9.0.0-win-x64.exe', '', nil) then
  begin
    Result := '无法下载 .NET 9 Desktop Runtime。' + #13#10 +
              '请手动访问 ' + DotnetDownloadUrl + ' 下载安装。';
    Exit;
  end;

  // 静默安装（/quiet /norestart）
  if not Exec(DownloadedInstaller, '/quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := '.NET 9 Desktop Runtime 安装失败（错误代码: ' + IntToStr(ResultCode) + '）。' + #13#10 +
              '请手动访问 ' + DotnetDownloadUrl + ' 下载安装。';
    Exit;
  end;

  // 安装成功
  Result := '';
end;
```

注意：`.NET 下载 URL` 中的版本号 9.0.0 需要替换为 .NET 9 的最新正式版本号（如 9.0.6）。当前 `9.0.0` 是 placeholder，需确认 `https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-9.0.0-windows-x64` 是否有效。如果无效，应使用更通用的下载页面 URL `https://dotnet.microsoft.com/en-us/download/dotnet/9.0` 并引导用户手动下载。

- [ ] **Step 2: 验证脚本语法**

```powershell
# iscc 编译检查（不实际构建）
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" "d:\Agent Space\Toolbox\setup\ToolboxSetup.iss"
```

Expected: 编译成功，输出 `Toolbox_Setup.exe`。如果报错，检查 `.iss` 语法（常见问题：中文注释的编码、路径中的空格、URL 格式）。

- [ ] **Step 3: 提交**

```bash
git add setup/ToolboxSetup.iss setup/publish/
git commit -m "feat: add Inno Setup installer script with .NET 9 runtime detection"
```

---

### Task 4: 验证安装包

- [ ] **Step 1: 确认安装包大小**

```powershell
Get-Item "d:\Agent Space\Toolbox\setup\Toolbox_Setup.exe" | Select-Object Length
```

Expected: 约 2-3MB（仅为发布产物 + Inno Setup 压缩后的安装器壳，不含 .NET 运行时）。

- [ ] **Step 2: 在本机测试安装（交互式）**

```powershell
# 直接运行安装包
& "d:\Agent Space\Toolbox\setup\Toolbox_Setup.exe"
```

Expected:
- 安装器启动，显示中文向导界面
- 如果本机已装 .NET 9，直接进入安装步骤
- 完成后启动 Toolbox
- 所有功能正常（工具列表、音乐悬浮窗等）

- [ ] **Step 3: 在目标机器测试**

将 `Toolbox_Setup.exe` 复制到另一台 Windows 11 电脑：
1. 双击安装包
2. 如果未装 .NET 9，安装器应弹出下载提示 → 自动下载安装
3. 安装完成后 Toolbox 正常运行

- [ ] **Step 4: 提交最终版本**

```bash
git add setup/
git commit -m "feat: Toolbox installer complete - Toolbox_Setup.exe (~2-3MB)"
```

---

### 部署与维护说明

**发布流程（每次版本更新时）：**

```powershell
# 1. 构建发布产物
cd "d:\Agent Space\Toolbox"
dotnet publish Toolbox.csproj -c Release -p:RuntimeIdentifier=win-x64 -o setup\publish

# 2. 编译安装包
& "C:\Program Files (x86)\Inno Setup 6\iscc.exe" setup\ToolboxSetup.iss

# 3. 产物位置
# setup\Toolbox_Setup.exe
```

**.NET 版本更新时：**

当 .NET 9 发布新版本时，需要更新 `ToolboxSetup.iss` 中的：
- `WindowsDesktopRuntimeUrl`（下载链接中的版本号）
- `Net9RegKey`（注册表检查的版本号，可选，旧版本通常向下兼容）

**卸载方式：**

安装器会在"开始菜单"创建卸载快捷方式，也支持通过"设置 > 应用 > 应用和功能"卸载。卸载会删除 `%ProgramFiles%\Toolbox` 目录和所有快捷方式，不影响 .NET 运行时（由 Windows 单独管理）。

**优势总结：**

| 对比项 | SelfContained 单文件（之前方案） | Framework-Dependent 安装包（此方案） |
|---|---|---|
| 下载大小 | 153MB | **~3MB** |
| 首次启动时间 | 10-30 秒（自解压） | **即点即开** |
| .NET 运行时 | 内置在 exe 中 | 安装器按需下载或复用系统已装 |
| 更新方式 | 重新下载整个 exe | 安装包内仅应用代码部分需更新 |
| 专业度 | 一个裸 exe | **完整安装向导 + 开始菜单 + 卸载** |