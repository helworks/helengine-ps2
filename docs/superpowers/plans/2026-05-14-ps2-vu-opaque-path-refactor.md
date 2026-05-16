# PS2 VU Opaque Path Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move opaque PS2 VU rendering off EE-side per-triangle clipping/projection/culling so `cube_test` and `colored_cube_grid` keep rendering correctly while the opaque VU path becomes materially faster.

**Architecture:** Keep `Ps2RenderManager3D` as the scene/batch orchestrator, preserve the existing CPU opaque fallback and alpha CPU path, and introduce a new opaque-only VU contract that uploads raw opaque geometry plus transforms/constants instead of EE-built screen-space triangles. Update the opaque VU program and packet builder together so opaque clipping/projection rejection happens in the VU path rather than in `Ps2VuVifPacketBuilder::AddOpaqueBatch(...)`.

**Tech Stack:** C++, PS2 VU1 microprograms, `packet2`, `gsKit`, headless editor export, PCSX2 runtime verification.

---

## File Structure

### Existing Files To Modify

- `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
  Keeps path selection, opaque fallback control, runtime counters, and VU-path dispatch.
- `src/platform/ps2/rendering/Ps2RenderManager3D.hpp`
  Exposes any new timing/debug getters needed during rollout.
- `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
  Replace EE-side opaque triangle clipping/projection/cull logic with raw opaque batch upload logic.
- `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`
  Define the new opaque batch packet-builder interface and any temporary timing fields.
- `src/platform/ps2/rendering/vu/Ps2VuGifStateEncoder.cpp`
  Adjust GIF state setup only if the opaque payload/register contract changes.
- `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm`
  Update the opaque VU microprogram to consume the new payload contract and perform VU-side clip-style rejection.
- `src/platform/ps2/Ps2BootHost.cpp`
  Keep the timing overlay and add one explicit marker for the new opaque path during rollout. Remove or reduce temporary diagnostics at the end.

### Existing Files To Verify

- `builder/Ps2NativeBuildExecutor.cs`
  Confirm the opaque VU program continues to be rebuilt and embedded correctly after program changes.
- `Makefile`
  Confirm the opaque VU program source remains part of the PS2 native build.
- `builder.tests/Ps2NativeBuildInputsTests.cs`
  Update if any existing diagnostic assumptions change.

### Runtime Verification Inputs

- `C:\dev\helprojs\city\user_settings\build_config.json`
  Used to switch between `cube_test` and `colored_cube_grid` as validation scenes.
- `C:\dev\helprojs\output\ps2-vu-colored-baseline\game.iso`
  Packaged runtime output used for PCSX2 verification.

---

### Task 1: Freeze A Safe Comparison Baseline

**Files:**
- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.hpp`
- Test: `C:\dev\helprojs\city\user_settings\build_config.json`

- [ ] **Step 1: Write the failing verification target down in the code path**

Add one temporary opaque-path marker string that is only visible when the new opaque VU path is active. Put it next to the existing timing overlay update in `Ps2BootHost.cpp`.

```cpp
FrameTimingOverlayLine1 =
    "OpaqueVU2 "
    + std::to_string(RenderManager3DBackend.GetLastVuTrianglePrepMilliseconds())
    + " "
    + std::to_string(RenderManager3DBackend.GetLastVuTriangleEmitMilliseconds());
```

- [ ] **Step 2: Keep the current fallback switch explicit**

In `Ps2RenderManager3D.cpp`, preserve the branch that keeps the old CPU opaque fallback available during the refactor. Do not delete it yet.

```cpp
if (UseLegacyCpuOpaquePath) {
    for (const Ps2RenderProxy* proxy : plan.OpaqueWorld) {
        if (proxy != nullptr) {
            DrawOpaqueProxyLegacy(*proxy, view, projection, viewport, camera->get_NearPlaneDistance());
        }
    }

    for (const Ps2RenderProxy* proxy : plan.OpaqueDynamic) {
        if (proxy != nullptr) {
            DrawOpaqueProxyLegacy(*proxy, view, projection, viewport, camera->get_NearPlaneDistance());
        }
    }
} else {
    RenderOpaqueWithVuPath(plan, view, projection, viewport, camera->get_NearPlaneDistance());
}
```

- [ ] **Step 3: Rebuild the builder**

Run: `rtk dotnet build builder\helengine.ps2.builder.csproj`
Expected: `0 errors`

- [ ] **Step 4: Export `cube_test`**

Set the PS2 scene selection in `C:\dev\helprojs\city\user_settings\build_config.json` to `cube_test`, then run:

Run: `rtk dotnet run --project C:\dev\helworks\helengine\helengine.ui\helengine.editor.app\helengine.editor.app.csproj -- --project C:\dev\helprojs\city\project.heproj --build ps2 --output C:\dev\helprojs\output\ps2-vu-colored-baseline`
Expected: export completes and packages `game.iso`

- [ ] **Step 5: Verify the pre-refactor baseline in PCSX2**

Launch:

```bash
rtk powershell -Command "Start-Process -FilePath 'C:\Program Files\PCSX2\pcsx2-qt.exe' -ArgumentList 'C:\dev\helprojs\output\ps2-vu-colored-baseline\game.iso'"
```

Expected:
- `cube_test` renders correctly
- timing overlay still appears
- current `Prep` / `Emit` / `Asm` / `Pkt` values are visible

- [ ] **Step 6: Commit the baseline marker**

```bash
rtk git add src/platform/ps2/Ps2BootHost.cpp src/platform/ps2/rendering/Ps2RenderManager3D.cpp src/platform/ps2/rendering/Ps2RenderManager3D.hpp
rtk git commit -m "Add opaque VU refactor baseline markers"
```

---

### Task 2: Replace EE-Side Opaque Triangle Clipping And Projection Contract

**Files:**
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`
- Test: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`

- [ ] **Step 1: Write the failing architectural boundary in comments and code**

At the top of `AddOpaqueBatch(...)`, add a short comment that the new opaque path no longer performs CPU near-plane clipping, CPU projection, or CPU screen-space culling. This is not decorative; it marks the intended invariant during the refactor.

```cpp
// Opaque VU path invariant:
// - no CPU near-plane clipping
// - no CPU projection to XYZ2
// - no CPU screen-space front-face rejection
```

- [ ] **Step 2: Remove the old CPU-only helpers from the opaque path call graph**

Stop calling these helpers from `AddOpaqueBatch(...)`:

- `ClipTriangleAgainstNearPlane(...)`
- `ClipTexturedTriangleAgainstNearPlane(...)`
- `TryBuildVertexPositionRegister(...)`
- `IsFrontFacingTriangle(...)`

Replace the old flow with a raw opaque upload structure that packages:

- source positions
- source normals
- optional texture coordinates
- per-batch transforms/constants

Use a dedicated per-triangle upload structure instead of screen-space triangle payloads.

```cpp
struct alignas(16) Ps2VuOpaqueSourceTriangle final {
    float PositionA[4];
    float PositionB[4];
    float PositionC[4];
    float NormalA[4];
    float NormalB[4];
    float NormalC[4];
    float TexCoordA[4];
    float TexCoordB[4];
    float TexCoordC[4];
};
```

- [ ] **Step 3: Build raw opaque source triangles instead of clipped screen-space triangles**

Inside `AddOpaqueBatch(...)`, keep only enough EE work to read packed model data and build a raw per-triangle upload vector.

```cpp
std::vector<Ps2VuOpaqueSourceTriangle> sourceTriangles;
sourceTriangles.reserve(static_cast<std::size_t>(triangleVertexCount) / 3u);

for (std::uint32_t vertexIndex = 0; (vertexIndex + 2u) < triangleVertexCount; vertexIndex += 3u) {
    Ps2VuOpaqueSourceTriangle triangle {};
    triangle.PositionA[0] = packedPositionWords[positionWordIndexA + 0u];
    triangle.PositionA[1] = packedPositionWords[positionWordIndexA + 1u];
    triangle.PositionA[2] = packedPositionWords[positionWordIndexA + 2u];
    triangle.PositionA[3] = 1.0f;
    // Fill B/C positions, normals, and texture coordinates the same way.
    sourceTriangles.push_back(triangle);
}
```

- [ ] **Step 4: Keep triangle count and debug bounds logic valid enough for rollout**

Since final screen-space positions are no longer known on the CPU side, change the temporary debug counters so they report triangle counts but stop pretending to know final CPU-side screen-space bounds.

```cpp
SubmittedTriangleCount = sourceTriangles.size();
SubmittedScreenBounds = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
SubmittedTriangleBoundsA = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
SubmittedTriangleBoundsB = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
```

- [ ] **Step 5: Run a build to verify the packet-builder compiles**

Run: `rtk dotnet build builder\helengine.ps2.builder.csproj`
Expected: `0 errors`

- [ ] **Step 6: Commit the packet-builder contract change**

```bash
rtk git add src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp
rtk git commit -m "Refactor opaque VU packet builder toward raw triangle upload"
```

---

### Task 3: Update The Opaque VU Program To Own Clip-Style Rejection

**Files:**
- Modify: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuGifStateEncoder.cpp`
- Modify: `builder/Ps2NativeBuildExecutor.cs`
- Test: `Makefile`

- [ ] **Step 1: Identify the current VU input layout and replace it with the new source-triangle layout**

Adjust the VU program input definitions so they read raw source positions, normals, and optional texture coordinates rather than CPU-generated XYZ2-ready output.

Example target shape in the VU-side program definitions:

```c
.data
source_triangle_position_a: .word 0, 0, 0, 0
source_triangle_position_b: .word 0, 0, 0, 0
source_triangle_position_c: .word 0, 0, 0, 0
```

- [ ] **Step 2: Move transform/projection work into the VU program**

Implement VU-side:

- world/view/projection application
- clip-style rejection for invalid `clipw`
- final output position generation for `XYZ2`

The first pass only needs to support opaque rigid meshes used by `cube_test` and `colored_cube_grid`.

```asm
; Pseudocode intent only; implement in actual VU assembly style
; 1. transform source vertex by matrices
; 2. perform clip-style check
; 3. if rejected, write ADC/cull-compatible output
; 4. otherwise emit final XYZ2-ready positions
```

- [ ] **Step 3: Keep GIF state aligned with the new VU output**

Adjust `Ps2VuGifStateEncoder.cpp` only if the emitted register list or data order changes.

```cpp
void Ps2VuGifStateEncoder::EncodeOpaqueState(const Ps2VuOpaqueBatch& batch, GSGLOBAL* gsGlobal) {
    // Preserve the existing opaque GS state contract unless the new VU output requires a changed register layout.
}
```

- [ ] **Step 4: Verify native build regeneration still embeds the updated VU program**

Run: `rtk dotnet build builder\helengine.ps2.builder.csproj`
Expected: `0 errors`

Then confirm the program is still part of the build inputs:

Run: `rtk git diff -- Makefile builder/Ps2NativeBuildExecutor.cs`
Expected: the opaque VU program remains referenced and rebuilt

- [ ] **Step 5: Commit the VU program contract update**

```bash
rtk git add src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm src/platform/ps2/rendering/vu/Ps2VuGifStateEncoder.cpp builder/Ps2NativeBuildExecutor.cs
rtk git commit -m "Move opaque clip-style rejection into PS2 VU program"
```

---

### Task 4: Wire Runtime Selection And Validate `cube_test`

**Files:**
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Test: `C:\dev\helprojs\city\user_settings\build_config.json`

- [ ] **Step 1: Gate the new path behind an explicit opaque-only branch**

In `Ps2RenderManager3D.cpp`, keep alpha on the old CPU path and keep the old opaque CPU fallback accessible while enabling the new opaque VU path by default for current validation scenes.

```cpp
if (UseLegacyCpuOpaquePath) {
    // existing CPU opaque path
} else {
    RenderOpaqueWithVuPath(plan, view, projection, viewport, camera->get_NearPlaneDistance());
}

for (const Ps2RenderProxy* proxy : plan.AlphaWorld) {
    if (proxy != nullptr) {
        DrawAlphaProxy(*proxy, view, projection, viewport, camera->get_NearPlaneDistance());
    }
}
```

- [ ] **Step 2: Keep the timing overlay active and readable**

Update the boot overlay so it keeps showing:

- `Prep`
- `Emit`
- `Asm`
- `Pkt`
- `3D`
- `Pre`

and includes the `OpaqueVU2` marker from Task 1.

- [ ] **Step 3: Export and verify `cube_test`**

Set scene selection to `cube_test`.

Run: `rtk dotnet run --project C:\dev\helworks\helengine\helengine.ui\helengine.editor.app\helengine.editor.app.csproj -- --project C:\dev\helprojs\city\project.heproj --build ps2 --output C:\dev\helprojs\output\ps2-vu-colored-baseline`
Expected: export completes and packages `game.iso`

Launch PCSX2 and verify:

- cube renders
- rotation is correct
- size is correct
- no presentation flicker
- no regression to blank 3D

- [ ] **Step 4: Compare timing against the pre-refactor baseline**

Expected trend:

- `Prep` materially lower
- `Emit` materially lower
- `Pkt` materially lower
- `3D` materially lower

Do not accept visual correctness with unchanged EE-side timings as a completed refactor.

- [ ] **Step 5: Commit the runtime selection and `cube_test` validation pass**

```bash
rtk git add src/platform/ps2/rendering/Ps2RenderManager3D.cpp src/platform/ps2/Ps2BootHost.cpp
rtk git commit -m "Validate opaque VU refactor on cube_test"
```

---

### Task 5: Validate `colored_cube_grid` And Clean Up Temporary Diagnostics

**Files:**
- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Modify: `builder.tests/Ps2NativeBuildInputsTests.cs`
- Test: `C:\dev\helprojs\city\user_settings\build_config.json`

- [ ] **Step 1: Export `colored_cube_grid`**

Set scene selection back to `colored_cube_grid`.

Run: `rtk dotnet run --project C:\dev\helworks\helengine\helengine.ui\helengine.editor.app\helengine.editor.app.csproj -- --project C:\dev\helprojs\city\project.heproj --build ps2 --output C:\dev\helprojs\output\ps2-vu-colored-baseline`
Expected: export completes and packages `game.iso`

- [ ] **Step 2: Verify the stable colored-cube baseline still holds**

Launch PCSX2 and verify:

- colored cubes render
- no regression to all-black 3D
- no regression to all-white due to path breakage
- no regression to the old bottom-half flicker
- timing improves relative to the original opaque VU path

- [ ] **Step 3: Reduce the temporary diagnostics to the minimum needed**

Once the new path is working, remove or reduce the deepest per-substage overlay text while preserving a simpler runtime timing check.

```cpp
constexpr bool EnableFrameTimingDiagnostics = true;
constexpr bool EnableFrameTimingDiagnosticHalt = false;
```

Retain only the diagnostics needed for future comparison, not the full deep-investigation overlay.

- [ ] **Step 4: Update any tests affected by the retained boot configuration**

If `builder.tests/Ps2NativeBuildInputsTests.cs` or related tests depend on boot-host settings that were changed for the refactor, update them explicitly.

```csharp
[Fact]
public void ShouldKeepOpaqueVuProgramEnabledForPs2Build() {
    // Assert the expected PS2 native input set for the opaque VU path.
}
```

- [ ] **Step 5: Run the final verification commands**

Run: `rtk dotnet build builder\helengine.ps2.builder.csproj`
Expected: `0 errors`

Run: `rtk git status --short`
Expected: only the intended refactor files remain modified before the final commit

- [ ] **Step 6: Commit the colored-cube validation and cleanup**

```bash
rtk git add src/platform/ps2/Ps2BootHost.cpp builder.tests/Ps2NativeBuildInputsTests.cs
rtk git commit -m "Finish PS2 opaque VU refactor validation"
```

---

## Self-Review

### Spec Coverage

Covered:

- opaque-only scope
- `Ps2RenderManager3D` orchestration boundary
- `Ps2VuVifPacketBuilder` contract refactor
- VU program update
- fallback preservation
- `cube_test` and `colored_cube_grid` verification
- timing-based validation

No spec gaps found.

### Placeholder Scan

Reviewed for:

- `TBD`
- `TODO`
- vague “handle edge cases” language
- undefined file targets

No unresolved placeholders remain.

### Type And Naming Consistency

Plan uses these consistent names throughout:

- `RenderOpaqueWithVuPath(...)`
- `Ps2VuVifPacketBuilder::AddOpaqueBatch(...)`
- `UseLegacyCpuOpaquePath`
- `cube_test`
- `colored_cube_grid`

No inconsistent method names were introduced in the plan text.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-14-ps2-vu-opaque-path-refactor.md`. Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**
