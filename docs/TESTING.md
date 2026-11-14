# Testing

## Automated (runs anywhere)

```bash
dotnet test
```

- `tests/CamTurn.Core.Tests`: roll math, quadrant snapping, hysteresis/hold/flat tracker, rotation mapping
  and flip, frame rotation, NV12 round trip, base+hinge fallback.
- `tests/CamTurn.Simulator.Tests`: device kinematics for every pose preset, sensor timing/noise/determinism,
  Windows auto-rotate model, camera states, the upright detector, and every scenario in `sim/scenarios/`.

## Not covered yet (needs Windows 11 / the real laptop)

The media source, Frame Server, virtual camera registration, ACLs, WinRT sensors, app compatibility,
installer, and CPU measurements are not built yet (stages 2–5). Their manual test plan will be added here.
Until then, the first on-device task is to settle the simulator's assumptions; see
[KNOWN_ISSUES.md](KNOWN_ISSUES.md) for the exact checks.
