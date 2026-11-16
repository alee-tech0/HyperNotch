; Script Inno Setup per HyperNotch
; Apri questo file con Inno Setup Compiler e premi Compile (F9)

#define MyAppName "HyperNotch"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ItsAlee"
#define MyAppExeName "HyperNotch.exe"

[Setup]
AppId={{8F6E2A1B-9C4D-4E7A-B123-7D4C5E6F8A90}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\website\downloads
OutputBaseFilename=HyperNotch_Setup
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dark windows11 hidebevels
WizardBackColor=#0B0E17
WizardBackImageFile=installer_bg.png
WizardResizable=no
DisableWelcomePage=no
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
