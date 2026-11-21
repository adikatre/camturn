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
installer/                   optional camera component packaging
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
| Portable EXE and optional camera component | – | .NET publish and Inno Setup 6 |

## Prerequisites

- .NET 10 SDK (10.0.100 or newer feature band; `global.json`). On a fresh Linux container: `scripts/bootstrap-dotnet.sh`.
- Windows: Visual Studio 2022 (or Build Tools) with **Desktop development with C++** (MSVC v143, Windows 11
  SDK 10.0.22621 or newer, C++ CMake tools) and the **.NET desktop development** workload, and
  [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the embedded camera component.

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

To build the portable app and run the existing checks on a Windows development machine, use

```powershell
./build/build-windows.ps1
```

The distributable is `artifacts\portable\CamTurn.exe`, with a SHA-256 checksum alongside it.
Pass `-SkipTests` to build without running tests or launching the published app.

## Using the portable build

The EXE runs under the current user. Double-click its tray icon for setup and settings. The camera registration
button launches the embedded camera component tool with administrator approval. The component has a fixed
location in Program Files so Frame Server can still load it after the portable app moves. Use
`CamTurn.exe --register` and `CamTurn.exe --unregister` for the equivalent command-line operations.
An existing camera from the older installer can serve the portable app. To transfer management of that camera,
remove the older installation before registering the portable build's component.

## Debugging tips

- **App side without Frame Server:** start CamTurn, then run `CamTurn.exe --pipe-probe` in a terminal. It connects
  like the media source does and reports frames, rotation and fps.
- **No convertible handy:** `--simulate-device dell-inspiron-16-7640-2in1` drives sensors and camera from the
  simulator, and the tray gains a "Simulated pose" menu. `--simulate-orientation N` fakes just the sensor.
- **Media source traces:** build the DLL with `-DCAMTURN_TRACE` (or Debug) and watch `OutputDebugString` with
  DebugView. The DLL runs in the Frame Server `svchost.exe`, so attach the debugger there.
- **Logs:** `%LOCALAPPDATA%\CamTurn\logs\camturn.log` (redacted).
- **Simulator:** see [SIMULATOR.md](SIMULATOR.md).
