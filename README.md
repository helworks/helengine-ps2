# Helengine PS2 Host

This repository contains the PS2 platform host and builder integration for Helengine.

## Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ..\helengine\artifacts\build-platform.ps1 `
  -Project ..\helprojs\city\project.heproj `
  -Platform ps2 `
  -Output ..\helprojs\city\ps2-build
```

## Run In Emulator

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\launch_in_emulator.ps1 `
  -ArtifactPath ..\helprojs\city\ps2-build\game.iso
```

## More Docs

- [Docker Build Notes](docs/Docker.md)
