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

- .NET SDK pinned via `global.json` (`rollForward: latestFeature`); warnings are errors. (Retargeted to .NET 10 by D-005.)
- No third-party runtime dependencies in Core/Simulator. PNG output is a small in-house encoder over
  `System.IO.Compression.ZLibStream`, so there are no extra licenses to track. Tests use xUnit (Apache-2.0) and
  Microsoft.NET.Test.Sdk (MIT).

## D-005 — .NET 10 LTS instead of .NET 8 (2026-11-01)

The handoff defaulted to .NET 8, but .NET 8 support ends on 2026-11-10. A new app shouldn't ship on a runtime
that stops getting security fixes the week it's built. All projects target `net10.0` (LTS, supported to
November 2028) and `net10.0-windows10.0.22621.0` for Windows code. Package versions are managed centrally in
`Directory.Packages.props`.

## D-006 — `ReadingTransform`: there is no `DisplayOrientations.Native` (2026-11-01)

The handoff asked for `ReadingTransform = DisplayOrientations.Native`. No such value exists. Per the WinRT docs,
`ReadingTransform` aligns readings with the display orientation you pass, relative to the panel's native
orientation. CamTurn therefore sets it to the panel's native orientation (`Landscape` on every convertible
laptop seen so far), so readings stay in the panel frame however the desktop is rotated. `probe-device.ps1`
records readings with the default and with `Landscape`, plus a step with the desktop manually rotated, to
confirm this on the Inspiron (KNOWN_ISSUES K-4).

## D-007 — Frame transport is a named pipe, not a `Global\` file mapping (2026-11-01)

The media source runs inside Windows Frame Server, as LocalService in session 0 (confirmed in Microsoft's
Frame Server custom media source docs). The tray app runs in the user's session. A user process can't create a
`Global\` file mapping without `SeCreateGlobalPrivilege`, and `Local\` names are per-session, so the shared-memory
design in the handoff would fail on a standard account. Named pipes aren't session-scoped. So the app hosts
`\\.\pipe\CamTurn.Video.v1` with a DACL for the current user, LocalService and SYSTEM, and rejects remote
clients. The media source connects as a client only while a stream is running. That connection is the
"consumer started/stopped" signal, so no separate named events are needed. Every message carries the
versioned 64-byte header (`protocol.h` / `FrameProtocol.cs`, pinned by the shared golden file
`tests/fixtures/frame-header-v1.bin`). Throughput is about 41 MB/s at 720p30 NV12 and 93 MB/s at 1080p30. Microsoft's own
Windows-Camera VirtualCamera sample also uses a named pipe between its media source and its tray process.
Limitation: one pipe name per machine, so with fast user switching only one user's CamTurn serves frames.

## D-008 — Virtual camera: System lifetime, all users, created by the installer (2026-11-01)

`MFCreateVirtualCamera(SoftwareCameraSource, System, AllUsers, ...)` requires administrator rights (per the API docs),
so the installer creates it during its single UAC prompt and the uninstaller removes it. The app never needs admin
rights. The media source CLSID must be registered under HKLM, because Frame Server doesn't read per-user classes.
Windows appends "Windows Virtual Camera" to the friendly name we pass ("CamTurn Camera"), so apps may show a
longer name.

## D-009 — `camturn_native.dll` wraps the virtual-camera API (2026-11-01)

`IMFVirtualCamera` inherits `IMFAttributes`. Declaring its vtable correctly in C# is error-prone and untestable here.
A ~100-line C++ DLL exports `CamTurn_IsVirtualCameraSupported`, `CamTurn_RegisterVirtualCamera` and
`CamTurn_UnregisterVirtualCamera` for the app to P/Invoke.

## D-010 — Media source design choices (2026-11-01)

- Written from scratch with WRL (part of the Windows SDK) to avoid WIL/C++/WinRT NuGet dependencies. Structure follows
  Microsoft's Frame Server custom media source documentation, with `smourier/VCamSample` (MIT) as a reference.
- System-memory samples only (`MFCreate2DMediaBuffer`). `SetD3DManager` is accepted and ignored, as the docs allow.
  Converting 720p/1080p NV12 on the CPU is cheap.
- Formats: NV12 and YUY2 at 1280x720@30 and 1920x1080@30. YUY2 is converted from NV12 in the source.
- If no fresh frame has arrived within 1 s, the source serves a static "CamTurn Camera — waiting" placeholder. It
  never blocks `RequestSample` and never returns an error to the host app because the CamTurn app is missing.
- Static CRT, so no VC++ redistributable is needed in the Frame Server process.
- Alternative not taken (yet): Windows 11 22621+ lets a virtual camera wrap the physical camera inside Frame Server
  (`MF_VIRTUALCAMERA_ASSOCIATED_CAMERA_SOURCES`). The media source could then rotate frames itself, and the app would
  only send the orientation. That design is simpler and more private, but harder to build and debug without
  hardware. Listed in ROADMAP.

## Open questions

- Repository name/owner (`adikatre/camturn` is a placeholder) and license. Not decided at this stage.
- WinUI 3 vs WPF for the app (stage 4).
