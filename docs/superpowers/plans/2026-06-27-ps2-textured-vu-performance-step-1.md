# PS2 Textured VU Performance Step 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove textured CPU per-triangle lighting work from the PS2 opaque VU path while keeping the `textured_cube_grid` scene boot target and current textured packet structure intact.

**Architecture:** Keep the current textured submission path and GIF packet layout, but stop deriving per-triangle textured colors from transformed normals on the EE. Use one shared material color for the textured batch so the first step isolates CPU lighting cost without changing batching or VU transport yet.

**Tech Stack:** C++, PS2 gsKit/packet2, city PS2 build pipeline, PCSX2 verification

---

### Task 1: Replace Textured CPU Per-Triangle Lighting

**Files:**
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Verify: `src/platform/ps2/Ps2BootHost.cpp`
- Verify: `C:\dev\helprojs\city\output\ps2\ps2_bootlog.txt`

- [ ] **Step 1: Confirm the startup scene stays pinned to `textured_cube_grid`**

Run: `rtk rg -n "StartupSceneDiagnosticOverrideId|textured_cube_grid" src/platform/ps2/Ps2BootHost.cpp`
Expected: the startup override still points to `textured_cube_grid`

- [ ] **Step 2: Replace textured per-triangle lighting with shared material color**

Change `Ps2VuVifPacketBuilder::AddOpaqueBatch(...)` so the textured path stops calling per-triangle textured lighting helpers and instead uses `GS_SETREG_RGBAQ(material base color)` for textured triangles in the first step.

- [ ] **Step 3: Keep the rest of the textured path unchanged**

Do not change:
- packet structure
- startup scene override
- VIF/VU submit flow
- textured culling state from the current working renderer state

- [ ] **Step 4: Rebuild the PS2 city output**

Run: `rtk dotnet run --project C:\dev\helworks\helengine\helengine.ui\helengine.editor.app\helengine.editor.app.csproj -c Debug -- --project C:\dev\helprojs\city\project.heproj --build ps2 --output C:\dev\helprojs\city\output\ps2`
Expected: `Build completed for platform 'ps2'`

- [ ] **Step 5: Relaunch PCSX2 on the rebuilt ISO**

Launch the fresh `C:\dev\helprojs\city\output\ps2\game.iso` and keep the emulator pointed at `textured_cube_grid`.

- [ ] **Step 6: Check the boot log for the textured VU path**

Run: search the host boot log for `textured-vu submit`
Expected: textured VU submissions still occur for the scene

- [ ] **Step 7: Stop and ask for emulator feedback**

Do not continue to the next optimization step until the user confirms the visual result and perceived performance of this build.
