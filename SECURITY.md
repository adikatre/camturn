# Security policy

## Supported versions

Only the latest release receives fixes.

## Reporting a vulnerability

Please **do not** open a public issue. Report privately through
[GitHub private vulnerability reporting](https://github.com/adikatre/camturn/security/advisories/new). Include
the affected version, your Windows build, and steps to reproduce. You can expect an acknowledgement within a
week.

## Security-relevant design

- The tray app never runs elevated. Administrator rights are used only by the installer, and by the optional
  "Register virtual camera" repair, which runs `CamTurn.exe --register` through a UAC prompt.
- Frames travel over the named pipe `\\.\pipe\CamTurn.Video.v1`. Its ACL allows only the signed-in user, LocalService
  (the Windows Frame Server account) and SYSTEM, and explicitly denies network logons. Every message is length-
  and version-checked on both sides before any payload is read (`FrameProtocol` / `protocol.h`).
- The media source DLL runs inside Windows Frame Server. It only reads from the pipe, never writes files or
  registry keys, and serves a placeholder frame on any error.
- No network access, no telemetry, no update checks.
- Installers are not yet code-signed. Verify downloads against the published SHA-256 checksums.
