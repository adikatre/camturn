# CamTurn

Auto-rotating webcam for Windows 11 2-in-1 laptops: CamTurn follows the device's accelerometer, rotates the
built-in camera's frames so they stay upright in tent/tablet/stand modes, and publishes them as a virtual
camera ("CamTurn Camera") that Zoom, Teams, Meet, Discord and the Windows Camera app can select.
It uses only the native Windows 11 virtual camera API, with no third-party streaming software.

> **Status: early development.** What exists today is the platform-independent core logic and a
> device simulator. The Windows app, virtual camera and installer are not built yet.

- [docs/SIMULATOR.md](docs/SIMULATOR.md): the device simulator (Dell Inspiron 16 7640 2-in-1 profile)
- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md): build and test
- [docs/DECISIONS.md](docs/DECISIONS.md): design decisions log
- [docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md): unverified assumptions and how to check them
