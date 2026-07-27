# PS2 VU Source Cache and Lighting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move Colored Cubes' repeated immutable-source decoding and flat diffuse-light calculation out of the EE packet encoder while preserving the CPU clipping fallback and reaching `Drw <= 2.0 ms`.

**Architecture:** The bounded `Ps2VuTexturedPacketCache` supplies immutable local positions, local face normals, and UVs to the conservative textured VU1 route. VU1 receives per-batch material and local-light constants, calculates a flat diffuse RGBA value per source triangle, transforms the vertices, and emits perspective-correct STQ output. Batches that can meet a clip plane continue through the existing CPU encoder unchanged.

**Tech Stack:** C++17, PS2SDK packet2/VIF1/GIF DMA, VU1 `.vsm` assembly, C# source-contract tests, HelenUI OCR.

---

## File structure

- `src/platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.hpp/.cpp` owns bounded immutable local triangle source data shared by CPU and VU paths.
- `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp/.cpp` packs cached source records, per-batch VU lighting state, and coarse source-packing timings.
- `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm` calculates flat diffuse RGBA on VU1 before emitting STQ/RGBAQ/XYZ2.
- `src/platform/ps2/rendering/Ps2RenderPerformanceMetrics.hpp` publishes non-overlapping source-cache and source-pack timings.
- `src/platform/ps2/rendering/Ps2RenderManager3D.cpp` aggregates those timings without changing the conservative fallback classifier.
- `src/platform/ps2/Ps2BootHost.cpp` increments the build stamp and exposes the two coarse timings in the profiling overlay.
- `builder.tests/Ps2NativeBuildInputsTests.cs` and `builder.tests/Ps2RenderManager3DSourceTests.cs` enforce source and VU-contract invariants.

### Task 1: Lock down the VU cached-source contract

**Files:**
- Modify: `builder.tests/Ps2NativeBuildInputsTests.cs`
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`

- [ ] **Step 1: Write failing source-contract tests**

Add a test requiring the VU fast-path method body to resolve cached triangle sources and reject direct packed-buffer/index access:

```csharp
int methodStart = source.IndexOf("void Ps2VuVifPacketBuilder::AddOpaqueTexturedVuBatches(", StringComparison.Ordinal);
string methodBody = source[methodStart..];
Assert.Contains("TexturedPacketCache.ResolveTriangleSources(*batch->Model, runtimeModel)", methodBody, StringComparison.Ordinal);
Assert.DoesNotContain("const float* packedPositionWords", methodBody, StringComparison.Ordinal);
Assert.DoesNotContain("const std::vector<std::uint16_t>* runtimeIndices", methodBody, StringComparison.Ordinal);
Assert.Contains("sourceTriangle.FaceNormal", methodBody, StringComparison.Ordinal);
```

- [ ] **Step 2: Run the focused test and verify red**

Run:

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --no-restore --filter "FullyQualifiedName~Ps2NativeBuildInputsTests.Ps2_textured_vu_fast_path_uses_cached_immutable_triangle_sources"
```

Expected: FAIL because `AddOpaqueTexturedVuBatches` still decodes packed position, normal, UV, and index buffers directly.

- [ ] **Step 3: Replace direct source decoding with the existing immutable cache**

In `AddOpaqueTexturedVuBatches`, remove the `packedPositionWords`, `packedNormalWords`, `packedTexCoordWords`, runtime-index, runtime-normal, and runtime-UV lookups. Use the cache record for each source triangle:

```cpp
const Ps2RuntimeModel* runtimeModel = batch->Proxy != nullptr ? batch->Proxy->GetModel() : nullptr;
const std::vector<Ps2VuTexturedTriangleSource>& triangleSources = TexturedPacketCache.ResolveTriangleSources(*batch->Model, runtimeModel);
const Ps2VuTexturedTriangleSource& sourceTriangle = triangleSources[sourceTriangleIndex];
```

Copy `PositionA/B/C` and `TexCoordA/B/C` from `sourceTriangle`, and copy its local `FaceNormal` into the VU source record. Preserve existing source-range validation before indexing the cache vector.

- [ ] **Step 4: Run the focused test and verify green**

Run the command from Step 2.

Expected: PASS.

- [ ] **Step 5: Commit the contract-preserving cache change**

```powershell
rtk git add -- builder.tests/Ps2NativeBuildInputsTests.cs builder.tests/Ps2RenderManager3DSourceTests.cs src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp
rtk git commit -m "perf(ps2): reuse textured VU triangle sources"
```

### Task 2: Pack VU-local lighting inputs

**Files:**
- Modify: `builder.tests/Ps2NativeBuildInputsTests.cs`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`

- [ ] **Step 1: Write failing layout tests**

Require source records to carry a local face normal and shared state to carry local light plus material lighting values:

```csharp
Assert.Contains("float FaceNormal[4];", source, StringComparison.Ordinal);
Assert.Contains("float LocalLightDirection[4];", source, StringComparison.Ordinal);
Assert.Contains("float MaterialLighting[4];", source, StringComparison.Ordinal);
Assert.Contains("sourceTriangle.FaceNormal[0] = triangleSource.FaceNormal.X;", source, StringComparison.Ordinal);
Assert.Contains("TransformDirection", source, StringComparison.Ordinal);
```

- [ ] **Step 2: Run the focused test and verify red**

Run:

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --no-restore --filter "FullyQualifiedName~Ps2NativeBuildInputsTests.Ps2_textured_vu_source_packet_contains_local_lighting_inputs"
```

Expected: FAIL because the VU source payload still contains a CPU-computed `LitColor` field.

- [ ] **Step 3: Define data layout and pack only batch-level dynamic lighting state**

Replace `LitColor` with `FaceNormal` in `Ps2VuTexturedSourceTriangle`. Add these aligned fields to `Ps2VuTexturedSharedState`:

```cpp
float LocalLightDirection[4];
float MaterialLighting[4];
```

Populate them once per batch. `LocalLightDirection` is the normalized world light transformed into local space with a direction transform (W = 0). `MaterialLighting` stores base RGB and the diffuse multiplier. Reject a batch from the VU fast path when the material is unlit, uses showcase/expensive lighting, has non-zero emissive strength, or has non-zero specular strength; those batches retain the CPU path.

- [ ] **Step 4: Update payload-size constants and validate alignment**

Keep every VIF record a 16-byte multiple and retain these assertions:

```cpp
static_assert((sizeof(Ps2VuTexturedSourceTriangle) % 16u) == 0u);
static_assert((sizeof(Ps2VuTexturedSharedState) % 16u) == 0u);
```

Recalculate `TexturedVuSourceBatchPayloadQwordCount` only through `sizeof`, never a duplicated literal.

- [ ] **Step 5: Run the focused test and commit**

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --no-restore --filter "FullyQualifiedName~Ps2NativeBuildInputsTests.Ps2_textured_vu_source_packet_contains_local_lighting_inputs"
rtk git add -- builder.tests/Ps2NativeBuildInputsTests.cs src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp
rtk git commit -m "feat(ps2): pack textured VU lighting inputs"
```

Expected: focused test PASS.

### Task 3: Calculate flat diffuse color on VU1

**Files:**
- Modify: `builder.tests/Ps2NativeBuildInputsTests.cs`
- Modify: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm`

- [ ] **Step 1: Write failing VU-program contract tests**

Add one test requiring normal/light dot product, clamped intensity, material-color multiplication, and the absence of the host `LitColor` load:

```csharp
Assert.Contains("mulax         ACC", microProgram, StringComparison.Ordinal);
Assert.Contains("madday        ACC", microProgram, StringComparison.Ordinal);
Assert.Contains("maddaz", microProgram, StringComparison.Ordinal);
Assert.Contains("maxi", microProgram, StringComparison.Ordinal);
Assert.Contains("mul.xyzw", microProgram, StringComparison.Ordinal);
Assert.DoesNotContain("lq VF07, 6(VI05)", microProgram, StringComparison.Ordinal);
```

- [ ] **Step 2: Run the focused test and verify red**

Run:

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --no-restore --filter "FullyQualifiedName~Ps2NativeBuildInputsTests.Ps2_textured_vu_program_calculates_flat_diffuse_color"
```

Expected: FAIL because the microprogram currently loads precomputed `LitColor` from each triangle source.

- [ ] **Step 3: Change the VU register map and emit VU-computed RGBAQ**

Load `LocalLightDirection` and `MaterialLighting` from the shared header once. For each triangle, load its local face normal, calculate `max(dot(normal, light), 0)`, multiply by the diffuse multiplier, then multiply base RGB by the resulting intensity. Store that color in the three RGBAQ records for the triangle. Keep the existing WVP transform, reciprocal-Q, `mulq.xy` ST calculation, and `xgkick` behavior intact.

Use a single triangle color for all three vertices. Do not add VU clipping or an affine UV path.

- [ ] **Step 4: Run the focused source test and native compile**

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --no-restore --filter "FullyQualifiedName~Ps2NativeBuildInputsTests.Ps2_textured_vu_program_calculates_flat_diffuse_color"
rtk docker run --rm -v C:\dev\helworks\helengine-ps2:/workspace -w /workspace helengine-ps2 make
```

Expected: test PASS and both VU programs assemble/link with no errors.

- [ ] **Step 5: Commit the VU lighting program**

```powershell
rtk git add -- builder.tests/Ps2NativeBuildInputsTests.cs src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm
rtk git commit -m "perf(ps2): calculate flat textured lighting on VU1"
```

### Task 4: Publish non-overlapping source-pack timings

**Files:**
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`
- Modify: `src/platform/ps2/rendering/Ps2RenderPerformanceMetrics.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp/.cpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Modify: `src/platform/ps2/Ps2BootHost.cpp`

- [ ] **Step 1: Write failing telemetry tests**

Require separate metrics for cache resolution and payload packing, and require the overlay to publish both:

```csharp
Assert.Contains("double SourceCacheMilliseconds = 0.0;", metricsHeader, StringComparison.Ordinal);
Assert.Contains("double SourcePayloadMilliseconds = 0.0;", metricsHeader, StringComparison.Ordinal);
Assert.Contains("GetLastSourceCacheMilliseconds()", builderHeader, StringComparison.Ordinal);
Assert.Contains("GetLastSourcePayloadMilliseconds()", builderHeader, StringComparison.Ordinal);
Assert.Contains("\"Cache \"", bootHost, StringComparison.Ordinal);
Assert.Contains("\"Pack \"", bootHost, StringComparison.Ordinal);
```

- [ ] **Step 2: Run the focused test and verify red**

Run:

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --no-restore --filter "FullyQualifiedName~Ps2RenderManager3DSourceTests.Ps2BootHost_WhenProfilingColoredCubes_PublishesVuSourceTimings"
```

Expected: FAIL because those metrics do not exist.

- [ ] **Step 3: Implement coarse phase timing**

Measure one cache-resolution phase around each batch cache lookup and one source-payload phase around copying cached records to the packet. Aggregate them through `Ps2RenderManager3D` into `Ps2RenderPerformanceMetrics`. Do not call `std::clock()` inside the triangle loop. Increment `FrameTimingOverlayBuildNumber` to `B121` and publish `Cache` and `Pack` alongside the existing `Enc`, `Vif`, and `Gif` values.

- [ ] **Step 4: Run PS2 source tests and commit**

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --no-restore --filter "FullyQualifiedName~Ps2RenderManager3DSourceTests|FullyQualifiedName~Ps2NativeBuildInputsTests"
rtk git add -- builder.tests/Ps2RenderManager3DSourceTests.cs src/platform/ps2/rendering/Ps2RenderPerformanceMetrics.hpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp src/platform/ps2/rendering/Ps2RenderManager3D.cpp src/platform/ps2/Ps2BootHost.cpp
rtk git commit -m "feat(ps2): profile textured VU source packing"
```

Expected: all focused source tests PASS.

### Task 5: Measure the optimized Colored Cubes build

**Files:**
- Modify: no source files expected
- Output: `C:\dev\helworks\builds\demodisc\ps2\B121-vu-source-cache-lighting\game.iso`

- [ ] **Step 1: Build in a visible workspace-owned output directory**

Run the repository PS2 build command with `TEMP` and `TMP` directed into `C:\dev\helworks\builds\demodisc\ps2\B121-vu-source-cache-lighting\intermediate`. Wait for the build waiter to report the packaged ISO exists.

- [ ] **Step 2: Launch only the exact ISO through the repository launcher**

```powershell
rtk powershell -ExecutionPolicy Bypass -File .\scripts\launch_in_emulator.ps1 -IsoPath "C:\dev\helworks\builds\demodisc\ps2\B121-vu-source-cache-lighting\game.iso"
```

Expected: launcher reports the exact artifact path and PCSX2 process identifier.

- [ ] **Step 3: Capture metrics through HelenUI**

Capture the `HELENGIN.ELF` window with `screenshot-cli`, then run `recognition-cli analyze` using `C:\dev\helenui\demodisc.json` and the PCSX2 OCR configuration. Read only OCR text, never the image. Record B121, `Drw`, `3D`, `Enc`, `Cache`, `Pack`, `Vif`, `Gif`, `Tri`, `Bat`, and `Bytes`.

- [ ] **Step 4: Validate visual correctness interactively**

Confirm with the user that all 16 cubes are present, retain distinct material colors and lighting, and remain stable while the orbit camera reaches near-camera and screen-boundary conditions. Any unsafe batch must route to CPU clipping without an explosion, missing triangle, or affine texture warp.

- [ ] **Step 5: Record outcome and commit only source changes made by this plan**

```powershell
rtk git status --short
rtk git log -1 --oneline
```

Expected: B121 OCR is associated with the source commits above. The performance goal is complete only when the measured `Drw` is at or below `2.0 ms` and the visual validation passes.
