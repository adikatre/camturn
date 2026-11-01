# Development

## Layout

```
src/
  CamTurn.Core/              pure logic, no OS calls: orientation math and tracker, rotation policy, compositor,
                             pipeline controller, engine, frame protocol + pipe host/client, config, test pattern
  CamTurn.Platform.Windows/  WinRT sensors and camera, ACL'd pipe, virtual camera registrar, autostart,
                             session events, display/posture, diagnostics, logging
  CamTurn.App/               WPF tray app, settings window, first-run wizard, CLI (CamTurn.exe)
  CamTurn.MediaSource/       C++: camturn_mediasource.dll (Frame Server media source),
                             camturn_native.dll (MFCreateVirtualCamera wrapper), portable protocol/placeholder code
  CamTurn.Simulator/         device digital twin (Inspiron 16 7640 2-in-1 profile) + camturn-sim CLI (CamTurn.Sim.Cli)
  CamTurn.Testing/           fakes: FakeCamera, NullVirtualCamera, test pipes
tests/                       Core, Simulator (incl. end-to-end over a real pipe), Windows integration; fixtures/
sim/                         device profiles and scenarios
installer/                   Inno Setup script
build/                       build-windows.ps1, smoke-test.ps1
scripts/                     probe-device.ps1 (hardware probe), bootstrap-dotnet.sh, check-no-streaming-deps.sh
assets/                      icon generator and generated icons
```

## What builds where

| Part | Linux / macOS | Windows 11 |
|---|---|---|
| Core, Simulator, Testing, their tests, `camturn-sim` | build + run | build + run |
| Platform.Windows, App, Windows tests | **build only** (`EnableWindowsTargeting`) | build + run |
| Media source: portable code + tests | build + run (g++/clang) | build + run (MSVC) |
| Media source and helper DLLs | – | build (MSVC) |
| Installer | – | Inno Setup 6 |

## Prerequisites

- .NET 10 SDK (10.0.100 or newer feature band; `global.json`). On a fresh Linux container: `scripts/bootstrap-dotnet.sh`.
- Windows: Visual Studio 2022 (or Build Tools) with **Desktop development with C++** (MSVC v143, Windows 11
  SDK 10.0.22621 or newer, C++ CMake tools) and the **.NET desktop development** workload, and
  [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer.

## Everyday commands

```bash
dotnet build CamTurn.sln
```
```bash
dotnet test tests/CamTurn.Core.Tests
```
```bash
dotnet test tests/CamTurn.Simulator.Tests
```
```bash
dotnet format CamTurn.sln --verify-no-changes
```

On Windows, the full build (native DLLs, tests, self-contained publish, installer):

```powershell
./build/build-windows.ps1
```

## Running a dev build without the installer (Windows, admin once)

The media source must be registered under HKLM, and Frame Server (LocalService) must be able to read the DLL, so
**don't** register it from a folder under your user profile. From an elevated PowerShell in the repository:

```powershell
./build/build-windows.ps1 -SkipInstaller
```
```powershell
New-Item -ItemType Directory -Force C:\CamTurnDev | Out-Null; Copy-Item artifacts\publish\* C:\CamTurnDev -Recurse -Force
```
```powershell
$clsid = '{EF0883BB-0F68-4106-B1D3-3B3B786D600D}'; New-Item -Force "HKLM:\SOFTWARE\Classes\CLSID\$clsid\InprocServer32" | Out-Null; Set-ItemProperty "HKLM:\SOFTWARE\Classes\CLSID\$clsid" '(default)' 'CamTurn Media Source'; Set-ItemProperty "HKLM:\SOFTWARE\Classes\CLSID\$clsid\InprocServer32" '(default)' 'C:\CamTurnDev\camturn_mediasource.dll'; Set-ItemProperty "HKLM:\SOFTWARE\Classes\CLSID\$clsid\InprocServer32" 'ThreadingModel' 'Both'
```
```powershell
C:\CamTurnDev\CamTurn.exe --register
```

Then, as your normal user:

```powershell
C:\CamTurnDev\CamTurn.exe --test-pattern --simulate-orientation 180
```

To unregister the dev build (elevated):

```powershell
C:\CamTurnDev\CamTurn.exe --unregister; Remove-Item -Recurse "HKLM:\SOFTWARE\Classes\CLSID\{EF0883BB-0F68-4106-B1D3-3B3B786D600D}"
```

If Frame Server still has the DLL loaded, restart the "Windows Camera Frame Server" service or reboot before
replacing it.

## Debugging tips

- **App side without Frame Server:** start CamTurn, then run `CamTurn.exe --pipe-probe` in a terminal. It connects
  like the media source does and reports frames, rotation and fps.
- **No convertible handy:** `--simulate-device dell-inspiron-16-7640-2in1` drives sensors and camera from the
  simulator, and the tray gains a "Simulated pose" menu. `--simulate-orientation N` fakes just the sensor.
- **Media source traces:** build the DLL with `-DCAMTURN_TRACE` (or Debug) and watch `OutputDebugString` with
  DebugView. The DLL runs in the Frame Server `svchost.exe`, so attach the debugger there.
- **Logs:** `%LOCALAPPDATA%\CamTurn\logs\camturn.log` (redacted).
- **Simulator:** see [SIMULATOR.md](SIMULATOR.md).
