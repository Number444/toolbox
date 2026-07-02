; Toolbox 安装程序 —— Inno Setup 6 脚本
; 检测 .NET 9 Desktop Runtime，未装则提示手动下载

#define MyAppName "Toolbox"
#define MyAppVersion "1.0"
#define MyAppPublisher "Toolbox"
#define MyAppURL "https://github.com/toolbox"
#define MyAppExeName "Toolbox.exe"

[Setup]
AppId={{B8A3C8E2-1F4D-4A6D-9C3E-7D5F2B1E4A8C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
VersionInfoVersion={#MyAppVersion}.0
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
CloseApplicationsFilter=*.exe
; 覆盖更新：AppId 保持不变，新安装包覆盖旧文件，开始菜单和卸载条目自动更新
UninstallDisplayName={#MyAppName} {#MyAppVersion}
DisableWelcomePage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: checkedonce

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Toolbox"; Flags: nowait postinstall skipifsilent

[Code]
// .NET 注册表路径（64 位系统上 Desktop Runtime 注册于此）
const
  DesktopRegPath = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';

/// <summary>
/// 检测 .NET 9 Desktop Runtime x64 是否已安装。
/// 枚举注册表值名，查找以 "9.0" 开头的条目（不绑定具体补丁版本号）。
/// </summary>
function IsNet9DesktopInstalled: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM, DesktopRegPath, Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      if Copy(Names[I], 1, 3) = '9.0' then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  if not IsNet9DesktopInstalled then
  begin
    if SuppressibleMsgBox(
      'Toolbox requires .NET 9 Desktop Runtime (x64) to run.' + #13#10 +
      'It does not appear to be installed on this system.' + #13#10 + #13#10 +
      'Do you want to open the download page now?' + #13#10 +
      '(Visit: https://dotnet.microsoft.com/en-us/download/dotnet/9.0)' + #13#10 + #13#10 +
      'Select "Yes" to open the download page in your browser.' + #13#10 +
      'Select "No" to continue without it (Toolbox will not launch).',
      mbConfirmation, MB_YESNO, IDYES) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/en-us/download/dotnet/9.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
  end;
  Result := True;
end;