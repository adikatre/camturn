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
