# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Windows 11 virtual camera "CamTurn Camera": a Frame Server custom media source (C++/WRL) fed by the tray app
  over an ACL-protected named pipe, with a placeholder frame when the app isn't sending.
- Automatic rotation from the raw accelerometer, with hysteresis, a hold time, flat-device holding, a configurable
  mapping table with "flip direction" calibration, manual rotation, and an advanced "follow display rotation" mode.
- Fixed-size output (1280×720 or 1920×1080, NV12/YUY2) with fit (letterbox) or fill (crop) for portrait, and mirroring.
- Physical camera opened only while an app uses CamTurn Camera (3 s grace period). Run modes: automatic, always on,
  manual. Camera released on lock and sleep.
- Tray app with status icons, settings window with live preview and status panel, first-run wizard.
- CLI: `--diagnose`, `--list-cameras`, `--sensor-dump`, `--register`, `--unregister`, `--pipe-probe`,
  `--set-autostart`, `--test-pattern`, `--simulate-orientation`, `--simulate-device`, `--log-level`, `--minimized`.
- Inno Setup installer and clean uninstaller; CI for Linux and Windows; draft releases on tags.
- Device simulator (`camturn-sim`) with a Dell Inspiron 16 7640 2-in-1 profile built from a real hardware probe.
- Read-only hardware probe script `scripts/probe-device.ps1`.
