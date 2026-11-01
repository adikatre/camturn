# Device simulator (`camturn-sim`)

A software model ("digital twin") of a 2-in-1 convertible, starting with the developer's
**Dell Inspiron 16 7640 2-in-1**. It lets the orientation and camera-rotation logic in `CamTurn.Core` be
developed and tested on any OS, including Linux CI, without the laptop.

It is **not** a Windows emulator. It does not run Zoom, Teams, the Frame Server or the virtual camera. Those
need real Windows 11 (see [TESTING.md](TESTING.md)). The simulator covers the inputs CamTurn reads and
the frames it receives.

## What it models

| Part | Model | Code |
|---|---|---|
| Physical pose | Lid roll (clockwise as seen by the viewer), lid tilt, hinge angle 0–360°, optional hand shake; smooth transitions | `DevicePose`, `SimDevice` |
| Lid accelerometer | Gravity in the panel's native frame (Windows convention), Gaussian noise, 16 ms report interval, optional axis-mismatch faults (`mirrorX`, `axisRotationDegrees`). The real Inspiron exposes exactly one accelerometer, in the lid (probe 2026-11-01). | `SimAccelerometer` |
| Base accelerometer | Same, in the keyboard frame. Not present on the real Inspiron; used by the `-base-only` profile. | `SimAccelerometer` |
| Hinge angle sensor | Hinge angle ± 0.5° noise. Not exposed on the real Inspiron. | `SimHingeAngleSensor` |
| SimpleOrientationSensor | Quadrant with hysteresis + Faceup/Facedown (diagnostic cross-check only) | `SimSimpleOrientationSensor` |
| Windows shell | Laptop/slate posture from hinge angle; desktop auto-rotate only in slate; rotation lock | `SimPosture`, `SimDisplay` |
| Lid webcam | Lid-fixed camera: when the device rolls, the scene appears rotated by the same amount. Advertised formats, NV12 output, busy/disconnect faults, "LED on" state | `SimLidCamera`, `SceneRenderer` |
| Upright check | Measures the rotation and mirroring of a scene frame, so tests can assert "output is upright" | `UprightDetector` |
| Scenarios | JSON timelines of poses, actions, and expectations, run through the real `OrientationTracker` + `RotationMapping` | `Scenario`, `ScenarioRunner` |

Device profiles live in `sim/devices/`, scenarios in `sim/scenarios/`.

## Conventions

- **World**: +X to the viewer's right, +Y away from the viewer, +Z up.
- **Lid sensor (Windows native frame)**: +X toward the panel's right edge, +Y toward its top edge, +Z out of the
  screen. The reading is the direction of gravity in g: upright → `(0, -1, 0)`, face up → `(0, 0, -1)`.
- **Roll** = `atan2(x, -y)`, clockwise as seen by the viewer: upright 0, right edge down 90, upside down 180,
  left edge down 270. (See DECISIONS D-002 for why this isn't `atan2(x, y)`.)
- **Base sensor (assumed)**: +X right of keyboard, +Y from the front edge toward the hinge, +Z out of the keyboard.
- **Hinge**: 0 closed, 90 lid upright, 180 flat, 360 tablet.
- **Camera**: frames are as the camera sees them (not mirrored). A lid rolled by θ clockwise shows the
  scene rotated θ clockwise, so CamTurn rotates frames by `RotationMapping` (default: `360 − quadrant`).

## Pose presets

| Preset | Roll | Tilt | Hinge | Expected quadrant |
|---|---|---|---|---|
| `laptop` | 0 | 20 | 110 | 0 |
| `laptop-flat` | 0 | 90 | 180 | holding |
| `tent` | 180 | 30 | 300 | 180 |
| `stand` | 0 | 15 | 285 | 0 |
| `tablet` | 0 | 30 | 360 | 0 |
| `tablet-portrait-right` | 90 | 30 | 360 | 90 |
| `tablet-inverted` | 180 | 30 | 360 | 180 |
| `tablet-portrait-left` | 270 | 30 | 360 | 270 |
| `flat-face-up` / `flat-face-down` | 0 | ±90 | 360 | holding |

## Usage

```bash
scripts/bootstrap-dotnet.sh          # once per fresh container
dotnet build
alias camturn-sim='dotnet src/CamTurn.Sim.Cli/bin/Debug/net10.0/camturn-sim.dll'

camturn-sim --diagnose --pose tent             # what CamTurn's --diagnose would report
camturn-sim --sensor-dump --pose tablet-portrait-right --duration 2
camturn-sim --sensor-dump --roll 50 --tilt 30 --hinge 360 --shake 5 --all-sensors
camturn-sim --snapshot out/snap --pose tent    # raw.png + corrected.png
camturn-sim --scenario tent-flip --render out/tent --csv out/tent.csv
camturn-sim --run-all                          # every scenario, PASS/FAIL
camturn-sim --scenario tablet-spin --flip-direction   # what a wrong mapping looks like
```

`--sensor-dump` and `--diagnose` print the same fields the real app's flags will print. On the laptop,
you can compare the two outputs line by line.

## Scenarios shipped

| Scenario | Checks |
|---|---|
| `tent-flip` | Laptop → tent: upright output within 1 s (acceptance criterion 2) |
| `tablet-spin` | All four orientations; Windows display rotation follows |
| `flat-hold` | Flat on a table: hold the last quadrant, report "holding" |
| `small-tilts-no-flip` | Jitter and 40–55° leans never flip the image |
| `rotation-locked` | Windows rotation lock on: CamTurn still corrects from the raw sensor |
| `camera-unplug` | Disconnect, reconnect, busy-in-another-app states |
| `base-sensor-only` | Only a base accelerometer: fall back to base + hinge angle |

Add a scenario by dropping a JSON file in `sim/scenarios/`. `ScenarioPasses` in the test suite picks it up
automatically. Fields: `steps[] {at, pose | roll/tilt/hinge/shake, transition, action}` and
`expect[] {at, quadrant, holding, autoAvailable, displayRotation, posture, outputUpright, cameraState}`.

## Updating the profile from the real laptop

Every unverified fact is listed under `assumptions` in the profile and in [KNOWN_ISSUES.md](KNOWN_ISSUES.md).
Once the real app's `--diagnose` and `--sensor-dump` exist, run them on the laptop in each preset pose and
edit the profile to match. If the real sensor's X axis turns out to be inverted, set `mirrorX: true` there.
The simulator then reproduces the bug and the "flip direction" calibration fixes it. A test already covers that
path: `MirroredSensorAxisIsFixedByFlipDirection`.

## What it cannot tell you

- Real sensor axis signs, mounting, noise, and which sensors Windows exposes (all assumed).
- Windows' exact SimpleOrientation and auto-rotate thresholds (undocumented; approximated).
- Anything about Media Foundation, the Frame Server, virtual camera registration, ACLs, or app compatibility.
- CPU cost on the real machine (rendering here is a procedural test scene, not a camera pipeline).
