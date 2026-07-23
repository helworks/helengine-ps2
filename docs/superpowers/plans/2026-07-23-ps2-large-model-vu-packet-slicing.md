# PS2 Large-Model VU Packet Slicing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render tessellated PS2 models larger than one bounded VU packet without freezing the textured Tilt Trial course.

**Architecture:** Introduce an internal `Ps2VuOpaqueBatchSlice` that references an existing opaque batch and a contiguous source-triangle range. The render manager partitions large textured and untextured models into bounded slices; the VIF builder encodes only the selected range while retaining its current clipping, culling, lighting, STQ, texture state, and VIF/GIF submission paths.

**Tech Stack:** C++20, PS2SDK/gsKit, VIF1/GIF packet builders, .NET 9 source-contract tests.

---

### Task 1: Define and verify the opaque triangle-slice contract

**Files:**
- Create: `src/platform/ps2/rendering/vu/Ps2VuOpaqueBatchSlice.hpp`
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Write failing source-contract tests**

Add a test that requires the slice type to expose a batch pointer, first source-triangle index, source-triangle count, and a `Create` factory that validates the source range. Add a test that requires renderer constants for 2,048 textured and 64 untextured source triangles to be used by slicing rather than as whole-model rejection thresholds.

- [ ] **Step 2: Run the focused tests to verify they fail**

Run: `rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3DSourceTests" --no-restore`

Expected: FAIL because `Ps2VuOpaqueBatchSlice.hpp` and slice-based renderer references do not exist.

- [ ] **Step 3: Implement the validated slice value type**

Create `Ps2VuOpaqueBatchSlice` with:

```cpp
const Ps2VuOpaqueBatch* Batch;
std::size_t FirstSourceTriangle;
std::size_t SourceTriangleCount;
static Ps2VuOpaqueBatchSlice Create(const Ps2VuOpaqueBatch& batch, std::size_t firstSourceTriangle, std::size_t sourceTriangleCount);
```

`Create` must reject a null model, zero range, a start at or beyond the model triangle count, and a range extending beyond the model. It returns a descriptor only; it never copies model data.

- [ ] **Step 4: Run the focused tests to verify they pass**

Run: `rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3DSourceTests" --no-restore`

Expected: PASS.

### Task 2: Slice textured models into bounded aggregate packets

**Files:**
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Write failing source-contract tests**

Add a test requiring a 6,064-triangle textured batch to be partitioned into three source ranges: `2048`, `2048`, and `1968`. Require the packet builder textured API to receive slices and read the packed stream from `FirstSourceTriangle * 3` through the slice end. Retain assertions for resolved texture, STQ, clipping, and culling.

- [ ] **Step 2: Run the focused tests to verify they fail**

Run: `rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3DSourceTests" --no-restore`

Expected: FAIL because textured aggregation accepts whole batches only.

- [ ] **Step 3: Implement textured slice partitioning and encoding**

Replace whole-batch textured aggregation inputs with `Ps2VuOpaqueBatchSlice` values. Partition each textured batch into consecutive ranges at `MaximumBoundedTexturedAggregateSourceTriangleCount`, then aggregate only slices whose combined count fits one packet. Update `AddOpaqueTexturedBatches` to iterate each slice range, preserving the existing texture, STQ, screen-frustum clipping, backface culling, lighting, and direct-GIF packet behavior.

- [ ] **Step 4: Run focused tests to verify they pass**

Run: `rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3DSourceTests" --no-restore`

Expected: PASS.

### Task 3: Apply the same bounded slicing to untextured models

**Files:**
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`
- Modify: `builder.tests/Ps2VuUntexturedPathSourceTests.cs`

- [ ] **Step 1: Write failing source-contract tests**

Add tests requiring large untextured batches to be represented by 64-triangle slices and requiring the untextured packet builder to encode each slice range rather than route large models into the whole-model fallback.

- [ ] **Step 2: Run focused tests to verify they fail**

Run: `rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3DSourceTests|FullyQualifiedName~Ps2VuUntexturedPathSourceTests" --no-restore`

Expected: FAIL because the 64-triangle limit only accepts or rejects an entire model.

- [ ] **Step 3: Implement untextured slice grouping and range encoding**

Create untextured slices at `MaximumBoundedUntexturedAggregateSourceTriangleCount`, retain compatible-material grouping for adjacent slices, and update the VIF encoder to use each slice's first and count range. Preserve all existing near-plane and screen-frustum clipping behavior and aggregate packet accounting.

- [ ] **Step 4: Run focused tests to verify they pass**

Run: `rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3DSourceTests|FullyQualifiedName~Ps2VuUntexturedPathSourceTests" --no-restore`

Expected: PASS.

### Task 4: Validate the native renderer and full tessellated game

**Files:**
- Modify: `docs/superpowers/specs/2026-07-23-ps2-large-model-vu-packet-slicing-design.md`

- [ ] **Step 1: Run the PS2 builder test suite**

Run: `rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --no-restore`

Expected: PASS with no failed tests.

- [ ] **Step 2: Build the PS2 native target**

Run: `rtk powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1`

Expected: native PS2 build succeeds.

- [ ] **Step 3: Build the complete DemoDisc PS2 ISO**

Run: `rtk powershell -NoProfile -ExecutionPolicy Bypass -File C:\dev\helworks\helengine\scripts\build-platform.ps1 -Project C:\dev\helprojs\demodisc\project.heproj -Platform ps2 -Output C:\dev\helprojs\demodisc\ps2-build-level01-tessellation-sliced`

Expected: build succeeds and includes the six preconfigured 0.5 tessellated Level 01 models.

- [ ] **Step 4: Launch the exact ISO and document validation evidence**

Run: `rtk powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\launch_in_emulator.ps1 -ArtifactPath C:\dev\helprojs\demodisc\ps2-build-level01-tessellation-sliced\game.iso`

Expected: PCSX2 launches the new full-menu ISO. Update the design spec with the test, native-build, ISO-build, and launch evidence.
