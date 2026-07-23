# Splash Solid Background Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the unimportable black PPM from the DemoDisc splash cook path by authoring its backdrop as a solid 2D primitive.

**Architecture:** The generated splash scene will use `RoundedRectComponent` for its full-canvas black backdrop, matching the existing menu panel and button rendering route. The logo remains a file-backed `SpriteComponent`, preserving its current serialization and animation behavior.

**Tech Stack:** C#, Helengine scene authoring, xUnit source tests, PS2 platform cooker.

---

### Task 1: Cover the generated background contract

**Files:**
- Modify: `C:/dev/helprojs/demodisc/assets/codebase/menu.tools.tests/HelenOfCodeSplashSceneSourceTests.cs`
- Modify: `C:/dev/helprojs/demodisc/assets/codebase/menu.tools/HelenOfCodeSplashSceneFactory.cs`

- [ ] **Step 1: Write the failing test**

Add assertions that the factory creates a `RoundedRectComponent` for `HelenOfCodeSplashBackground`, and that the source no longer references `black.ppm` or `BackgroundTexturePath`.

- [ ] **Step 2: Run the source test to verify it fails**

Run: `dotnet test C:/dev/helprojs/demodisc/assets/codebase/menu.tools.tests/menu.tools.tests.csproj --filter FullyQualifiedName~HelenOfCodeSplashSceneSourceTests`

Expected: FAIL because the factory currently creates a `SpriteComponent` and persists `images/splash/black.ppm`.

- [ ] **Step 3: Write minimal implementation**

Remove `BackgroundTexturePath`. In `CreateBackgroundEntity`, attach a `RoundedRectComponent` configured with the splash canvas size, zero radius, zero border thickness, opaque black fill, opaque black border, and the existing background draw order. Do not create a texture asset reference for that entity.

- [ ] **Step 4: Run the source test to verify it passes**

Run: `dotnet test C:/dev/helprojs/demodisc/assets/codebase/menu.tools.tests/menu.tools.tests.csproj --filter FullyQualifiedName~HelenOfCodeSplashSceneSourceTests`

Expected: PASS.

### Task 2: Validate the PS2 cook reaches native packaging

**Files:**
- Verify: `C:/dev/helprojs/demodisc/ps2-build-level01-tessellation-slicing/game.iso`

- [ ] **Step 1: Run the full PS2 DemoDisc build**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File C:/dev/helworks/helengine/scripts/build-platform.ps1 -Project C:/dev/helprojs/demodisc/project.heproj -Platform ps2 -Output C:/dev/helprojs/demodisc/ps2-build-level01-tessellation-slicing`

Expected: the black PPM does not appear in texture cook work items and `game.iso` is written.

- [ ] **Step 2: Launch the newly produced ISO in PCSX2**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File C:/dev/helworks/helengine-ps2/scripts/launch_in_emulator.ps1 -ArtifactPath C:/dev/helprojs/demodisc/ps2-build-level01-tessellation-slicing/game.iso`

Expected: PCSX2 starts the fresh full DemoDisc image.
