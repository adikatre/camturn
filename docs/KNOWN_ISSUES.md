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

## Not yet run anywhere (highest risk first)

| # | Risk | Why it might fail | First check |
|---|---|---|---|
| W-1 | MSVC build of `camturn_mediasource.dll` / `camturn_native.dll` | Written without a Windows compiler. Header or WRL details (e.g. `IKsControl` declarations, `ChainInterfaces`) may need small fixes | CI `windows` job |
| W-2 | Frame Server loads the media source and Windows Camera shows the placeholder | COM registration, activator attributes, sensor-profile filter string, or required stream attributes may differ from what Frame Server expects | TESTING M1 with CamTurn not running |
| W-3 | Media source in Frame Server (LocalService) can open the user's pipe | Pipe DACL grants LocalService. If Frame Server activates the source inside an AppContainer client process, the ACL won't admit it and the camera shows only the placeholder | TESTING M1 with CamTurn running; `--pipe-probe` works but the Camera app shows the placeholder → this issue |
| W-4 | `MFCreateVirtualCamera(System, AllUsers)` from the installer | Needs admin (we have it) and the privacy setting to allow camera access. Windows appends "Windows Virtual Camera" to the name | Installer log; `--diagnose` "visible to apps" |
| W-5 | MediaCapture with an exclusive NV12 frame reader on the Realtek webcam | Format negotiation, MJPG decode path, busy/privacy HRESULT mapping | TESTING L6, L7; `--list-cameras` |
| W-6 | Teams and other apps: the remote side gets the picture | One reference virtual camera sample reports Teams showing a local preview but not sending to the far end | TESTING R2 |
| W-7 | Some apps cache camera lists or don't list virtual cameras | App-specific | TESTING R1–R6 |
| W-8 | Frame Server restart during a call | The media source is recreated, and the pipe client reconnects on Start; the host app's behaviour is unknown | TESTING M4 |
| W-9 | WPF UI layout and Fluent theme in light/dark, keyboard navigation | Never rendered | Open settings in both themes; Tab through all controls |
| W-10 | Installer upgrade while Frame Server holds the DLL | `restartreplace` should defer the replacement to reboot | Install a newer build over an older one during a call |
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
