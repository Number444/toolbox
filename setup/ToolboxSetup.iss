[Setup]
AppId={{B8A3C8E2-4A5D-4F8E-9B2C-1D3E5F7A8B9C}}
AppName=Toolbox
AppVersion=1.0.0
AppPublisher=Number444
DefaultDirName={autopf}\Toolbox
DefaultGroupName=Toolbox
OutputDir=.
OutputBaseFilename=Toolbox_Setup
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=admin
CloseApplications=yes
UninstallDisplayName=Toolbox
DisableWelcomePage=yes
DisableProgramGroupPage=yes
AllowNoIcons=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
; Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcut"; Flags: checkedonce

[Files]
; Toolbox 发布文件
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; .NET 9 Desktop Runtime 安装包（检查未安装时自动安装）
Source: "windowsdesktop-runtime-9.0.13-win-x64.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall; Check: not IsDotNet9Installed

[Icons]
Name: "{group}\Toolbox"; Filename: "{app}\Toolbox.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall Toolbox"; Filename: "{uninstallexe}"
Name: "{commondesktop}\Toolbox"; Filename: "{app}\Toolbox.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; 优先安装 .NET 运行时，再安装 Toolbox
Filename: "{tmp}\windowsdesktop-runtime-9.0.13-win-x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "正在安装 .NET 9 Desktop Runtime..."; Check: not IsDotNet9Installed; Flags: runascurrentuser
Filename: "{app}\Toolbox.exe"; Description: "Launch Toolbox"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNet9Installed: Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(
    ExpandConstant('{commonpf64}') + '\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.*',
    FindRec);
  if Result then
    FindClose(FindRec);
end;
