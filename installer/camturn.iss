; CamTurn installer (Inno Setup 6). Built by build/build-windows.ps1 on CI and locally.
;
; Per-machine install to Program Files (readable by the Frame Server's LocalService account), the media source
; COM class registered under HKLM (Frame Server does not read per-user classes), and "CamTurn Camera" created for
; all users by running CamTurn.exe --register elevated. One UAC prompt at install; none at runtime.
; See docs/DECISIONS.md D-008 and D-010.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#define MediaSourceClsid "{{EF0883BB-0F68-4106-B1D3-3B3B786D600D}"

[Setup]
AppId={{9583B875-E9C5-4DE3-B291-6E367224D65B}
AppName=CamTurn
AppVersion={#AppVersion}
AppVerName=CamTurn {#AppVersion}
AppPublisher=Aditya Katre
AppPublisherURL=https://github.com/adikatre/camturn
AppSupportURL=https://github.com/adikatre/camturn/issues
DefaultDirName={autopf}\CamTurn
DefaultGroupName=CamTurn
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 11 (build 22000) is the first release with the virtual camera API.
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=CamTurn-Setup-{#AppVersion}
SetupIconFile=..\assets\icons\camturn.ico
UninstallDisplayIcon={app}\CamTurn.exe
UninstallDisplayName=CamTurn
LicenseFile=..\LICENSE
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "autostart"; Description: "Start CamTurn when I sign in to Windows"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "camturn_mediasource.dll"; Flags: ignoreversion recursesubdirs createallsubdirs
; Frame Server may still have the media source loaded during upgrade/uninstall: replace or delete it on reboot then.
Source: "{#PublishDir}\camturn_mediasource.dll"; DestDir: "{app}"; Flags: ignoreversion restartreplace uninsrestartdelete

[Registry]
Root: HKLM64; Subkey: "Software\Classes\CLSID\{#MediaSourceClsid}"; ValueType: string; ValueName: ""; ValueData: "CamTurn Media Source"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\Classes\CLSID\{#MediaSourceClsid}\InprocServer32"; ValueType: string; ValueName: ""; ValueData: "{app}\camturn_mediasource.dll"
Root: HKLM64; Subkey: "Software\Classes\CLSID\{#MediaSourceClsid}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Both"

[Icons]
Name: "{autoprograms}\CamTurn"; Filename: "{app}\CamTurn.exe"

[Run]
Filename: "{app}\CamTurn.exe"; Parameters: "--register"; StatusMsg: "Creating ""CamTurn Camera""..."; Flags: runhidden waituntilterminated
Filename: "{app}\CamTurn.exe"; Parameters: "--set-autostart on"; Tasks: autostart; Flags: runhidden waituntilterminated runasoriginaluser
Filename: "{app}\CamTurn.exe"; Description: "Start CamTurn now"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM CamTurn.exe /F"; RunOnceId: "StopCamTurn"; Flags: runhidden waituntilterminated
Filename: "{app}\CamTurn.exe"; Parameters: "--unregister"; RunOnceId: "RemoveCamera"; Flags: runhidden waituntilterminated

[UninstallDelete]
; Settings and logs of the user running the uninstaller (other users' copies are documented in the README).
Type: filesandordirs; Name: "{userappdata}\CamTurn"
Type: filesandordirs; Name: "{localappdata}\CamTurn"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CamTurn');
end;
