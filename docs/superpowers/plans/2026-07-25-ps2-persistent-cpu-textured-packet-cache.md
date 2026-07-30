# PS2 Persistent CPU Textured Packet Cache Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove avoidable CPU work from the stable textured direct-GIF renderer while preserving clipping, perspective-correct texturing, color, and lighting.

**Architecture:** `Ps2VuVifPacketBuilder` will retain immutable per-model textured triangle sources and reusable packet scratch buffers. Every frame will still transform, classify, clip, light, and emit each triangle from current dynamic state; only packed-source decoding, runtime-index/UV lookup, and transient packet storage are removed.

**Tech Stack:** C++17, PS2SDK gsKit/packet2, existing native source-contract tests in `builder.tests`.

---

### Task 1: Establish the cache contract with a failing source test

**Files:**
- Create: `builder.tests/Ps2PersistentTexturedPacketCacheSourceTests.cs`
- Test: `builder.tests/helengine.ps2.builder.tests.csproj`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void BuilderOwnsBoundedPersistentTexturedPacketCache() {
    string source = File.ReadAllText(GetBuilderHeaderPath());

    Assert.Contains("Ps2VuTexturedPacketCache TexturedPacketCache", source);
}

[Fact]
public void DirectGifPathUsesCachedTriangleSourcesAndReusableWords() {
    string source = File.ReadAllText(GetBuilderSourcePath());

    Assert.Contains("TexturedPacketCache.ResolveTriangleSources", source);
    Assert.Contains("DirectGifPacketWords.clear();", source);
    Assert.DoesNotContain("std::vector<std::array<std::uint64_t, TexturedTrianglePacketWordCount>> texturedTrianglePackets;", source);
}
```

Include XML documentation on the test class, test methods, and path helpers. The path helpers shall resolve the repository root from `AppContext.BaseDirectory` and return the exact builder header/source files.

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --no-restore --filter FullyQualifiedName~Ps2PersistentTexturedPacketCacheSourceTests --nologo
```

Expected: FAIL because no persistent cache member or cache lookup exists.

- [ ] **Step 3: Commit the failing contract**

```powershell
git add -- builder.tests/Ps2PersistentTexturedPacketCacheSourceTests.cs
git commit -m "test(ps2): define persistent textured packet cache contract"
```

### Task 2: Add immutable model triangle cache

**Files:**
- Create: `src/platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.hpp`
- Create: `src/platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.cpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`
- Modify: `Makefile`

- [ ] **Step 1: Define immutable source and bounded cache types**

In `Ps2VuTexturedPacketCache.hpp`, define `Ps2VuTexturedTriangleSource` with `float4 PositionA`, `PositionB`, `PositionC`; `float3 FaceNormal`; and `float2 TexCoordA`, `TexCoordB`, `TexCoordC`. Define a `Ps2VuTexturedPacketCache` class with:

```cpp
const std::vector<Ps2VuTexturedTriangleSource>& ResolveTriangleSources(
    const Ps2VuPackedModel& packedModel,
    const Ps2RuntimeModel* runtimeModel);
void ResetFrame();
```

The implementation stores no world, view, projection, material, texture, GS, or light state. A cache entry matches packed-model pointer, runtime-model pointer, and triangle-vertex count. It keeps at most eight entries and replaces the least-recently-used entry when full. `ResetFrame` advances the monotonic usage serial without freeing retained storage.

- [ ] **Step 2: Build source records only from valid cooked data**

In `Ps2VuTexturedPacketCache.cpp`, validate that the packed model exposes position, normal, and texture-coordinate blocks before decoding. For every three packed vertices, copy the three local positions, pre-sum the three normals into `FaceNormal`, and resolve UVs from runtime indices/runtime texcoords when both are available; otherwise use the packed UV block. Throw `std::invalid_argument` for missing blocks and `std::runtime_error` for inconsistent runtime index/UV data instead of creating defaults.

Add `$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.cpp` beside `Ps2VuVifPacketBuilder.cpp` in the `Makefile` source list.

- [ ] **Step 3: Give the builder ownership**

In `Ps2VuVifPacketBuilder.hpp`, include the cache header and add this private member:

```cpp
Ps2VuTexturedPacketCache TexturedPacketCache;
```

Call `TexturedPacketCache.ResetFrame()` from `Ps2VuVifPacketBuilder::Reset` after output packet state is cleared. Do not alter the behavior of `ReleasePacket`.

- [ ] **Step 4: Compile the focused native source path**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --no-restore --filter FullyQualifiedName~Ps2PersistentTexturedPacketCacheSourceTests --nologo
```

Expected: the source contract still fails until Task 3 introduces the lookup; resolve native compile errors exposed by the existing PS2 build-input tests before continuing.

### Task 3: Replace per-frame decoding and packet allocations in the CPU fallback

**Files:**
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp:2296-2785`
- Test: `builder.tests/Ps2PersistentTexturedPacketCacheSourceTests.cs`

- [ ] **Step 1: Add reusable output buffers to the builder**

Add these private members to `Ps2VuVifPacketBuilder`:

```cpp
std::vector<std::array<std::uint64_t, 22u>> TexturedTrianglePackets;
std::vector<std::uint64_t> DirectGifPacketWords;
```

Clear each vector in `Reset` without shrinking capacity. Reserve before use only when the requested capacity exceeds existing capacity. Keep the existing local four-vertex clipping scratch because its type is intentionally private to the builder implementation and it is not the direct-GIF hot-path allocation.

- [ ] **Step 2: Resolve cached sources once per batch**

At the beginning of each valid textured batch in `AddOpaqueTexturedBatches`, call:

```cpp
const std::vector<Ps2VuTexturedTriangleSource>& triangleSources =
    TexturedPacketCache.ResolveTriangleSources(*batch->Model, runtimeModel);
```

Validate `batchSlice.FirstSourceTriangle` and `batchSlice.SourceTriangleCount` against `triangleSources.size()`. Iterate source-triangle indices, retrieve the immutable source record, and use its local positions, face normal, and UVs in place of packed-word reads, `runtimeIndices` lookups, and per-triangle source construction.

- [ ] **Step 3: Stream direct-GIF output**

For `createVifPacket == false`, build each valid triangle packet using the existing `BuildTexturedTriangleGifPacketBytes`. For the first valid triangle of a batch append words `[0, 8)` followed by words `[8, 22)` to `DirectGifPacketWords`; for later valid triangles append only `[8, 22)`. Do this both for un-clipped and clipped triangle emission. Do not retain a complete packet array in the direct-GIF route.

For `createVifPacket == true`, retain complete packet words in `TexturedTrianglePackets` and keep the existing VIF assembly behavior. `GifPacketBytes` is populated from `DirectGifPacketWords` only after all direct-GIF batches complete.

- [ ] **Step 4: Preserve all dynamic safety and rendering behavior**

Keep `TransformPosition`, `TryClassifyAndBuildTexturedVertexPositionRegister`, `ClipTexturedTriangleAgainstScreenFrustum`, `ResolvePerspectiveTextureReciprocalW`, `ResolveTexturedVertexColor`, all submitted-triangle diagnostics, and backface behavior unchanged. Do not route the CPU fallback into the textured VU fast path.

- [ ] **Step 5: Run the source contract until it passes**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --no-restore --filter FullyQualifiedName~Ps2PersistentTexturedPacketCacheSourceTests --nologo
```

Expected: PASS.

- [ ] **Step 6: Run the related PS2 source tests**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --no-restore --filter "FullyQualifiedName~Ps2PersistentTexturedPacketCacheSourceTests|FullyQualifiedName~Ps2DirectGifUntexturedBatchingSourceTests|FullyQualifiedName~Ps2NativeBuildInputsTests" --nologo
```

Expected: PASS; unrelated existing warnings may remain, but no targeted failure.

- [ ] **Step 7: Commit implementation**

```powershell
git add -- builder.tests/Ps2PersistentTexturedPacketCacheSourceTests.cs src/platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.hpp src/platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.cpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp
git commit -m "perf(ps2): cache CPU textured packet sources"
```

### Task 4: Produce and measure the focused Colored Cubes ISO

**Files:**
- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Use: `scripts/launch_in_emulator.ps1`

- [ ] **Step 1: Stamp a new build identifier**

Set `FrameTimingOverlayBuildNumber` to the next sequential `B` value in `Ps2BootHost.cpp`. Keep the timing labels unchanged so the result remains comparable to B104/B105/B106.

- [ ] **Step 2: Start the isolated build through build-waiter**

Run:

```powershell
rtk dotnet run --project C:\dev\helworks\helengine\tools\build-waiter\helengine.buildwaiter.csproj --no-restore -- --output C:\Users\Helena\AppData\Local\Temp\demodisc-ps2-colored-persistent-cache --require game.iso -- powershell -NoProfile -ExecutionPolicy Bypass -Command "& 'C:\dev\helworks\helengine\scripts\build-platform.ps1' -Project 'C:\dev\helprojs\demodisc\project.heproj' -Platform 'ps2' -Output 'C:\Users\Helena\AppData\Local\Temp\demodisc-ps2-colored-persistent-cache' -Configuration 'Debug' -AdditionalArgs @('--build-profile', 'colored-cube-grid')"
```

Expected: build-waiter reports `game.iso` after its child build completes.

- [ ] **Step 3: Launch only through the project launcher**

Run:

```powershell
rtk proxy powershell -NoProfile -ExecutionPolicy Bypass -File scripts\launch_in_emulator.ps1 -ArtifactPath C:\Users\Helena\AppData\Local\Temp\demodisc-ps2-colored-persistent-cache\game.iso
```

Expected: returns the launched PCSX2 process ID. Do not start `pcsx2-qt.exe` directly.

- [ ] **Step 4: Capture metrics with the HelenUI OCR workflow**

Use the launcher's PCSX2 process/window handle with HelenUI's screenshot CLI and Windows native OCR wrapper. Record the stamped build identifier, finite FPS, and `Drw` metric. Do not manually inspect the screenshot.

- [ ] **Step 5: Report the measured result**

Report the exact OCR result and ask the user to confirm visual correctness. Claim the target only when `Drw <= 2.0 ms` and the user confirms that all colored cubes, UVs, perspective, clipping, and lighting are correct.
