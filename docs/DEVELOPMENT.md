# Development

## What builds where

| Part | Linux / macOS | Windows 11 |
|---|---|---|
| `CamTurn.Core`, `CamTurn.Simulator`, `camturn-sim`, their tests | yes | yes |
| Windows platform layer, app, media source, installer (later stages) | no | yes |

## Prerequisites

- .NET 8 SDK (8.0.400 or newer feature band; see `global.json`).
- Windows-only stages will additionally need Visual Studio 2022 with the C++ desktop and .NET desktop
  workloads and the Windows 11 SDK (documented when those stages land).

On a fresh Linux container (e.g. the cloud dev box), install the SDK into `~/.dotnet`:

```bash
scripts/bootstrap-dotnet.sh
```

```bash
export PATH="$HOME/.dotnet:$PATH"
```

## Build and test

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project src/CamTurn.Sim.Cli -- --run-all
```

See [SIMULATOR.md](SIMULATOR.md) for the simulator's commands.
