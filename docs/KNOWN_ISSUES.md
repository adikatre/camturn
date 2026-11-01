# Known issues and unverified assumptions

Everything here was **not** verified on real hardware. Each item says how to check it.

## Confirmed on the real laptop (probe-device.ps1, 2026-11-01)

- Model "Inspiron 16 7640 2-in-1" (Dell Inc.), Windows 11 Home 26H2, build 26300. WMI chassis type is 10 (Notebook),
  not 31 (Convertible), so CamTurn must not rely on chassis type to detect a convertible.
- **One accelerometer, in the lid** (Intel Integrated Sensor Hub, HID VID_8087&PID_0AC2), default, minimum report
  interval 10 ms. Laptop pose reads x = 0.009, y = -0.980, z = -0.165, consistent with the Windows axis convention
  and a lid leaning back ~10°. No base accelerometer and **no hinge-angle sensor** are exposed, so the base+hinge
  fallback (D-003) is not needed on this device (it stays for other hardware).
- `ReadingTransform` defaults to `None`, and `Landscape` gives identical readings with the desktop unrotated.
- `SimpleOrientationSensor` present. `ConvertibleSlateMode` (laptop/slate) is reported. AutoRotation is enabled.
- Primary display 3840x2160.
- One camera: "Integrated Webcam" (Realtek, VID_0BDA&PID_5583), front panel, colour only, no IR stream.

## Still assumed by the simulator profile (`sim/devices/dell-inspiron-16-7640-2in1.json`)

| # | Assumption | How to verify on the laptop |
|---|---|---|
| K-3 | Sign of the lid sensor's X axis (which way portrait rolls read). Laptop pose already matches the convention. | `probe-device.ps1 -Poses` (tent, tablet portrait right/left) |
| K-4 | `ReadingTransform = DisplayOrientations.Native` really yields un-rotated readings when the desktop is rotated. | Rotate the desktop manually (Settings > Display > Orientation) while still; readings must not change |
| K-5 | Webcam is mounted unrotated and delivers unmirrored frames. | Windows Camera app in laptop mode |
| K-6 | Webcam formats (probe v1 found the camera but could not list formats; fixed in probe v2). | `probe-device.ps1` v2, or `camturn --list-cameras` |
| K-8 | Windows only auto-rotates in slate posture (the simulator approximates slate as hinge > 190°; the real device reports it via ConvertibleSlateMode). | Fold slowly from laptop to tent with rotation lock off |
| K-9 | Accelerometer noise ~0.02 g/axis (minimum interval measured: 10 ms). | `camturn --sensor-dump` with the laptop still on a desk |

## Verified on Windows CI (2026-11-01, GitHub-hosted Windows Server 2025, build 26100)

- **W-1 resolved:** both native DLLs build with MSVC (/W4 /WX, static CRT); WPF app and all .NET projects build.
- **W-4 resolved (on Server 2025):** the installer registers the media source in HKLM, and
  `MFCreateVirtualCamera(System, AllUsers)` succeeds. Apps see the device as **"CamTurn Camera (Windows Virtual
  Camera)"**: Windows appends the suffix, as documented. Uninstall removes the device, COM class, files and
  autostart entry.
- All 159 .NET tests pass on Windows, including the integration tests: pipe DACL contents, a pipe round trip with
  the real ACLs, and the autostart registry round trip.
- The published `CamTurn.exe` starts and `--diagnose` runs. CI caught and fixed a malformed `app.manifest` that
  stopped the exe from starting at all.
- With the app running `--test-pattern --simulate-orientation 180`, a managed consumer received about 28 fps,
  rotated 180°, over the pipe.
- When desktop camera access is off in Windows privacy settings, opening CamTurn Camera fails with CamTurn's
  "Camera access is blocked…" message, as intended.

## Not yet run anywhere (highest risk first)

| # | Risk | Why it might fail | First check |
|---|---|---|---|
| W-2 | Frame Server loads the media source and Windows Camera shows the placeholder | COM registration, activator attributes, sensor-profile filter string, or required stream attributes may differ from what Frame Server expects | TESTING M1 with CamTurn not running |
| W-3 | Media source in Frame Server (LocalService) can open the user's pipe | Pipe DACL grants LocalService. If Frame Server activates the source inside an AppContainer client process, the ACL won't admit it and the camera shows only the placeholder | TESTING M1 with CamTurn running; `--pipe-probe` works but the Camera app shows the placeholder → this issue |
| W-4b | Registration on Windows 11 client SKUs (verified only on Server 2025 so far) | Different SKU; the privacy setting must allow camera access | Installer on the laptop; `--diagnose` "visible to apps" |
| W-5 | MediaCapture with an exclusive NV12 frame reader on the Realtek webcam | Format negotiation, MJPG decode path, busy/privacy HRESULT mapping | TESTING L6, L7; `--list-cameras` |
| W-6 | Teams and other apps: the remote side gets the picture | One reference virtual camera sample reports Teams showing a local preview but not sending to the far end | TESTING R2 |
| W-7 | Some apps cache camera lists or don't list virtual cameras | App-specific | TESTING R1–R6 |
| W-8 | Frame Server restart during a call | The media source is recreated, and the pipe client reconnects on Start; the host app's behaviour is unknown | TESTING M4 |
| W-9 | WPF UI layout and Fluent theme in light/dark, keyboard navigation | Never rendered | Open settings in both themes; Tab through all controls |
| W-10 | Installer upgrade while Frame Server holds the DLL | `restartreplace` should defer the replacement to reboot | Install a newer build over an older one during a call |
| K-11 | `SM_CONVERTIBLESLATEMODE` reads 0 ("slate") on non-convertible machines (seen on the CI VM) | Windows reports slate when the hardware doesn't report convertible support | Posture is informational only (diagnostics); rotation never uses it |
| K-10 | `dmDisplayOrientation` (DMDO_90/180/270) mapped to 90/180/270 clockwise for "follow display rotation" | Direction unverified | Advanced mode with the desktop rotated each way |

## Simulator limitations

- Windows' SimpleOrientation and auto-rotate thresholds are approximations (±60° band, Faceup below 35%
  in-plane gravity). Real values are undocumented.
- The simulator does not model the Frame Server, Media Foundation, the virtual camera, ACLs, session
  lock/suspend, or app behaviour (Zoom/Teams/Discord/browsers).
- Pose transitions interpolate angles linearly with easing. Real folding paths differ, but the tracker only
  sees the readings, so this matters only for transition timing.
- CPU numbers from the simulator say nothing about 1080p30 cost on the laptop.

## Environment

- The cloud dev container has no KVM, so no Windows VM can run here (DECISIONS D-001). The .NET SDK is installed
  per container by `scripts/bootstrap-dotnet.sh` and is lost when the container is recycled.
