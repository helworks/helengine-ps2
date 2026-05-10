# Helengine PS2 Host Bootstrap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first Docker-only PS2 host that compiles inside `ps2dev/ps2dev:latest` and boots in PCSX2 by clearing the screen to a solid color.

**Architecture:** The PS2 project stays intentionally small: one Docker image definition, one PS2 SDK build file, and one native entrypoint that initializes gsKit/dmaKit, clears the framebuffer, and loops forever. This keeps the first milestone focused on proving the toolchain and boot path before any generated-core binding is added.

**Tech Stack:** Docker, ps2dev/ps2dev, PS2SDK, gsKit, dmaKit, C++17.

---

### Task 1: Docker build environment

**Files:**
- Create: `Dockerfile`
- Create: `README.md`

- [ ] **Step 1: Define the Docker image**

```dockerfile
FROM ps2dev/ps2dev:latest

WORKDIR /workspace
CMD ["/bin/bash"]
```

- [ ] **Step 2: Document the image usage**

```markdown
docker build -t helengine-ps2 .
docker run --rm -it -v "$PWD":/workspace -w /workspace helengine-ps2
```

- [ ] **Step 3: Keep the docs focused on the first milestone**

Document only the Docker build, the PS2 ELF output, and the PCSX2 boot check for the black-screen test.

### Task 2: Native PS2 bootstrap

**Files:**
- Create: `Makefile`
- Create: `src/main.cpp`
- Create: `src/platform/ps2/Ps2BootHost.hpp`
- Create: `src/platform/ps2/Ps2BootHost.cpp`

- [ ] **Step 1: Define the PS2 build target**

Use `mips64r5900el-ps2-elf-g++`, the PS2SDK startup linkfile, and the gsKit/dmaKit include and library paths from the Docker image.

- [ ] **Step 2: Implement the boot entrypoint**

Initialize gsKit, set a standard NTSC framebuffer, clear the screen to black with `gsKit_clear`, and loop with `gsKit_sync_flip` and `gsKit_queue_exec`.

- [ ] **Step 3: Keep the host boundary clean**

Put the PS2-specific setup and frame presentation logic in `Ps2BootHost`, leaving `main.cpp` as a small launcher.

- [ ] **Step 4: Build the ELF**

Run `make` inside the Docker container and confirm the output ELF is emitted under `build/`.

### Task 3: First verification pass

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add the exact build command**

Document the exact Docker command that builds the ELF from the mounted workspace.

- [ ] **Step 2: Add the exact emulator check**

Document that the resulting ELF should boot in PCSX2 and display a solid black frame.
