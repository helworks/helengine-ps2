# PS2 Cube Test Visible VU Baseline Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the last visible rotating `cube_test` PS2 VU baseline without rolling back later generic texture-capability work.

**Architecture:** Treat this as a targeted VU contract recovery, not a fresh optimization pass. First, update the PS2 source-contract tests so they describe one coherent visible-cube VU baseline rather than the current mixed diagnostic state. Then restore the matching host packet layout, VU microprogram behavior, and renderer-side orchestration together. Finish by rebuilding the PS2 export and verifying the visible rotating cube in PCSX2 with the VU path active.

**Tech Stack:** C# xUnit source-contract tests, C++20 PS2 runtime code, VU assembly (`.vsm`), `rtk`, PS2 export pipeline, PCSX2.

---

## File Structure

### Files to Modify

- `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildExecutorTests.cs`
  - Replace contradictory diagnostic-path expectations with the last visible VU baseline expectations.
- `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\programs\Ps2OpaqueDraw3D.vsm`
  - Restore the visible rotating cube VU microprogram contract.
- `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.cpp`
  - Restore the matching host-side compact untextured packet layout and dispatch behavior.
- `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\Ps2RenderManager3D.cpp`
  - Restore the matching visible-path orchestration and VU counter publication path if needed by the recovered baseline.

### Files to Inspect While Implementing

- `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\Ps2RenderManager3D.hpp`
- `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.hpp`
- `C:\dev\helworks\helengine-ps2\src\platform\ps2\Ps2BootHost.cpp`

### Validation Commands

- Focused source-contract slice:
  - `rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "Ps2OpaqueDraw3DProgram_WhenUsingVuClipCullPath_ShouldUseClipwForTriangleVisibility|Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedPath_ShouldNotOverwriteTriangleRgbaqSlots|Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedPath_ShouldAvoidVuLightingMath|Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedDiagnosticPath_ShouldBypassVuClipFlags|Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedPath_ShouldNotUseCpuTriangleRejectionMarkers|Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedTemplate_ShouldUseVuOwnedPacketHeader|Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedTemplate_ShouldRespectActiveDepthState" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine`
- Rebuild/export path after source tests are green:
  - use the existing PS2 export flow already used in this repo for `cube_test`

## Task 1: Re-lock the Desired Visible VU Baseline in Tests

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildExecutorTests.cs`

- [ ] **Step 1: Write the failing baseline test updates for the VU microprogram**

Replace the current diagnostic-path expectations so the visible baseline is described consistently:

```csharp
[Fact]
public void Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedVisibleBaseline_ShouldUseVuClipFlags() {
    string repositoryRootPath = ResolveRepositoryRoot();
    string programPath = Path.Combine(
        repositoryRootPath,
        "src",
        "platform",
        "ps2",
        "rendering",
        "vu",
        "programs",
        "Ps2OpaqueDraw3D.vsm");

    string source = File.ReadAllText(programPath);

    Assert.Contains("clipw.xyz", source, StringComparison.Ordinal);
    Assert.Contains("fcand", source, StringComparison.Ordinal);
    Assert.DoesNotContain("iaddiu VI04, VI00, 0x00000000", source, StringComparison.Ordinal);
}
```

Update or delete the current contradictory diagnostic test:

```csharp
[Fact]
public void Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedDiagnosticPath_ShouldBypassVuClipFlags()
```

because the recovery target is explicitly the last visible rotating baseline, not the black-screen diagnostic path.

- [ ] **Step 2: Keep the host-owned flat-color guardrails but pin them to the visible baseline**

Keep these expectations intact in the same file:

```csharp
Assert.DoesNotContain("sq VF12, 21(VI00)", source, StringComparison.Ordinal);
Assert.DoesNotContain("sq VF12, 23(VI00)", source, StringComparison.Ordinal);
Assert.DoesNotContain("sq VF12, 25(VI00)", source, StringComparison.Ordinal);
Assert.DoesNotContain("opmula.xyz", source, StringComparison.Ordinal);
Assert.DoesNotContain("opmsub.xyz", source, StringComparison.Ordinal);
```

That preserves the last visible baseline’s “host-owned flat color, no VU lighting restore in this slice” constraint.

- [ ] **Step 3: Run the focused microprogram tests to verify they fail**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "Ps2OpaqueDraw3DProgram_WhenUsingVuClipCullPath_ShouldUseClipwForTriangleVisibility|Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedPath_ShouldNotOverwriteTriangleRgbaqSlots|Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedPath_ShouldAvoidVuLightingMath|Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedVisibleBaseline_ShouldUseVuClipFlags" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Expected:

```text
FAIL
Current source still bypasses clip flags or still carries the wrong compact-path diagnostic markers.
```

- [ ] **Step 4: Commit the red test change**

```powershell
git add builder.tests/Ps2NativeBuildExecutorTests.cs
git commit -m "test: lock visible cube_test VU baseline"
```

## Task 2: Restore the Matching VU Microprogram Contract

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\programs\Ps2OpaqueDraw3D.vsm`
- Test: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildExecutorTests.cs`

- [ ] **Step 1: Restore VU clip-flag generation and remove the diagnostic clear-ADC setup**

Change the current setup block in `Ps2OpaqueDraw3D.vsm` from the diagnostic clear-ADC form:

```asm
NOP                                                        ilw.x VI05, 40(VI00)
NOP                                                        iaddiu VI04, VI00, 0x00000000
```

to the visible baseline form that uses real clip/cull flag generation. The recovered source must contain:

```asm
clipw.xyz
fcand
```

and must no longer depend on the hardcoded clear-ADC register path.

- [ ] **Step 2: Preserve the visible baseline’s non-lighting compact path**

While restoring clip/cull, keep these visible-baseline constraints:

```asm
; keep host-owned flat color
; do not restore VU-side face-normal lighting
; do not write RGBAQ slots from VF12
```

Concrete source checks that must remain true after the edit:

```asm
; still absent
opmula.xyz
opmsub.xyz
sq VF12, 21(VI00)
sq VF12, 23(VI00)
sq VF12, 25(VI00)
```

- [ ] **Step 3: Run the focused microprogram source tests to verify they pass**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "Ps2OpaqueDraw3DProgram_WhenUsingVuClipCullPath_ShouldUseClipwForTriangleVisibility|Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedPath_ShouldNotOverwriteTriangleRgbaqSlots|Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedPath_ShouldAvoidVuLightingMath|Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedVisibleBaseline_ShouldUseVuClipFlags" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Expected:

```text
PASS
```

- [ ] **Step 4: Commit the microprogram recovery**

```powershell
git add src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm builder.tests/Ps2NativeBuildExecutorTests.cs
git commit -m "feat: restore visible cube_test VU microprogram baseline"
```

## Task 3: Restore the Matching Host Packet Contract

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.cpp`
- Inspect: `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.hpp`
- Test: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildExecutorTests.cs`

- [ ] **Step 1: Reconcile the compact untextured host path with the restored microprogram**

Inspect the active untextured VU branch and restore the packet semantics that correspond to the visible baseline:

```cpp
} else if (!textured) {
    // compact untextured visible baseline path
}
```

The recovered path must continue to satisfy:

```cpp
Assert.DoesNotContain("ClipTriangleAgainstNearPlane(", untexturedBranch, StringComparison.Ordinal);
Assert.DoesNotContain("IsFrontFacingTriangle(", untexturedBranch, StringComparison.Ordinal);
Assert.Contains("packet2_utils_gif_add_set(gifPacket.get(), 1);", source, StringComparison.Ordinal);
Assert.Contains("packet2_utils_gs_add_prim_giftag(gifPacket.get(), &prim, 3u, UntexturedTriangleRegisterList, 2u, 0);", source, StringComparison.Ordinal);
```

and must no longer be aligned to the black-screen diagnostic-only contract.

- [ ] **Step 2: Remove any compact-path host behavior that only exists to support the black-screen diagnostic mode**

Specifically, remove or undo host behavior whose only purpose is the visible-path-bypassed diagnostic contract. Keep the visible baseline’s batching, template, and depth-state behavior, but do not keep diagnostic-only packet assumptions that require the VU program to bypass clip flags.

- [ ] **Step 3: Run the focused packet-builder tests**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedPath_ShouldNotUseCpuTriangleRejectionMarkers|Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedTemplate_ShouldUseVuOwnedPacketHeader|Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedTemplate_ShouldRespectActiveDepthState" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Expected:

```text
PASS
```

- [ ] **Step 4: Commit the packet-builder recovery**

```powershell
git add src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp
git commit -m "feat: restore visible cube_test VU packet baseline"
```

## Task 4: Restore Renderer-Side Visible Baseline Orchestration

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\Ps2RenderManager3D.cpp`
- Inspect: `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\Ps2RenderManager3D.hpp`
- Inspect: `C:\dev\helworks\helengine-ps2\src\platform\ps2\Ps2BootHost.cpp`

- [ ] **Step 1: Reconcile the opaque VU path orchestration with the recovered packet/microprogram baseline**

Inspect the current opaque VU execution branch in `Ps2RenderManager3D.cpp` and restore the path that matches the last visible rotating cube. Preserve the VU path rather than routing `cube_test` back through a CPU-only fallback.

The recovered renderer state must still support the user-visible target:

```cpp
Tri > 0
Disp > 0
```

for the live `cube_test` scene.

- [ ] **Step 2: Preserve later unrelated builder/runtime changes**

Do not touch:

```cpp
generic PS2 texture capability work
platform-owned texture cook integration
```

Only recover the VU renderer/runtime slice needed for the visible cube baseline.

- [ ] **Step 3: Build the focused source test slice again after renderer reconciliation**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "Ps2OpaqueDraw3DProgram_WhenUsingVuClipCullPath_ShouldUseClipwForTriangleVisibility|Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedPath_ShouldNotOverwriteTriangleRgbaqSlots|Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedPath_ShouldAvoidVuLightingMath|Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedVisibleBaseline_ShouldUseVuClipFlags|Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedPath_ShouldNotUseCpuTriangleRejectionMarkers|Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedTemplate_ShouldUseVuOwnedPacketHeader|Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedTemplate_ShouldRespectActiveDepthState" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Expected:

```text
PASS
All focused VU source-contract tests are green together.
```

- [ ] **Step 4: Commit the renderer-side recovery**

```powershell
git add src/platform/ps2/rendering/Ps2RenderManager3D.cpp src/platform/ps2/rendering/Ps2RenderManager3D.hpp src/platform/ps2/Ps2BootHost.cpp
git commit -m "feat: restore visible cube_test VU renderer baseline"
```

## Task 5: Rebuild and Re-verify the Live Cube

**Files:**
- No new code files required.
- Runtime verification against the rebuilt PS2 export and PCSX2.

- [ ] **Step 1: Run the complete focused PS2 source-contract slice**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "Ps2OpaqueDraw3DProgram_WhenUsingVuClipCullPath_ShouldUseClipwForTriangleVisibility|Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedPath_ShouldNotOverwriteTriangleRgbaqSlots|Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedPath_ShouldAvoidVuLightingMath|Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedVisibleBaseline_ShouldUseVuClipFlags|Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedPath_ShouldNotUseCpuTriangleRejectionMarkers|Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedTemplate_ShouldUseVuOwnedPacketHeader|Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedTemplate_ShouldRespectActiveDepthState" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Expected:

```text
PASS
```

- [ ] **Step 2: Rebuild the PS2 export using the existing cube_test workflow**

Run the same PS2 export command path already used in this repo for `cube_test`.

Expected:

```text
PS2 export succeeds and produces a fresh ISO.
```

- [ ] **Step 3: Launch PCSX2 on the rebuilt export**

Run the same launch flow already used in this repo for PS2 validation.

Expected:

```text
PCSX2 boots the rebuilt export successfully.
```

- [ ] **Step 4: Confirm the live runtime target**

Manual/runtime acceptance criteria:

```text
- cube is visible
- cube rotates
- VU path is active
- counters are non-zero again
```

- [ ] **Step 5: Commit any final test-only or tiny recovery adjustments**

```powershell
git add builder.tests/Ps2NativeBuildExecutorTests.cs src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp src/platform/ps2/rendering/Ps2RenderManager3D.cpp src/platform/ps2/rendering/Ps2RenderManager3D.hpp src/platform/ps2/Ps2BootHost.cpp
git commit -m "chore: finalize visible cube_test VU baseline recovery"
```
