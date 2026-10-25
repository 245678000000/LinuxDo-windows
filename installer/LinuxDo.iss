; LinuxDo for Windows 安装程序（Inno Setup 6）
; 用法：ISCC /DAppVersion=2.0.0 /DArch=x64 installer\LinuxDo.iss
; 默认按当前用户安装到 %LOCALAPPDATA%\Programs\LinuxDo，不需要管理员权限。
; 卸载不会删除 %LOCALAPPDATA%\com.pake.acea3c3 中的登录状态和设置。

#ifndef AppVersion
  #define AppVersion "2.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#define SourceDir "..\artifacts\publish\" + Arch

[Setup]
AppId={{51FC28FD-671C-40AD-839B-E592FCB896A3}
AppName=LinuxDo
AppVersion={#AppVersion}
AppVerName=LinuxDo {#AppVersion}
AppPublisher=LinuxDo for Windows
AppPublisherURL=https://github.com/245678000000/LinuxDo-windows
AppSupportURL=https://github.com/245678000000/LinuxDo-windows/issues
DefaultDirName={autopf}\LinuxDo
DefaultGroupName=LinuxDo
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\artifacts
OutputBaseFilename=LinuxDo-{#AppVersion}-Setup-{#Arch}
SetupIconFile=..\src\LinuxDo\Resources\AppIcon.ico
UninstallDisplayIcon={app}\LinuxDo.exe
UninstallDisplayName=LinuxDo
VersionInfoVersion={#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
MinVersion=10.0.17763
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Languages]
#if FileExists(CompilerPath + "Languages\ChineseSimplified.isl")
Name: "zh"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
#elif FileExists(CompilerPath + "Languages\Unofficial\ChineseSimplified.isl")
Name: "zh"; MessagesFile: "compiler:Languages\Unofficial\ChineseSimplified.isl"
#endif
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\LinuxDo"; Filename: "{app}\LinuxDo.exe"
Name: "{autodesktop}\LinuxDo"; Filename: "{app}\LinuxDo.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\LinuxDo.exe"; Description: "{cm:LaunchProgram,LinuxDo}"; Flags: nowait postinstall skipifsilent
