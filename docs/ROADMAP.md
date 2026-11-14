# Roadmap

## Next (needs hardware)

- Run the manual test plan ([TESTING.md](TESTING.md)) on the Dell Inspiron 16 7640 2-in-1. Fill in the
  supported-apps table and the measured CPU use, and close the open items in [KNOWN_ISSUES.md](KNOWN_ISSUES.md).
- Run `scripts/probe-device.ps1 -Poses` to confirm the lid sensor's X-axis sign and camera formats. Update the
  simulator profile from the results.
- Code signing for the installer and binaries (SignPath, Azure Trusted Signing or an OV certificate). It plugs into
  `build/build-windows.ps1` after publish and after `iscc`.

## Later

- **Wrap the camera inside Frame Server (Windows 11 22621+).** The media source could open the physical camera
  itself (`MF_VIRTUALCAMERA_ASSOCIATED_CAMERA_SOURCES`) and rotate frames in the service, with the app sending
  only the orientation. That would remove the pipe copy and keep the camera inside the Windows camera stack
  (DECISIONS D-010).
- GPU path (Direct3D 11 / Video Processor MFT) if CPU use at 1080p30 proves too high on low-end hardware.
- Per-user pipe names so fast user switching works for several users at once.
- More languages for the UI.

## Optional: Windows 10 backend

Windows 10 has no `MFCreateVirtualCamera`. A separate, optional backend could expose a **DirectShow** virtual
camera instead. That only reaches DirectShow apps (most desktop apps and Chromium-based browsers), not
Media Foundation-only apps such as the Windows Camera app. Candidates:

| Project | License (checked 2026-11-01) | Notes |
|---|---|---|
| [tshino/softcam](https://github.com/tshino/softcam) | MIT | DirectShow source filter library with a simple frame-push API. MIT-compatible; preferred. |
| [webcamoid/akvirtualcamera](https://github.com/webcamoid/akvirtualcamera) | GPL-3.0 | Full virtual camera (DirectShow and macOS). GPL-3.0 can't be bundled into MIT CamTurn; it could only be an optional, separately installed backend. |

Neither requires any third-party streaming software. CamTurn's pipeline (`CamTurnEngine` + `FramePipeHost`) is
already transport-agnostic, so a softcam backend would replace only the consumer side.
