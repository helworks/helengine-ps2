# Helengine PS2 Host

This repository contains the native PlayStation 2 host scaffold for Helengine.

## Current milestone

- Docker-only build using the official precompiled `ps2dev` release toolchain
- Native PS2 ELF output
- PCSX2 boot check with a solid black screen

## Build

```bash
docker build -t helengine-ps2 .
docker run --rm -v "$PWD":/workspace -w /workspace helengine-ps2 make
```

The first `docker build` downloads and unpacks the official `ps2dev` release toolchain into the image.

The build emits `build/helengine_ps2.elf`.

## Boot check

Load `build/helengine_ps2.elf` in PCSX2. The expected result for this milestone is a solid black frame.
