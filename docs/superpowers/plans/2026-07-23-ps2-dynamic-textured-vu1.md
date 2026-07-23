# PS2 Dynamic Textured VU1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render fully visible, dynamically viewed textured opaque batches through VU1 at `Drw <= 2.0 ms` in Colored Cubes, while retaining the proven CPU clip path for every unsafe batch.

**Architecture:** `Ps2RenderManager3D` partitions textured batches using a conservative whole-batch frustum classifier. `Ps2VuVifPacketBuilder` packs VU source records for only safe batches; the expanded textured microprogram transforms, computes perspective Q, and emits bounded GIF payloads. The existing direct-GIF CPU encoder remains unchanged as fallback.

**Tech Stack:** C++17, PS2SDK `packet2`/VIF1/GIF DMA, VU1 assembly (`.vsm`), C# source-contract tests.

---

### Task 1: Establish conservative VU1 eligibility

**Files:**
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.hpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`

- [ ] **Step 1: Write failing source tests**

Require a `CanUseTexturedVuFastPath` helper which receives a batch, world/view/projection matrices, viewport, and near-plane distance. Assert it requires all batch bounds to be strictly inside and that `RenderOpaqueWithVuPath` retains a separate CPU-fallback batch collection.

```csharp
Assert.Contains("bool CanUseTexturedVuFastPath(", source, StringComparison.Ordinal);
Assert.Contains("return allBoundsInside;", source, StringComparison.Ordinal);
Assert.Contains("std::vector<Ps2VuOpaqueBatchSlice> cpuFallbackTexturedBatches;", source, StringComparison.Ordinal);
```

- [ ] **Step 2: Verify the test is red**

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3DSourceTests.Ps2RenderManager3D_WhenClassifyingTexturedBatches_ReservesCpuClippingFallback" --no-restore
```

Expected: FAIL because no conservative fast-path classifier exists.

- [ ] **Step 3: Add the bounds-only classifier**

Build a world-space conservative bound from `Ps2RuntimeModel::GetBoundsMinimum()` and `GetBoundsMaximum()`, transform its eight corners through world/view/projection, and accept only when each corner has positive clip-W, is beyond the near-plane epsilon, and lies strictly inside the clip rectangle. Add unsafe batches to the unchanged CPU collection.

- [ ] **Step 4: Verify the focused test is green and commit**

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3DSourceTests.Ps2RenderManager3D_WhenClassifyingTexturedBatches_ReservesCpuClippingFallback" --no-restore
rtk git add -- builder.tests/Ps2RenderManager3DSourceTests.cs src/platform/ps2/rendering/Ps2RenderManager3D.hpp src/platform/ps2/rendering/Ps2RenderManager3D.cpp
rtk git commit -m "feat(ps2): classify safe textured VU batches"
```

### Task 2: Define and pack the VU1 textured source stream

**Files:**
- Modify: `builder.tests/Ps2NativeBuildInputsTests.cs`
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`

- [ ] **Step 1: Write failing packet-layout tests**

Require `AddOpaqueTexturedVuBatches` to accept safe batch slices, WVP inputs, and texture state, emit a VIF packet, and track submitted safe triangles. Require the packet builder to pack source positions/UVs and material color—not preprojected XYZ/STQ values.

```csharp
Assert.Contains("void AddOpaqueTexturedVuBatches(", header, StringComparison.Ordinal);
Assert.Contains("Ps2VuTexturedSourceTriangle", source, StringComparison.Ordinal);
Assert.Contains("packet2_utils_vu_open_unpack", source, StringComparison.Ordinal);
Assert.DoesNotContain("TryClassifyAndBuildTexturedVertexPositionRegister", vuFastPathBody, StringComparison.Ordinal);
```

- [ ] **Step 2: Verify the tests are red**

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2NativeBuildInputsTests|FullyQualifiedName~Ps2RenderManager3DSourceTests" --no-restore
```

Expected: the new VU1 contract tests fail because the packet builder has only the CPU-projected path.

- [ ] **Step 3: Add bounded VIF source packing**

Define aligned shared-state, batch-header, and source-triangle records. Pack WVP, GS scale/offset, texture state, flat material color, local source positions, and UVs. Split only at triangle boundaries to respect VU1 data-memory and VIF DMA limits. Update renderer submission metrics for VU1 batches, triangles, and bytes.

- [ ] **Step 4: Verify and commit**

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2NativeBuildInputsTests|FullyQualifiedName~Ps2RenderManager3DSourceTests" --no-restore
rtk git add -- builder.tests/Ps2NativeBuildInputsTests.cs builder.tests/Ps2RenderManager3DSourceTests.cs src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp src/platform/ps2/rendering/Ps2RenderManager3D.cpp
rtk git commit -m "feat(ps2): pack textured VU1 source batches"
```

### Task 3: Execute perspective-correct textured batches on VU1

**Files:**
- Modify: `builder.tests/Ps2NativeBuildInputsTests.cs`
- Modify: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`

- [ ] **Step 1: Write the failing microprogram contract test**

Require the textured VU program to load source records, calculate reciprocal Q from clip W, multiply UV values by Q, write Q with the color, and execute a bounded `xgkick` loop. The test must reject the former three-instruction forwarding program.

```csharp
Assert.Contains("div           Q", microProgram, StringComparison.Ordinal);
Assert.Contains("mulq.xy", microProgram, StringComparison.Ordinal);
Assert.Contains("xgkick", microProgram, StringComparison.Ordinal);
Assert.DoesNotContain("NOP                                                        xgkick VI02", microProgram, StringComparison.Ordinal);
```

- [ ] **Step 2: Verify the test is red**

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2NativeBuildInputsTests.Ps2_textured_vu_program_transforms_vertices_with_perspective_q" --no-restore
```

Expected: FAIL because the textured program currently only forwards host-built GIF bytes.

- [ ] **Step 3: Implement the VU loop**

Replace the forwarding program with a bounded record loop. For every source vertex, multiply local position by WVP, calculate Q, convert screen XY/Z, calculate S/T from UV and Q, and write RGBAQ/ST/XYZ2 into the GIF output region. Emit shared TEST/TEX1/TEX0/PRIM state once per compatible packet and kick it after all records are written. Preserve triangle boundaries and do not emit an affine texture coordinate path.

- [ ] **Step 4: Build the native target and verify the new program assembles**

```powershell
rtk docker run --rm -v C:\dev\helworks\helengine-ps2:/workspace -w /workspace helengine-ps2 make
```

Expected: both `Ps2OpaqueTexturedDraw3D.vsm` and the PS2 ELF assemble and link successfully.

- [ ] **Step 5: Commit**

```powershell
rtk git add -- builder.tests/Ps2NativeBuildInputsTests.cs src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp
rtk git commit -m "feat(ps2): transform textured batches on VU1"
```

### Task 4: Verify visual correctness and the render budget

**Files:**
- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Test: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Increment the build stamp and retain fast/fallback metrics**

Increment `FrameTimingOverlayBuildNumber` and expose VU1 versus CPU-fallback triangle counts in the host timing log. Keep `Drw`, `3D`, `Enc`, `Vif`, and `Gif` unchanged.

- [ ] **Step 2: Build and launch a fresh Colored Cubes ISO**

Build `C:\dev\helprojs\demodisc\project.heproj` to a new `ps2-build-colored-cubes-bNN` directory. Wait for `packaged outputs verified`, then launch only its `game.iso` through `scripts\launch_in_emulator.ps1`.

- [ ] **Step 3: Validate runtime evidence**

Use `ps2_bootlog.txt` to confirm a steady sample with `Drw <= 2.0 ms`, VU1 fast-path triangles present, CPU fallback zero for the fully visible grid, and valid GIF drain. Exercise the orbit camera so a near-plane or screen-boundary case routes to CPU fallback without mesh explosion or affine texture warping.

- [ ] **Step 4: Commit and report only measured results**

```powershell
rtk git add -- src/platform/ps2/Ps2BootHost.cpp builder.tests/Ps2RenderManager3DSourceTests.cs
rtk git commit -m "perf(ps2): accelerate dynamic textured opaque batches"
```
