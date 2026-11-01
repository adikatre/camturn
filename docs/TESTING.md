# Testing

Three layers: automated tests that run anywhere, Windows integration tests (CI and the laptop), and a manual
plan for the laptop. **As of 2026-11-01 only the first layer has actually been run** (on Linux). The Windows
layers are written but haven't run yet: the GitHub repository and CI aren't set up, and no Windows machine was
available to the author.

## 1. Automated, cross-platform (run: yes, on Linux)

```bash
dotnet test tests/CamTurn.Core.Tests
```
```bash
dotnet test tests/CamTurn.Simulator.Tests
```
```bash
dotnet run --project src/CamTurn.Sim.Cli -- --run-all
```
```bash
cmake -S src/CamTurn.MediaSource -B artifacts/native && cmake --build artifacts/native && ctest --test-dir artifacts/native
```

| Area | Tests |
|---|---|
| Rotation math, quadrant snapping, mapping and flip | `OrientationMathTests`, `FrameAndMappingTests`, `RotationPolicyTests` |
| Hysteresis (60° band, 300 ms hold), flat holding | `OrientationTrackerTests`, scenarios `small-tilts-no-flip`, `flat-hold` |
| Rotate/mirror/fit/fill on NV12 | `CompositorTests` |
| Camera on/off: consumers, 3 s grace, lock/suspend, pause, busy/missing retry | `PipelineControllerTests`, `EngineTests` |
| Config load/save/migration/clamping | `ConfigTests` |
| Frame protocol, golden bytes shared with C++ | `FrameProtocolTests`, `portable_tests.cpp` |
| Placeholder frame, NV12→YUY2 | `portable_tests.cpp` |
| Device model (Inspiron profile from the real probe) | `DeviceModelTests`, `CameraAndScenarioTests` |
| **Whole pipeline over a real named pipe** with a managed media-source stand-in: test pattern at all four orientations, simulated tent flip, grace-period close, busy retry, malformed handshake | `EngineTests` |

Measured on the dev container (cloud VM, 4 vCPU, Release build):
- Compositor, 1920×1080 NV12 in → 1280×720 out, with rotate, mirror and fit: **1.05 ms per frame**, about 3% of one
  core at 30 fps (`CompositorCostAt1080pInput`).
- Simulated laptop → tent fold: upright output **0.73 s** after the motion starts (target ≤ 1 s).

## 2. Windows integration tests (run: not yet)

`tests/CamTurn.Windows.Tests` runs on CI (`windows-latest`) and can run on the laptop:

```powershell
dotnet test tests/CamTurn.Windows.Tests
```

It covers the HKCU autostart round trip, the pipe DACL contents (user, LocalService, SYSTEM allowed; network
denied), a pipe round trip with real ACLs, virtual camera state reporting, sensor and camera enumeration, diagnostics
redaction, and CLI parsing. On machines without sensors or cameras, those tests only check that the APIs answer.

`build/smoke-test.ps1 -Installer <setup.exe>` installs silently, checks the HKLM registration, runs
`--diagnose` and `--pipe-probe` against `--test-pattern --simulate-orientation 180`, uninstalls, and checks for
leftovers. CI runs it on Windows Server 2025 as an experiment. Run it on a fresh Windows 11 VM for acceptance
criterion 6.

## 3. Manual plan on the laptop (run: not yet)

Prerequisites: install the CI-built installer, and keep `CamTurn.exe --diagnose` output for the report. For each
row, record pass/fail, the app version and notes.

### Virtual camera basics
| # | Step | Expected |
|---|---|---|
| M1 | Run `CamTurn.exe --test-pattern --simulate-orientation 180`, open the Windows Camera app, switch to CamTurn Camera | Sky at the top, yellow arrow pointing up, red disc top-left (acceptance 1) |
| M2 | Close CamTurn (tray → Quit) while the Camera app shows CamTurn Camera | "CamTurn Camera — waiting" placeholder within about 1 s; the app doesn't freeze or error |
| M3 | Start CamTurn again | Live picture returns without reopening the Camera app |
| M4 | `services.msc` → restart "Windows Camera Frame Server" during a call | App recovers or shows the placeholder; CamTurn keeps running; note the behaviour |

### Rotation (each app below × each orientation: laptop 0°, tablet portrait right 90°, tent 180°, tablet portrait left 270°)
| # | App | Expected |
|---|---|---|
| R1 | Windows Camera | Upright in all four; switches within about 1 s of folding (acceptance 2) |
| R2 | Microsoft Teams (new) | Same; also check the picture the **other side** of a test call receives |
| R3 | Zoom | Same, including the other side |
| R4 | Google Meet in Chrome | Same |
| R5 | Google Meet in Edge | Same |
| R6 | Discord (camera test, then a call) | Same |
| R7 | Lay the tablet flat on the desk | Last orientation is kept; status says "holding" |
| R8 | Turn rotation lock on and rotate | Desktop doesn't rotate; camera picture still corrects |
| R9 | If any orientation is wrong: Settings → Flip direction | All four orientations correct afterwards; record which mapping was needed |

### Camera lifecycle and privacy
| # | Step | Expected |
|---|---|---|
| L1 | CamTurn running in Auto mode, no app using the camera | Camera light **off** (acceptance 3) |
| L2 | Open, then close, an app using CamTurn Camera | Light on while open; off about 3 s after closing |
| L3 | Open the settings window | Light on (the preview uses the camera); off about 3 s after closing it |
| L4 | Lock (Win+L) during a call; unlock | Light off while locked; picture returns after unlock |
| L5 | Sleep during a call; wake | Same as L4 |
| L6 | Open the real camera in another app first (exclusive), then use CamTurn Camera | Status "Camera busy or blocked"; recovers when the other app closes |
| L7 | Settings → Privacy → Camera → turn off desktop app access | Clear "blocked" message; recovers when turned back on |
| L8 | Unplug/replug a USB camera selected in CamTurn | "Camera not found", then recovers |

### Performance (acceptance 4)
| # | Step | Record |
|---|---|---|
| P1 | 1080p30 camera into a 1280×720 consumer, in tent mode, for 2 minutes; Task Manager → Details → CamTurn.exe CPU | Average CPU %. Target < 10% |
| P2 | Same with a 1920×1080 consumer | Average CPU % |

### Install / uninstall (acceptance 5 and 6)
| # | Step | Expected |
|---|---|---|
| I1 | Fresh Windows 11 VM: run the installer | Exactly one UAC prompt; wizard opens; CamTurn Camera listed in the Camera app |
| I2 | Use CamTurn without admin rights afterwards | No further UAC prompts |
| I3 | Close the settings window | CamTurn stays in the tray |
| I4 | Tray → Quit | Process exits; camera light off; pipe gone |
| I5 | Uninstall | CamTurn Camera disappears from apps; `HKLM\Software\Classes\CLSID\{EF0883BB-…}` gone; `C:\Program Files\CamTurn` gone (or gone after reboot); HKCU Run value gone; `%APPDATA%\CamTurn` gone |
| I6 | `build/smoke-test.ps1` on the VM | All checks pass |

## What could not be run by the author

Everything in sections 2 and 3: the MSVC build of the media source and helper DLL, Frame Server loading the
media source, LocalService connecting through the pipe ACL, `MFCreateVirtualCamera` registration, WinRT sensors and
camera capture, the WPF UI, the installer, and all app compatibility. These are the first things to check on the
laptop. The most likely failure points are listed in [KNOWN_ISSUES.md](KNOWN_ISSUES.md).
