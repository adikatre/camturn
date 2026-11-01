## Summary

<!-- What does this change and why? Link issues with "Fixes #123". -->

## Testing

<!-- What you ran. Say which parts ran on Windows hardware and which only in CI / the simulator. -->
- [ ] `dotnet test` (Core + Simulator tests)
- [ ] `dotnet run --project src/CamTurn.Sim.Cli -- --run-all`
- [ ] Windows build / manual checks from docs/TESTING.md (list which)

## Checklist

- [ ] No network calls, telemetry, or new third-party runtime dependencies (or THIRD_PARTY_NOTICES.md updated)
- [ ] docs/DECISIONS.md updated for design changes; docs/KNOWN_ISSUES.md for anything unverified
- [ ] CHANGELOG.md updated under "Unreleased"
