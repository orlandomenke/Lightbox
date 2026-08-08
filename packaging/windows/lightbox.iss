; Lightbox — Inno Setup definition.
;
; CI does not run this yet: today's distribution is the self-contained zip
; the build workflow uploads, and the app runs from anywhere without
; installing. This file exists so the day an installer ships, the branding
; is already right — the icon below is the same split-gray tile the EXE,
; the windows and the taskbar wear (docs/design/brand/gen_logo.py is the
; source of all of them).
;
; Build (on a machine with Inno Setup 6):
;   1. dotnet publish src/Lightbox.App/Lightbox.App.csproj -c Release
;      -r win-x64 --self-contained true -p:PublishTrimmed=false
;      -p:DebugType=embedded -o publish/win-x64
;   2. dotnet publish src/Lightbox.Mcp/Lightbox.Mcp.csproj -c Release
;      -r win-x64 --self-contained true -p:PublishTrimmed=false
;      -p:DebugType=embedded -o publish/win-x64/mcp
;   3. iscc packaging\windows\lightbox.iss

#define AppName "Lightbox"
#define AppPublisher "Lightbox"
#define AppExe "Lightbox.App.exe"
; Keep in step with the release; a real pipeline passes /DAppVersion=x.y.z.
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{7E9B7C42-30AF-4E9B-9B7D-1B4E3C6A5D21}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
; The setup executable's own icon — the first thing anybody sees of the
; installer, so it is the brand tile, not Inno's default.
SetupIconFile=..\..\docs\design\brand\lightbox.ico
; Add/Remove Programs shows this beside the entry.
UninstallDisplayIcon={app}\{#AppExe}
OutputBaseFilename=Lightbox-setup-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; The app is per-user friendly (it never needs admin at runtime); lowest
; friction wins until something needs machine-wide state.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Files]
Source: "..\..\publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
; Shortcuts take the EXE's own embedded icon — the same .ico the csproj
; embeds via ApplicationIcon — so there is exactly one icon to update.
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
