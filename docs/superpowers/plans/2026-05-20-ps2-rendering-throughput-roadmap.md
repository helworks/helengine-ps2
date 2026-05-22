# PS2 Rendering Throughput Roadmap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rework the PS2 renderer toward a production-style Path 1 streaming pipeline where VIF1 feeds VU1 in larger batches, VU1 writes GIF-ready output, and the EE mostly performs orchestration, visibility, state sorting, and DMA scheduling.

**Architecture:** Keep the current renderer working while adding one measurable throughput improvement at a time. The first milestone is a correct and instrumented opaque-untextured cube path; later milestones generalize the packet format, move textured geometry onto the same VU batch model, and add PS2-native texture/VRAM decisions from the platform-owned cook pipeline.

**Tech Stack:** C++ PS2 runtime, ps2sdk/gsKit packet and DMA APIs, VIF1/VU1 microprograms, C# builder/source tests, PCSX2 runtime verification, `cube_test` as the initial visual/performance scene.

---

## Research Constraints To Preserve

- Prefer Path 1 for opaque 3D: EE builds batches, VIF1 stages input, VU1 transforms and emits GIF-ready data with `XGKICK`.
- Treat batches as the unit of performance, not individual triangles or PC-style draw calls.
- Avoid repeated tiny `XGKICK` and `MSCAL` work; build larger VU-local GIF packets and kick once per batch when possible.
- Use double buffering at packet and VIF/VU local-store boundaries so CPU, DMA, VIF, VU, GIF, and GS can overlap.
- Keep Path 3 for overlays, UI, debug, and small exceptional work.
- Make GS VRAM layout and texture format choices explicit platform data, not incidental runtime conversions.
- Measure where time moved after every pass: setup, prep, emit, encode, submit, wait, draw, triangle count, and dispatch count.

## Current Code Starting Point

- `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
  - Builds `Ps2VuOpaqueBatch` records, calls `Ps2VuVifPacketBuilder.AddOpaqueBatch`, sends the returned VIF packet on `DMA_CHANNEL_VIF1`, and publishes overlay metrics.
  - Currently waits for VIF1 before building each batch and has a committed deferred completion wait experiment.
- `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
  - Builds current VIF packets.
  - Untextured path uses `Ps2VuOpaqueUntexturedSetupBuilder` and a fixed two-triangle diagnostic loop.
  - Textured path still builds CPU-side GIF packet bytes per triangle.
- `src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm`
  - Current untextured VU program transforms source positions, patches a helper-generated GIF packet, and issues `xgkick`.
  - Current two-triangle diagnostic repeats the same small packet kick twice.
- `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm`
  - Currently kicks a host-built textured GIF packet rather than owning transform/packet generation.
- `src/platform/ps2/rendering/vu/Ps2VuPackedModel.cpp`
  - Provides packed position, normal, and texcoord blocks.
- `src/platform/ps2/Ps2BootHost.cpp`
  - Loads VU microprograms and initializes VIF/VU double buffering.
  - Also owns 2D texture upload records for UI and font paths.
- `builder.tests/Ps2NativeBuildExecutorTests.cs`
  - Current source-test home for PS2 VU path assertions.

## Milestone 0: Stabilize Measurement And Build Repro

**Purpose:** Before changing more VU behavior, make the performance loop reproducible enough to trust the numbers.

**Files:**
- Modify: `builder.tests/Ps2NativeBuildExecutorTests.cs`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.hpp`
- Optional create: `docs/ps2-rendering-metrics.md`

- [ ] **Step 1: Add a source test that draw timing includes only renderer work**

Add a test in `Ps2NativeBuildExecutorTests.cs` that asserts `PublishPerformanceOverlayMetrics()` is called after VU rendering and that the normal VU submit branch does not include diagnostic GIF code.

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3D_WhenDrawingVuPath_ShouldPublishCorePerformanceOverlayMetrics" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Expected: the existing metric publication test passes or fails only because the new assertion is missing.

- [ ] **Step 2: Add explicit runtime metric fields for DMA overlap**

In `Ps2RenderManager3D.hpp`, add fields and getters for:

```cpp
double LastVuInitialWaitMilliseconds;
double LastVuCompletionWaitMilliseconds;
double LastVuSubmitOnlyMilliseconds;
```

In `Ps2RenderManager3D.cpp`, split the existing wait/submit timing into those fields. Keep the overlay values stable until the FPS component can display the new counters cleanly.

- [ ] **Step 3: Verify the source-test slice**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2NativeBuildExecutorTests" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Expected: all `Ps2NativeBuildExecutorTests` pass.

- [ ] **Step 4: Capture a baseline**

Build and launch `cube_test`, then record:

```text
Set:
Prep:
Emit:
Enc:
Sub:
Wt:
Drw:
Tri:
Disp:
Visible result:
```

Do not proceed unless the cube is visible and rotating.

Commit:

```powershell
rtk git add builder.tests/Ps2NativeBuildExecutorTests.cs src/platform/ps2/rendering/Ps2RenderManager3D.cpp src/platform/ps2/rendering/Ps2RenderManager3D.hpp docs/ps2-rendering-metrics.md
rtk git commit -m "Instrument PS2 VU renderer timing"
```

## Milestone 1: Remove Diagnostic Pair-Batch As The Main Path

**Purpose:** Replace the fixed two-triangle diagnostic with a real batch contract. The research says repeated small `XGKICK`s are the wrong center of gravity; this milestone prepares for one `XGKICK` per VU batch.

**Files:**
- Modify: `builder.tests/Ps2NativeBuildExecutorTests.cs`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Modify: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm`

- [ ] **Step 1: Add failing tests for removing the diagnostic flag from production path**

Add tests that assert:

```csharp
Assert.Contains("constexpr std::uint32_t MaxUntexturedBatchTriangleCount", source, StringComparison.Ordinal);
Assert.DoesNotContain("EnableVuTwoTriangleBatchDiagnostic = true", source, StringComparison.Ordinal);
Assert.DoesNotContain("VuDiagnosticBatchTriangleCount", source, StringComparison.Ordinal);
```

Run the focused tests and confirm they fail on the current diagnostic implementation.

- [ ] **Step 2: Introduce an explicit batch constant**

In `Ps2VuVifPacketBuilder.cpp`, replace the diagnostic constants with:

```cpp
constexpr std::uint32_t MaxUntexturedBatchTriangleCount = 12u;
```

Keep the first production target conservative: one cube-sized batch. Do not make this dynamic yet.

- [ ] **Step 3: Keep one fallback branch for debugging**

Keep a compile-time disabled single-payload fallback:

```cpp
constexpr bool EnableVuSingleTriangleDispatchFallback = false;
```

The fallback should preserve the last known visible single-payload behavior for debugging only.

- [ ] **Step 4: Verify source tests**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2NativeBuildExecutorTests" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Commit:

```powershell
rtk git add builder.tests/Ps2NativeBuildExecutorTests.cs src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm
rtk git commit -m "Replace PS2 VU pair diagnostic with batch contract"
```

## Milestone 2: Build One GIF Packet Per Untextured VU Batch

**Purpose:** Stop patching and kicking one helper-generated GIF packet per triangle. Build one VU-local GIF stream for the batch and `xgkick` once.

**Files:**
- Modify: `builder.tests/Ps2NativeBuildExecutorTests.cs`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Modify: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm`

- [ ] **Step 1: Add a failing test for one batch `xgkick`**

Add a VSM source test asserting:

```csharp
Assert.Contains("__ps2_opaque_draw_3d_build_batch_loop:", source, StringComparison.Ordinal);
Assert.Equal(1, source.Split(new[] { "xgkick" }, StringSplitOptions.None).Length - 1);
Assert.Contains("iaddiu VI03, VI00,", source, StringComparison.Ordinal);
Assert.DoesNotContain("xgkick VI03\n         NOP                                                        iaddiu VI02", source, StringComparison.Ordinal);
```

Expected: fail until VSM builds one output packet.

- [ ] **Step 2: Add a batch header generated by the EE**

In `Ps2VuVifPacketBuilder.cpp`, add a 16-byte batch header before payload data:

```cpp
struct alignas(16) Ps2VuUntexturedBatchHeader final {
    std::uint32_t TriangleCount;
    std::uint32_t OutputGifQwordOffset;
    std::uint32_t PayloadQwordOffset;
    std::uint32_t Reserved;
};
```

This header is explicit and fixed. It replaces the failed ad hoc header path and includes output location so VSM does not infer it from `xtop`.

- [ ] **Step 3: Generate the batch output in VU memory**

In `Ps2OpaqueDraw3D.vsm`, use this shape:

```asm
         NOP                                                        xtop VI02
         NOP                                                        ilw.x VI05, 0(VI02)   ; triangle count
         NOP                                                        ilw.y VI03, 0(VI02)   ; output GIF base qword
         NOP                                                        ilw.z VI06, 0(VI02)   ; first payload qword
__ps2_opaque_draw_3d_build_batch_loop:
         ; transform one triangle from VI06
         ; write packed RGBAQ/XYZ2 records to VI03
         ; advance VI03 by triangle output stride
         ; advance VI06 by payload stride
         ; decrement VI05
         ; loop
         NOP                                                        xgkick VI03_OR_OUTPUT_BASE
```

Use a dedicated output-base integer register so `xgkick` points at the first GIF qword, not the post-loop cursor.

- [ ] **Step 4: Make the EE packet allocate VU space for input and output**

In `Ps2VuVifPacketBuilder.cpp`, upload:

```text
qword 0: Ps2VuUntexturedBatchHeader
qword 1..N: compact source payloads
qword M..: output GIF area initialized with GIF tag/register descriptors if needed
```

Keep the first version cube-sized and untextured only.

- [ ] **Step 5: Runtime verify**

Build and launch `cube_test`. Expected result:

```text
Visible result: rotating cube
Tri: 12
Disp: 1
Enc: should not rise materially from the current baseline
Drw: should move down only if the previous cost included repeated XGKICK stalls
```

If the screen is black or red, restore the compile-time fallback and inspect VIF unpack size, VU memory offsets, and `xgkick` address before changing anything else.

Commit:

```powershell
rtk git add builder.tests/Ps2NativeBuildExecutorTests.cs src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm
rtk git commit -m "Batch PS2 untextured VU output into one GIF kick"
```

## Milestone 3: Replace Large Per-Triangle Payloads With Compact VU Inputs

**Purpose:** The current payload repeats matrices, viewport constants, lighting constants, and packet templates per triangle. The research points toward streaming compact input plus batch constants.

**Files:**
- Create: `src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedBatchLayout.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.cpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Modify: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm`
- Modify: `builder.tests/Ps2NativeBuildExecutorTests.cs`

- [ ] **Step 1: Define layout types in one header**

Create `Ps2VuOpaqueUntexturedBatchLayout.hpp` with:

```cpp
#pragma once

#include <cstdint>

namespace helengine::ps2 {
    struct alignas(16) Ps2VuUntexturedBatchConstants final {
        float WorldViewProjectionMatrix[16];
        float GsScale[4];
        float GsOffset[4];
        float LightDirection[4];
        float LightConstants[4];
    };

    struct alignas(16) Ps2VuUntexturedTriangleInput final {
        float PositionA[4];
        float PositionB[4];
        float PositionC[4];
        float FaceNormal[4];
    };
}
```

Keep one class/struct per file rule in mind if the repo enforces it strictly for new C# classes; this C++ layout header follows the existing C++ style where small packet structs live together.

- [ ] **Step 2: Add source tests for no repeated matrix payload**

Assert `Ps2VuVifPacketBuilder.cpp` no longer copies `WorldMatrix`, `ViewMatrix`, or `ProjectionMatrix` per triangle in the untextured normal path.

- [ ] **Step 3: Update setup builder to emit compact inputs**

`Ps2VuOpaqueUntexturedSetupBuilder` should expose:

```cpp
const Ps2VuUntexturedBatchConstants& GetBatchConstants() const;
const std::vector<Ps2VuUntexturedTriangleInput>& GetTriangleInputs() const;
```

Use existing `SubmittedTriangle*` diagnostics unchanged.

- [ ] **Step 4: Update VSM offsets**

Change `Ps2OpaqueDraw3D.vsm` to load constants once and loop over compact triangle inputs. Verify it still transforms the cube correctly before adding lighting.

- [ ] **Step 5: Runtime verify against baseline**

Expected result:

```text
Visible result: rotating cube
Set: lower or unchanged
Enc: lower than repeated payload baseline
Packet bytes: lower than repeated payload baseline
Tri: 12
Disp: 1
```

Commit:

```powershell
rtk git add src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedBatchLayout.hpp src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.hpp src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.cpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm builder.tests/Ps2NativeBuildExecutorTests.cs
rtk git commit -m "Compact PS2 untextured VU batch inputs"
```

## Milestone 4: Add VIF/VU Double-Buffered Batch Submission

**Purpose:** Make the EE prepare one packet while VIF/VU/GS consume another, matching the research recommendation around overlap.

**Files:**
- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.hpp`
- Modify: `builder.tests/Ps2NativeBuildExecutorTests.cs`

- [ ] **Step 1: Assert VIF double-buffer setup remains present**

Add a test that `Ps2BootHost.cpp` still contains:

```csharp
Assert.Contains("packet2_utils_vu_add_double_buffer(packet2, 8, 496);", source, StringComparison.Ordinal);
```

- [ ] **Step 2: Add explicit frame-lifetime packet ownership**

In `Ps2RenderManager3D`, maintain two packet slots:

```cpp
packet2_t* VuFramePackets[2];
std::uint32_t VuFramePacketIndex;
```

The render loop waits for the previous slot before reusing it, submits the current slot, and flips the index.

- [ ] **Step 3: Keep a conservative wait point**

Wait before modifying/reusing packet memory. Do not wait immediately after send unless a debug flag requests synchronous behavior.

Use:

```cpp
constexpr bool EnableSynchronousVuSubmitDiagnostics = false;
```

- [ ] **Step 4: Runtime verify**

Expected:

```text
Visible result: rotating cube
Sub: small
Wt: may contain previous-frame completion cost
Drw: should decrease only if overlap is effective within the frame loop
No flicker or intermittent missing triangles
```

Commit:

```powershell
rtk git add src/platform/ps2/Ps2BootHost.cpp src/platform/ps2/rendering/Ps2RenderManager3D.cpp src/platform/ps2/rendering/Ps2RenderManager3D.hpp builder.tests/Ps2NativeBuildExecutorTests.cs
rtk git commit -m "Double buffer PS2 VU frame packet submission"
```

## Milestone 5: Move Textured Opaque Geometry Onto The Same VU Batch Model

**Purpose:** The current textured path still builds CPU-side clipped/projected GIF packets. Move it toward the same VU-owned transform and GIF-output model.

**Files:**
- Create: `src/platform/ps2/rendering/vu/Ps2VuOpaqueTexturedBatchLayout.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Modify: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Modify: `builder.tests/Ps2NativeBuildExecutorTests.cs`

- [ ] **Step 1: Add tests that textured path no longer CPU-projects normal triangles**

Assert the textured normal branch does not call:

```csharp
Assert.DoesNotContain("TryBuildVertexPositionRegister(", texturedBranch, StringComparison.Ordinal);
Assert.DoesNotContain("BuildTexturedTriangleGifPacketBytes(", texturedBranch, StringComparison.Ordinal);
```

Keep CPU clipping as a temporary fallback only if needed for near-plane correctness.

- [ ] **Step 2: Define textured compact input**

Use:

```cpp
struct alignas(16) Ps2VuTexturedTriangleInput final {
    float PositionA[4];
    float PositionB[4];
    float PositionC[4];
    float TexCoordA[4];
    float TexCoordB[4];
    float TexCoordC[4];
    float FaceNormal[4];
};
```

- [ ] **Step 3: Build textured GIF output in VU**

Update `Ps2OpaqueTexturedDraw3D.vsm` so it transforms vertices and writes `RGBAQ`, `UV`, `XYZ2` records into a batch GIF stream.

- [ ] **Step 4: Runtime verify on a textured cube/material scene**

Expected:

```text
Visible result: textured rotating cube or selected textured test scene
No texture coordinate inversion
No black texture fallback
Tri count matches expected scene
```

Commit:

```powershell
rtk git add src/platform/ps2/rendering/vu/Ps2VuOpaqueTexturedBatchLayout.hpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm src/platform/ps2/rendering/Ps2RenderManager3D.cpp builder.tests/Ps2NativeBuildExecutorTests.cs
rtk git commit -m "Move PS2 textured opaque geometry to VU batch output"
```

## Milestone 6: Add PS2-Native Texture/VRAM Formats To Runtime Rendering

**Purpose:** Use the platform-owned asset cook work already added for PS2 textures and font atlas textures. Runtime should load PS2-owned payloads directly and choose GS formats deliberately.

**Files:**
- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Modify: PS2 builder texture cook files discovered by `rtk rg -n "PlatformCookWorkItem|TextureAssetColorFormat|AlphaPrecision" builder`
- Modify: `builder.tests/Ps2PlatformAssetBuilderTests.cs`

- [ ] **Step 1: Add builder/runtime contract tests for PS2 texture payload metadata**

Tests should assert cooked PS2 texture artifacts include:

```text
Width
Height
GS PSM
CLUT PSM when applicable
Pixel payload offset/length
CLUT payload offset/length when applicable
```

- [ ] **Step 2: Teach runtime texture loading to use PS2 payload format**

Replace default `GS_PSM_CT32` assumptions for PS2-owned textures with the payload metadata:

```cpp
record.Texture.PSM = payload.Psm;
record.Texture.Clut = payload.ClutPixels;
record.Texture.VramClut = payload.ClutVram;
```

- [ ] **Step 3: Add CLUT upload and TEXFLUSH discipline**

After texture or CLUT upload, call the existing gsKit texture upload path in a way that flushes the GS texture cache before sampling newly uploaded data.

- [ ] **Step 4: Runtime verify UI/font and textured 3D**

Expected:

```text
FPS text visible
Font atlas visible
Textured material visible
No fallback to CT32 for PS2-owned PSM8/PSM4 textures
```

Commit:

```powershell
rtk git add src/platform/ps2/Ps2BootHost.cpp src/platform/ps2/rendering/Ps2RenderManager3D.cpp builder.tests/Ps2PlatformAssetBuilderTests.cs
rtk git commit -m "Load PS2 native texture formats at runtime"
```

## Milestone 7: Add CPU Scene Culling And Batch Sorting Before VU Submission

**Purpose:** The research strongly supports CPU-side culling, LOD, and material sorting as EE orchestration work. This saves VU, GIF, and GS work together.

**Files:**
- Modify: `src/platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.cpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.hpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Modify: `builder.tests/Ps2NativeBuildExecutorTests.cs`

- [ ] **Step 1: Add source tests for material/state sorting**

Assert opaque batches are sorted by:

```text
Textured flag
TextureRelativePath
Material pointer or stable material id
```

- [ ] **Step 2: Add conservative frustum rejection at proxy/bounds level**

Use proxy/model bounds rather than per-triangle clipping. Per-triangle near-plane details can remain VU or fallback work later.

- [ ] **Step 3: Publish rejected counts in overlay diagnostics**

Track:

```text
Visible batch count
Frustum rejected batch count
Missing model/material rejected counts
```

- [ ] **Step 4: Runtime verify against cube_test and a multi-object test**

Expected:

```text
cube_test: no visual change
multi-object scene: off-camera objects reduce submitted triangles and packet bytes
```

Commit:

```powershell
rtk git add src/platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.cpp src/platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.hpp src/platform/ps2/rendering/Ps2RenderManager3D.cpp builder.tests/Ps2NativeBuildExecutorTests.cs
rtk git commit -m "Cull and sort PS2 VU opaque batches"
```

## Milestone 8: Validation Matrix Before Moving Past Cube Test

**Purpose:** Prevent another black/red screen cycle by requiring each renderer stage to prove visual correctness and measured movement.

**Files:**
- Create: `docs/ps2-rendering-validation.md`
- Modify: `builder.tests/Ps2NativeBuildExecutorTests.cs`

- [ ] **Step 1: Document validation scenes**

Create `docs/ps2-rendering-validation.md` with:

```text
cube_test: opaque untextured transform, rotation, depth
textured_cube_test: textured opaque transform and UV
multi_cube_test: batch sorting, culling, packet byte scaling
font_ui_test: PS2 texture/font atlas runtime formats
```

- [ ] **Step 2: Add test names that guard each scene dependency**

Add source tests for required runtime features and assets:

```csharp
Assert.Contains("cooked/engine/models/cube.hasset", source, StringComparison.Ordinal);
Assert.Contains("Core::get_Instance()->SetPerformanceOverlayMetrics(", source, StringComparison.Ordinal);
```

- [ ] **Step 3: Record expected overlay bands**

Document expected metric bands after each milestone. Use ranges because PCSX2 timing and host load vary.

Commit:

```powershell
rtk git add docs/ps2-rendering-validation.md builder.tests/Ps2NativeBuildExecutorTests.cs
rtk git commit -m "Document PS2 rendering validation matrix"
```

## Execution Order

1. Milestone 0: measurement and build reproducibility.
2. Milestone 1: remove the diagnostic pair-batch from the main path.
3. Milestone 2: one VU-built GIF packet and one `XGKICK` per untextured batch.
4. Milestone 3: compact VU input payloads.
5. Milestone 4: packet/VIF double buffering and overlap.
6. Milestone 5: textured opaque geometry on the same VU batch model.
7. Milestone 6: PS2-native texture formats in runtime loading.
8. Milestone 7: CPU culling and state sorting.
9. Milestone 8: validation matrix before broader scene rollout.

## Stop Conditions

- Red screen, black screen, or missing rotating cube: stop and restore the fallback path for the latest milestone only.
- `Enc` rises without reducing `Drw`, `Sub`, `Wt`, or packet bytes: stop and inspect packet layout before adding more VU work.
- `Tri` or `Disp` becomes zero in `cube_test`: stop and inspect asset path resolution, batch builder rejection counts, and packet creation.
- Any editor build hang around generated scripts: kill stale `helengine.editor.app.exe`, run `rtk dotnet build-server shutdown`, clear only `C:\dev\helprojs\city\user_settings\generated_code\bin` and `obj`, then rebuild.

## Self-Review

- Research coverage: Path 1, batch granularity, double buffering, one-kick batch output, texture format discipline, culling/sorting, and validation are each represented by milestones.
- Current-code coverage: The plan targets `Ps2RenderManager3D`, `Ps2VuVifPacketBuilder`, VU programs, packed model inputs, texture runtime loading, and builder tests.
- Risk control: Each milestone keeps a visual runtime check and a focused source-test slice before commit.
- Known gap: This plan intentionally does not specify final near-plane clipping in VU. That should be planned after untextured and textured batch output are stable.
