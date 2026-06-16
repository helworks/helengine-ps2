# Helengine PS2 Host

This repository contains the native PlayStation 2 host scaffold for Helengine.

## Current milestone

- Docker-only build using the official precompiled `ps2dev` release toolchain
- Bootable PS2 ISO output plus an inspectable staged disc root
- PCSX2 boot check through the packaged disc image

## Renderer foundation

The PS2 build now exposes renderer families through platform graphics profiles:

- `ps2-standard-forward`
- `ps2-showcase-forward`

PS2 materials are cooked into `Ps2MaterialAsset` payloads instead of Windows shader-backed material assets. The PS2 runtime resolves those cooked assets through the player-owned render manager, mirrors live 3D drawables into PS2 render proxies, separates static and dynamic opaque work through a frame planner, and executes the first custom unlit/simple-lit opaque draw path on the native host.

The current native opaque path is color-only. It preserves PS2 texture metadata in the cooked material contract, but actual GS texture sampling remains a follow-up slice.

## Build

```bash
docker build -t helengine-ps2 .
docker run --rm -v "$PWD":/workspace -w /workspace helengine-ps2 make
```

The first `docker build` downloads and unpacks the official `ps2dev` release toolchain into the image.

The build emits:

- `build/helengine_ps2.elf` inside the repository as the intermediate native executable
- `game.iso` in the requested export root
- `disc/` in the requested export root for inspection

## Editor CLI build

If your workspace keeps `helengine-ps2`, `helengine`, and `helprojs` as sibling directories, use the shared wrapper like this:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ..\helengine\artifacts\build-platform.ps1 `
  -Project ..\helprojs\city\project.heproj `
  -Platform ps2 `
  -Output ..\helprojs\city\ps2-build
```

That wrapper runs the main editor CLI with `--build ps2` and writes the generated PS2 package to the output directory you provide.

## Boot check

Boot `game.iso` in PCSX2. The expected result for this milestone is that the image is recognized as bootable and enters the Helengine PS2 runtime instead of returning to the BIOS browser.

## Launching in PCSX2

Use the checked-in launcher script:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\launch_ps2_iso_in_pcsx2.ps1 `
  -IsoPath .\tmp\packaged-disc-proof-life\game.iso
```

The launcher requires an explicit `-IsoPath`. Before launch it force-closes any running `pcsx2-qt.exe` processes, recreates an isolated launcher output directory under `tmp\`, verifies the installed PCSX2 executable and the global PCSX2 profile root, and writes the emulator log to `tmp\pcsx2-launcher\pcsx2-emulog.txt`.

The launcher prints:

- the ISO path
- the ISO last write time
- the PCSX2 executable path
- the PCSX2 profile root
- the emulator log file path
- the spawned PCSX2 process id

It then starts PCSX2 with `-fastboot -logfile <log> -- <iso>`.

The script fails fast when:

- `-IsoPath` is missing
- the ISO file is missing
- the PCSX2 executable is missing
- the PCSX2 profile root is missing
