; Only the optional Frame Server component needs a fixed, machine-readable location.
; The portable tray executable is never copied here.
#define MediaSourceClsid "{{EF0883BB-0F68-4106-B1D3-3B3B786D600D}"
#define ComponentDir "{autopf}\CamTurn Camera"

[Setup]
AppId={{7087BC8F-6182-48E8-9556-A37DC43497EA}
AppName=CamTurn Camera
AppVersion={#AppVersion}
AppPublisher=Aditya Katre
DefaultDirName={#ComponentDir}
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
CreateAppDir=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=CamTurn.CameraComponent
SetupIconFile=..\assets\icons\camturn.ico
UninstallDisplayName=CamTurn Camera
Compression=lzma2
SolidCompression=yes
CloseApplications=no

[Files]
Source: "{#NativeDir}\camturn_mediasource.dll"; DestDir: "{app}"; Flags: ignoreversion uninsrestartdelete
Source: "{#NativeDir}\camturn_native.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#NativeDir}\camturn_camera_control.exe"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
Root: HKLM64; Subkey: "Software\Classes\CLSID\{#MediaSourceClsid}"; ValueType: string; ValueName: ""; ValueData: "CamTurn Media Source"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\Classes\CLSID\{#MediaSourceClsid}\InprocServer32"; ValueType: string; ValueName: ""; ValueData: "{app}\camturn_mediasource.dll"
Root: HKLM64; Subkey: "Software\Classes\CLSID\{#MediaSourceClsid}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Both"; AfterInstall: ChangeCamera('--register')

[Code]
function InitializeSetup(): Boolean;
var
  Existing: String;
begin
  Result := True;
  if RegQueryStringValue(HKLM64, 'Software\Classes\CLSID\{EF0883BB-0F68-4106-B1D3-3B3B786D600D}\InprocServer32', '', Existing) then
    if CompareText(Existing, ExpandConstant('{#ComponentDir}\camturn_mediasource.dll')) <> 0 then
    begin
      SuppressibleMsgBox('Remove the older CamTurn installation before replacing its camera component.', mbError, MB_OK, IDOK);
      Result := False;
    end;
end;

procedure ChangeCamera(Argument: String);
var
  Code: Integer;
begin
  if not Exec(ExpandConstant('{app}\camturn_camera_control.exe'), Argument, ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) then
    RaiseException('Could not start camera registration: ' + SysErrorMessage(Code));
  if Code < 0 then
    RaiseException(Format('Windows could not change the camera registration (0x%.8x).', [Code]));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    ChangeCamera('--unregister');
end;
