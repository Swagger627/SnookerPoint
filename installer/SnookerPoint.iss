; Snooker Point — Windows x64 installer (Inno Setup)
;
; Tool-selection note: Inno Setup was chosen over WiX for the pilot — it is a stable, widely-used,
; free installer that packages a self-contained app with minimal ceremony, produces a proper
; uninstaller and Add/Remove Programs entry, and does not require the WiX XML toolchain.
;
; Build (requires Inno Setup 6 installed — see the pilot installation guide):
;   1. dotnet publish src/SnookerPoint.App -c Release -r win-x64 --self-contained true \
;        -p:LicenseProfile=Pilot -o publish/win-x64        (needs the approved public key set)
;   2. iscc installer/SnookerPoint.iss
;
; The installer NEVER contains the private signing key or the LicenceIssuer tool.

#define AppName "Snooker Point"
#define AppVersion "1.0.1"
#define AppPublisher "Snooker Point"
#define AppExeName "SnookerPoint.App.exe"

[Setup]
AppId={{4B2C9E71-8B1D-4E2A-9A57-5F0C7C1A9D01}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
OutputBaseFilename=SnookerPoint-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Windows 10/11 x64 only; installs per-machine (Program Files) so admin rights are needed to INSTALL,
; but activation afterwards does not require admin.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
DisableProgramGroupPage=yes
; SetupIconFile omitted until a branded .ico asset is added (uses the default Setup icon).
; No console window — the app is a WinExe.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
; The self-contained win-x64 publish output. No separate .NET runtime is required.
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Dirs]
; Machine-level licence checkpoint under ProgramData, writable by all users so switching Windows
; users never starts a new trial. Only THIS folder is granted user-modify — not the app folder.
Name: "{commonappdata}\SnookerPoint"; Permissions: users-modify
Name: "{commonappdata}\SnookerPoint\License"; Permissions: users-modify

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove only application binaries (installed under {app}). Business data and activation state live
; under %AppData% and %ProgramData% and are deliberately NOT deleted here.
Type: filesandordirs; Name: "{app}"

[Messages]
; Reassure the user at uninstall that their data is preserved.
ConfirmUninstall=Do you want to remove {#AppName}? Your business data (sales, inventory, bookings), backups and licence/activation will be PRESERVED and are not deleted.

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    MsgBox('Snooker Point has been removed. Your business data, backups and licence/activation were preserved on this computer. Reinstalling will continue from your existing data.', mbInformation, MB_OK);
end;
