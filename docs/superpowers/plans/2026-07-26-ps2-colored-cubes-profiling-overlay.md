# PS2 Colored Cubes Profiling Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the temporary Light UI with a stable PS2 profiling overlay that attributes the Colored Cubes CPU encode cost to measured renderer phases.

**Architecture:** The PS2 boot host already accumulates and publishes four platform-owned rows. The shared FPS component already owns dynamic additional-row text components; change its compatibility merge so platform-owned detail and additional values flow into that existing visible path. The PS2 boot host composes granular timing rows, while the generated DemoDisc Colored Cubes scene removes only its temporary Light indicator.

**Tech Stack:** C++17 PS2 native host and renderer, C# source-contract tests, DemoDisc C# scene generator, .NET test runner.

---

### Task 1: Make all platform-owned FPS rows visible

**Files:**
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\FPSComponentTests.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.core\components\2d\FPSComponent.cs`

- [ ] **Step 1: Write the failing source-contract test**

Update the existing platform-overlay runtime test to call `Core.SetPerformanceOverlayTextRows` with four non-empty rows, attach an `FPSComponent`, and assert that the overlay owns the update/render rows plus one dynamic row for detail and one for the additional block. Add a companion test that clears the detail/additional rows and asserts the dynamic rows are removed.

- [ ] **Step 2: Run the targeted test and verify it fails**

Run:

```powershell
rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --no-restore --filter FullyQualifiedName~FPSComponentTests
```

Expected: FAIL because the current compatibility merge assigns empty detail/additional text and therefore creates no dynamic rows.

- [ ] **Step 3: Implement the minimal host-only profiling presentation**

In `FPSComponent.cs`, retain the existing dynamic-row hierarchy. Set `DetailFpsText` from `Core.PerformanceOverlayDetailText` when platform-owned rows are active, and merge it with `Core.PerformanceOverlayAdditionalText` and authored `AdditionalText` before `SynchronizeAdditionalLineRows`. Preserve the existing compact two-row behavior when no detail, additional, or authored text exists.

- [ ] **Step 4: Run the targeted test and verify it passes**

Run the command from Step 2.

Expected: PASS.

### Task 2: Publish granular PS2 profiling rows

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2BootHostSourceTests.cs`
- Modify: `C:\dev\helworks\helengine-ps2\src\platform\ps2\Ps2BootHost.cpp`

- [ ] **Step 1: Write the failing generator source-contract test**

Add a source-contract test that requires the four PS2 overlay rows to include `Set`, `Prep`, `Emit`, `Asm`, `Enc`, `Sub`, `Gif`, `Tri`, `Bat`, and `Bytes`.

- [ ] **Step 2: Run the focused DemoDisc test and verify it fails**

rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --no-restore --filter FullyQualifiedName~Ps2BootHostSourceTests

Expected: FAIL because the current visible PS2 strings omit the granular labels.

- [ ] **Step 3: Remove only the profiling-scene indicator attachment**

Keep timing collection unchanged. Compose the four rows as:

```text
Bxx <fps> FPS <frame-ms> ms
3D <ms> Set <ms> Enc <ms> Sub <ms> Gif <ms>
Prep <ms> Emit <ms> Asm <ms>
Tri <count> Bat <count> Bytes <count>
```

Do not infer GPU work from `Enc`; preserve measured zeroes for `Sub` and `Gif`.

- [ ] **Step 4: Run the focused DemoDisc test and verify it passes**

Run the command from Step 2.

Expected: PASS.

### Task 3: Remove the temporary Light indicator from the generated Colored Cubes scaffold

**Files:**
- Modify: `C:\dev\helprojs\demodisc\assets\codebase\rendering.tools\ColoredCubeGridSceneFactory.cs`
- Modify: `C:\dev\helprojs\demodisc\assets\codebase\rendering.tools.tests\FpsFontScaleSourceTests.cs`

- [ ] **Step 1: Write the failing generator source-contract test**

Add a source-contract test proving `ColoredCubeGridSceneFactory` does not attach `DemoDiscLightIndicatorOverlayFactory`, while the FPS component remains attached to the scene.

- [ ] **Step 2: Run the focused DemoDisc test and verify it fails**

Run the smallest project test command that contains `FpsFontScaleSourceTests` and the new source-contract test.

Expected: FAIL because the generator still emits the Light label and swatch.

- [ ] **Step 3: Remove only the profiling-scene indicator attachment**

Delete the Colored Cubes call that creates or attaches the Light label/swatch. Do not edit `DemoDiscLightToggleComponent`, lighting state, or other rendering-scene scaffolds; this is a temporary profiling UI change.

- [ ] **Step 4: Run the focused DemoDisc test and verify it passes**

Run the command from Step 2.

Expected: PASS.

### Task 4: Build and collect the first attribution sample

**Files:**
- No production-file changes.

- [ ] **Step 1: Run both focused source-contract test groups**

Run the PS2 host source tests and the focused DemoDisc generator test group.

- [ ] **Step 2: Build the full PS2 DemoDisc ISO**

Run the repository's normal PS2 full-build command with a new persistent output directory. Wait with the build waiter until the ISO exists and is non-empty.

- [ ] **Step 3: Launch through the project PCSX2 launcher**

Run `scripts\launch_in_emulator.ps1 -ArtifactPath <fresh-game.iso>` from `C:\dev\helworks\helengine-ps2`.

- [ ] **Step 4: Record the four profiling rows**

Use HelenUI OCR only. Do not inspect screenshots manually. Record `Set`, `Prep`, `Emit`, `Asm`, `Enc`, `Sub`, `Gif`, `Tri`, `Bat`, and `Bytes` from the Colored Cubes scene before selecting any renderer optimization.

### Task 5: Commit only profiling-owned changes

**Files:**
- Modify only the files listed in Tasks 1 and 2.

- [ ] **Step 1: Review staged paths and whitespace**

Run:

```powershell
rtk git diff --check
rtk git status --short
```

- [ ] **Step 2: Commit the profiling implementation**

Stage only the verified profiling source, test, and DemoDisc generator files. Do not include unrelated dirty changes from other agents.

```powershell
rtk git commit -m "feat(ps2): add colored cubes profiling overlay"
```
