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
; Toolbox self-contained 单文件发布（自带 .NET 运行时，无需额外检测和安装）
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Toolbox"; Filename: "{app}\Toolbox.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall Toolbox"; Filename: "{uninstallexe}"
Name: "{commondesktop}\Toolbox"; Filename: "{app}\Toolbox.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Toolbox.exe"; Description: "Launch Toolbox"; Flags: nowait postinstall skipifsilent
