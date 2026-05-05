# Helengine PS2 Host

This repository contains the native PlayStation 2 host scaffold for Helengine.

## Current milestone

- Docker-only build using the official precompiled `ps2dev` release toolchain
- Native PS2 ELF output
- PCSX2 boot check with a solid black screen

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

The build emits `build/helengine_ps2.elf`.

## Boot check

Load `build/helengine_ps2.elf` in PCSX2. The expected result for this milestone is a solid black frame.
