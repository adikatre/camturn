# Decisions log

Running log of choices that differ from, or fill in, the handoff defaults. Newest last.

## D-001 — No Windows VM in the cloud dev box; use a device digital twin (2026-10-30)

**Context.** The developer asked for a simulator/emulator of the Dell Inspiron 16 7640 2-in-1 in the cloud
dev container. That container is Ubuntu 24.04, 4 vCPU, 15 GB RAM, **without `/dev/kvm` or VT-x/AMD-V**.

**Decision.** Don't attempt a Windows 11 VM. Build a software model of the device (`CamTurn.Simulator` +
`camturn-sim`) that produces what CamTurn consumes: accelerometer/hinge readings, posture, Windows display
rotation, and lid-camera frames. Run it against the real `CamTurn.Core` logic.

**Why.** Without hardware virtualization, QEMU falls back to TCG emulation, which is far too slow for Windows 11.
Windows 11 also needs TPM 2.0/Secure Boot and a license. Even a working VM has no accelerometer or hinge
sensor: QEMU has no HID sensor device that Windows exposes through `Windows.Devices.Sensors`. So the parts
we most need to simulate would be missing anyway.

**Consequences.** Windows-only pieces (media source, Frame Server, MF virtual camera, WinRT sensors,
installer) still need real Windows: a `windows-latest` CI runner for build/unit/registration checks, and
the developer's laptop for sensors and apps. The simulator's device facts are assumptions until checked on hardware (see KNOWN_ISSUES).

## D-002 — Roll formula is `atan2(x, -y)`, not `atan2(x, y)` (2026-10-30)

The handoff sketched `theta = atan2(x, y)`. In Windows' accelerometer convention an upright panel reads
`y = -1 g`, so `atan2(x, y)` returns 180° for "upright". We use `atan2(x, -y)` so that upright = 0° and
the angle increases clockwise as seen by the viewer. This only shifts the angle by a constant and fixes its
sign. The axis signs are still unverified on the Inspiron. The configurable `RotationMapping` and its
"flip direction" calibration exist because they might turn out different.

## D-003 — Base-sensor fallback uses base accelerometer + hinge angle (2026-10-30)

If Windows exposes only a base accelerometer, the lid's gravity vector can be computed exactly from
the base reading and the hinge angle (`HingeKinematics.LidFromBase`). That's better than inferring from
tablet-mode state alone. It relies on an assumed base-frame axis convention (KNOWN_ISSUES K-3) and on a
`HingeAngleSensor` being present. If neither a lid accelerometer nor base + hinge is available, Auto mode is
disabled and only Manual rotation is offered.

## D-004 — Tooling for this stage (2026-10-30)

- .NET 8 SDK pinned via `global.json` (`rollForward: latestFeature`); warnings are errors.
- No third-party runtime dependencies in Core/Simulator. PNG output is a small in-house encoder over
  `System.IO.Compression.ZLibStream`, so there are no extra licenses to track. Tests use xUnit (Apache-2.0) and
  Microsoft.NET.Test.Sdk (MIT).

## Open questions

- Repository name/owner (`adikatre/camturn` is a placeholder) and license. Not decided at this stage.
- WinUI 3 vs WPF for the app (stage 4).
