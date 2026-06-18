# PS2 Docker Build

Use the low-level native Docker flow when you need to build the host directly instead of using the shared editor wrapper.

```bash
docker build -t helengine-ps2 .
docker run --rm -v "$PWD":/workspace -w /workspace helengine-ps2 make
```

The build emits:

- `build/helengine_ps2.elf` inside the repository as the intermediate native executable
- `game.iso` in the requested export root
- `disc/` in the requested export root for inspection

Boot `game.iso` in PCSX2 when you need the manual native proof path.
