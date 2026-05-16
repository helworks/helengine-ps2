# PS2 VU Clip/Cull Cube Test Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `cube_test` opaque-untextured clip/cull decisions from CPU ownership to the VU microprogram while preserving the already working transform-only VU baseline, packet contract, and explicit CPU fallback code.

**Architecture:** Keep `Ps2RenderManager3D` responsible for scene traversal, batch selection, and VIF dispatch; keep the current transform-only untextured packet shape and slot contract; and add VU-side clip-flag evaluation inside `Ps2OpaqueDraw3D.vsm`. CPU fallback remains available in source for debugging, but the normal `cube_test` VU path must not silently route back through it.

**Tech Stack:** C++, PS2 VU assembly (`.vsm`), packet2/gsKit, .NET builder tests, RTK, editor-driven PS2 export, PCSX2

---

## File Structure

- Modify: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildExecutorTests.cs`
  - Add and tighten source-contract tests for the clip/cull VU program and the untextured VU path guardrails.
- Modify: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildInputsTests.cs`
  - Preserve source-contract coverage that the PS2 renderer stays on the VU path by default and exposes VU diagnostics.
- Modify: `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\programs\Ps2OpaqueDraw3D.vsm`
  - Reintroduce clip/cull instructions into the transform-only microprogram without changing packet slot order or `xgkick` flow.
- Modify: `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\Ps2VuOpaqueUntexturedSetupBuilder.cpp`
  - Keep this file payload-only and ensure it does not become the normal-path visibility authority again.
- Modify: `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.cpp`
  - Preserve the transform-only payload/upload contract while ensuring the untextured VU branch does not reintroduce CPU clip/cull.
- Modify: `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\Ps2RenderManager3D.cpp`
  - Only if necessary to keep the fallback path explicit and the normal path pinned to the VU branch.
- Modify: `C:\dev\helworks\helengine-ps2\builder\Ps2NativeBuildExecutor.cs`
  - Only if generated-core normalization drifts during export and the smallest possible patch is needed.

### Task 1: Lock the Clip/Cull Source Contract

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildExecutorTests.cs`
- Test: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildExecutorTests.cs`

- [ ] **Step 1: Replace the transform-only negative test with a clip/cull positive contract**

Update the current transform-only source assertions so they describe the new target behavior:

```csharp
/// <summary>
/// Verifies that the opaque-untextured VU path performs clip-flag generation in the microprogram.
/// </summary>
[Fact]
public void Ps2OpaqueDraw3DProgram_WhenUsingVuClipCullPath_ShouldUseClipwForTriangleVisibility() {
    string source = File.ReadAllText(GetOpaqueDraw3DProgramPath());

    Assert.Contains("clipw.xyz", source, StringComparison.Ordinal);
    Assert.Contains("fcand", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Add an ADC-state contract test that still protects the current packet slot layout**

Keep the existing packet-slot assertions, but rewrite the ADC expectation around VU-owned clip/cull:

```csharp
/// <summary>
/// Verifies that the clip/cull VU path writes ADC state into the three existing XYZ2 slots before storing XYZ values.
/// </summary>
[Fact]
public void Ps2OpaqueDraw3DProgram_WhenUsingVuClipCullPath_ShouldWriteAdcStateIntoXyz2Slots() {
    string source = File.ReadAllText(GetOpaqueDraw3DProgramPath());

    Assert.Contains("isw.w", source, StringComparison.Ordinal);
    Assert.Contains("22(VI02)", source, StringComparison.Ordinal);
    Assert.Contains("24(VI02)", source, StringComparison.Ordinal);
    Assert.Contains("26(VI02)", source, StringComparison.Ordinal);
}
```

- [ ] **Step 3: Keep the existing CPU-rejection guardrail test on the untextured VU branch**

Retain the focused branch extraction test:

```csharp
Assert.DoesNotContain("ClipTriangleAgainstNearPlane(", untexturedBranch, StringComparison.Ordinal);
Assert.DoesNotContain("IsFrontFacingTriangle(", untexturedBranch, StringComparison.Ordinal);
```

- [ ] **Step 4: Run the focused builder-test slice and confirm the new expectations fail before implementation**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine'
rtk proxy dotnet test C:\dev\helworks\helengine-ps2\builder.tests\helengine.ps2.builder.tests.csproj /p:HelengineRoot=C:\dev\helworks\helengine --filter "FullyQualifiedName~Ps2OpaqueDraw3DProgram_WhenUsingVuClipCullPath|FullyQualifiedName~Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedPath_ShouldNotUseCpuTriangleRejectionMarkers" -v minimal
```

Expected: FAIL because the current microprogram is still the transform-only baseline and does not yet contain `clipw.xyz`/`fcand`.

- [ ] **Step 5: Commit**

```bash
git add builder.tests/Ps2NativeBuildExecutorTests.cs
git commit -m "test: lock PS2 cube_test VU clip cull contract"
```

### Task 2: Reintroduce Clip/Cull in the VU Microprogram Without Changing Packet Shape

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\programs\Ps2OpaqueDraw3D.vsm`
- Test: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildExecutorTests.cs`

- [ ] **Step 1: Update the microprogram comment block to match the new responsibility boundary**

Revise the scope comments so they explicitly say:

- transform vertices
- perform clip-flag based visibility checks
- convert to GS `XYZ2`
- patch the existing packet
- `xgkick`

and explicitly say this pass still excludes lighting and packet-layout changes.

- [ ] **Step 2: Reintroduce `clipw.xyz` and flag evaluation while preserving the transform-only write order**

Use the current transform-only baseline as the starting point and reintroduce clip/cull instructions in place, preserving:

```asm
sq.xyz VF01, 22(VI02)
sq.xyz VF03, 24(VI02)
sq.xyz VF02, 26(VI02)
xgkick VI03
```

The target shape is the earlier VU clip/cull sequence:

```asm
clipw.xyz     VF01, VF01
fcand         VI01, 0x0003FFFF
iaddiu        VI04, VI01, 0x00007FFF
isw.w         VI04, 22(VI02)
```

and likewise for the other two vertices, keeping the second/third store order aligned with the current CPU-facing winding contract.

- [ ] **Step 3: Run the focused source-contract tests**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine'
rtk proxy dotnet test C:\dev\helworks\helengine-ps2\builder.tests\helengine.ps2.builder.tests.csproj /p:HelengineRoot=C:\dev\helworks\helengine --filter "FullyQualifiedName~Ps2OpaqueDraw3DProgram_WhenUsingVuClipCullPath|FullyQualifiedName~Ps2OpaqueDraw3DProgram_WhenUsingTransformOnlyPacketPath_ShouldSwapSecondAndThirdVertexStores" -v minimal
```

Expected: PASS for the clip/cull assertions and PASS for the existing vertex-slot winding-order assertion.

- [ ] **Step 4: Commit**

```bash
git add src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm builder.tests/Ps2NativeBuildExecutorTests.cs
git commit -m "feat: add PS2 cube_test VU clip cull microprogram"
```

### Task 3: Keep the Untextured VU Path Free of Normal-Path CPU Visibility Ownership

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\Ps2VuOpaqueUntexturedSetupBuilder.cpp`
- Modify: `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.cpp`
- Modify: `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\Ps2RenderManager3D.cpp`
- Test: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildExecutorTests.cs`
- Test: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildInputsTests.cs`

- [ ] **Step 1: Inspect the untextured setup builder and keep it payload-only**

Verify that `Ps2VuOpaqueUntexturedSetupBuilder.cpp` still only prepares raw positions, normals, face normal, matrices, and GS constants for payload emission. Do not allow it to become the normal-path visibility authority for `cube_test`.

- [ ] **Step 2: Inspect the untextured VIF-packet-builder branch and keep CPU clip/cull out of it**

Keep the untextured branch shaped like:

```cpp
Ps2VuOpaqueUntexturedSetupBuilder setupBuilder;
setupBuilder.Build(batch, world, view, projection, viewport, normalizedLightDirection, nearPlaneDistance, gsGlobal);
const std::vector<Ps2VuOpaqueUntexturedTriangleSetup>& triangleSetups = setupBuilder.GetTriangleSetups();
for (const Ps2VuOpaqueUntexturedTriangleSetup& triangleSetup : triangleSetups) {
    Ps2VuOpaqueUntexturedTrianglePayload payload {};
    PopulateTrianglePayloadFromSetup(batch, triangleSetup, gsGlobal, payload);
    trianglePayloads.push_back(payload);
}
```

with no `ClipTriangleAgainstNearPlane(` or `IsFrontFacingTriangle(` in that branch.

- [ ] **Step 3: Verify the renderer still defaults to the VU path instead of the CPU fallback**

Use the existing source contracts around:

```cpp
UseLegacyCpuOpaquePath(false)
if (UseLegacyCpuOpaquePath) {
```

to ensure the fallback remains explicit in source while the normal path stays on VU.

- [ ] **Step 4: Run the focused builder-test slice**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine'
rtk proxy dotnet test C:\dev\helworks\helengine-ps2\builder.tests\helengine.ps2.builder.tests.csproj /p:HelengineRoot=C:\dev\helworks\helengine --filter "FullyQualifiedName~Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedPath_ShouldNotUseCpuTriangleRejectionMarkers|FullyQualifiedName~Ps2_renderer3d_routes_opaque_draws_through_vu_path_while_retaining_cpu_fallback" -v minimal
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.cpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp src/platform/ps2/rendering/Ps2RenderManager3D.cpp builder.tests/Ps2NativeBuildInputsTests.cs builder.tests/Ps2NativeBuildExecutorTests.cs
git commit -m "refactor: preserve PS2 cube_test VU clip cull ownership"
```

### Task 4: Export the Real Cube Test Build and Validate in PCSX2

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\builder\Ps2NativeBuildExecutor.cs` only if export-time normalization drifts
- Test: real export output under `C:\dev\helprojs\output\ps2-vu-colored-baseline`

- [ ] **Step 1: Build the PS2 builder from the active implementation checkout**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine'
rtk proxy dotnet build C:\dev\helworks\helengine-ps2\builder\helengine.ps2.builder.csproj /p:HelengineRoot=C:\dev\helworks\helengine -v minimal
```

Expected: PASS.

- [ ] **Step 2: Run the real editor-driven PS2 export**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine'
& 'C:\dev\helworks\helengine\helengine.ui\helengine.editor.app\bin\Debug\net9.0-windows\helengine.editor.app.exe' --project 'C:\dev\helprojs\city\project.heproj' --build ps2 --output 'C:\dev\helprojs\output\ps2-vu-colored-baseline'
```

Expected:

- native build completed
- disc layout written
- iso packaged
- packaged outputs verified

- [ ] **Step 3: If export drifts on generated-core normalization, patch only the smallest exact drift**

If the export fails because generated-core output changed shape, update `builder\Ps2NativeBuildExecutor.cs` with the narrowest possible normalization patch and rerun:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine'
rtk proxy dotnet build C:\dev\helworks\helengine-ps2\builder\helengine.ps2.builder.csproj /p:HelengineRoot=C:\dev\helworks\helengine -v minimal
rtk proxy dotnet test C:\dev\helworks\helengine-ps2\builder.tests\helengine.ps2.builder.tests.csproj /p:HelengineRoot=C:\dev\helworks\helengine --filter "FullyQualifiedName~Ps2NativeBuildExecutorTests" -v minimal
& 'C:\dev\helworks\helengine\helengine.ui\helengine.editor.app\bin\Debug\net9.0-windows\helengine.editor.app.exe' --project 'C:\dev\helprojs\city\project.heproj' --build ps2 --output 'C:\dev\helprojs\output\ps2-vu-colored-baseline'
```

Expected: PASS across build, tests, and export.

- [ ] **Step 4: Launch PCSX2 on the fresh ISO**

Run:

```powershell
& 'C:\Program Files\PCSX2\pcsx2-qt.exe' 'C:\dev\helprojs\output\ps2-vu-colored-baseline\game.iso'
```

Expected visual result:

- `cube_test` boots
- the spinning cube remains visible
- front/back visibility behaves correctly during rotation
- no black screen with UI-only output
- no obvious VIF/FIFO regression

- [ ] **Step 5: Commit**

```bash
git add builder/Ps2NativeBuildExecutor.cs
git commit -m "fix: stabilize PS2 cube_test VU clip cull export"
```

### Task 5: Final Verification and Checkpoint Commit

**Files:**
- Modify: any touched files from previous tasks
- Test: focused builder tests, PS2 builder compile, and real PCSX2 boot

- [ ] **Step 1: Run the focused builder-test verification suite**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine'
rtk proxy dotnet test C:\dev\helworks\helengine-ps2\builder.tests\helengine.ps2.builder.tests.csproj /p:HelengineRoot=C:\dev\helworks\helengine --filter "FullyQualifiedName~Ps2NativeBuildExecutorTests|FullyQualifiedName~Ps2NativeBuildInputsTests" -v minimal
```

Expected: PASS for the focused PS2 builder/source-contract slice relevant to this feature.

- [ ] **Step 2: Run the PS2 builder compile one more time**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine'
rtk proxy dotnet build C:\dev\helworks\helengine-ps2\builder\helengine.ps2.builder.csproj /p:HelengineRoot=C:\dev\helworks\helengine -v minimal
```

Expected: PASS.

- [ ] **Step 3: Confirm the real runtime checkpoint**

Confirm from the live PCSX2 run that:

- `cube_test` renders
- clip/cull is VU-owned for the active opaque-untextured path
- the cube does not disappear incorrectly while rotating
- the fallback path still exists in source for debugging

- [ ] **Step 4: Commit**

```bash
git add src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.cpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp src/platform/ps2/rendering/Ps2RenderManager3D.cpp builder.tests/Ps2NativeBuildExecutorTests.cs builder.tests/Ps2NativeBuildInputsTests.cs builder/Ps2NativeBuildExecutor.cs
git commit -m "feat: move PS2 cube_test clip cull onto VU"
```
