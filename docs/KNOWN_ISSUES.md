# Known issues and unverified assumptions

Everything here was **not** verified on real hardware. Each item says how to check it.

## Device facts assumed by the simulator profile (`sim/devices/dell-inspiron-16-7640-2in1.json`)

| # | Assumption | How to verify on the laptop |
|---|---|---|
| K-1 | Model is a convertible "Inspiron 16 7640 2-in-1". The *Inspiron 16 Plus 7640* is a non-convertible clamshell. | Settings > System > About; `camturn --diagnose` |
| K-2 | WinRT exposes **two** accelerometers (lid = default, base) and a hinge-angle sensor. Could be lid only, base only, or fused. | `camturn --diagnose` sensor list (from `DeviceInformation.FindAllAsync(Accelerometer.GetDeviceSelector())`) |
| K-3 | Lid accelerometer uses the documented Windows axes (+X right, +Y up, +Z out of screen; upright reads y ≈ -1). Base sensor: +Y toward hinge, +Z out of keyboard. | `camturn --sensor-dump` in each preset pose (laptop, tent, tablet portrait right/left); compare with `camturn-sim --sensor-dump --pose <same>` |
| K-4 | `ReadingTransform = DisplayOrientations.Native` really yields un-rotated readings when the desktop is rotated. | Rotate the desktop manually (Settings > Display > Orientation) while still; readings must not change |
| K-5 | Webcam is mounted unrotated and delivers unmirrored frames. | Windows Camera app in laptop mode |
| K-6 | Webcam formats (MJPG 1080p30/720p30, NV12 720p30, YUY2 480p30). | `camturn --list-cameras` |
| K-7 | Panel 1920x1200 native landscape. | Settings > System > Display |
| K-8 | Windows only auto-rotates in slate posture; slate above ~190° hinge. | Fold slowly from laptop to tent with rotation lock off |
| K-9 | Accelerometer noise ~0.02 g/axis, 16 ms interval. | `camturn --sensor-dump` with the laptop still on a desk |

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
