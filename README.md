# Helengine PS2 Host

This repository contains the PS2 platform host and builder integration for Helengine.

## Build

```powershell
dotnet run --project ..\helengine\tools\build-waiter\helengine.buildwaiter.csproj -- `
  --output ..\helprojs\city\ps2-build `
  --require game.iso `
  --require disc/SYSTEM.CNF `
  --require disc/HELENGIN.ELF `
  -- powershell -NoProfile -ExecutionPolicy Bypass -File ..\helengine\scripts\build-platform.ps1 `
  -Project ..\helprojs\city\project.heproj `
  -Platform ps2 `
  -Output ..\helprojs\city\ps2-build
```

The Build Waiter returns successfully only after the fresh, non-empty `game.iso`, `disc/SYSTEM.CNF`, and `disc/HELENGIN.ELF` disc boot contract is present.

## Run In Emulator

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\launch_in_emulator.ps1 `
  -ArtifactPath ..\helprojs\city\ps2-build\game.iso
```

## More Docs

- [Docker Build Notes](docs/Docker.md)
