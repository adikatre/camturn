# Contributing to CamTurn

Thanks for helping! CamTurn is small. The most useful contributions are:

- **Testing on your convertible**: run the [manual test plan](docs/TESTING.md) and report results (with
  `--diagnose` output). Each device adds one row to the supported-device list.
- **Device profiles** for the simulator (`sim/devices/*.json`), built from `scripts/probe-device.ps1` output.
- Bug fixes and focused improvements.

## Ground rules

- **Local only**: no network calls, telemetry, accounts or auto-update pings.
- **No third-party streaming software.** CamTurn uses only the Windows 11 virtual camera API.
- Keep the media source (`src/CamTurn.MediaSource`) minimal: it runs inside a Windows service in every app's
  camera pipeline.
- Put logic in `CamTurn.Core` (pure, OS-free, tested on Linux). Keep Windows calls in `CamTurn.Platform.Windows`.
- Record design changes in `docs/DECISIONS.md`. Record anything you couldn't verify in `docs/KNOWN_ISSUES.md`.

## Workflow

1. Open an issue first for anything larger than a small fix.
2. Fork, branch from `main`, and make your change with tests.
3. Run locally (details in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)):
   ```bash
   dotnet format CamTurn.sln --verify-no-changes
   ```
   ```bash
   dotnet test tests/CamTurn.Core.Tests
   ```
   ```bash
   dotnet test tests/CamTurn.Simulator.Tests
   ```
   On Windows, also run `./build/build-windows.ps1 -SkipInstaller`.
4. Add a line to `CHANGELOG.md` under **Unreleased**.
5. Open a pull request using the template. Say clearly what you ran on real hardware.

## Style

- C#: `.editorconfig`/`dotnet format` defaults, nullable enabled, warnings are errors.
- C++: `.clang-format` (Microsoft-based), C++20, no exceptions across COM boundaries.
- Comments explain *why*. Prefer small, testable functions.

By contributing you agree that your contributions are licensed under the MIT License, and you agree to follow the
[Code of Conduct](CODE_OF_CONDUCT.md).
