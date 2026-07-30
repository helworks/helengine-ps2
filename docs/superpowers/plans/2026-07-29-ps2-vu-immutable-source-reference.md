# PS2 VU Immutable Source Reference Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate per-frame copying of immutable textured VU triangle source data while retaining the stable 32-triangle VU double-buffer layout.

**Architecture:** `Ps2VuTexturedPacketCache` already owns 16-byte-aligned packed triangle records. The VIF builder will use those records directly: a CNT/UNPACK uploads dynamic shared state, followed by a REF/UNPACK that uploads the selected immutable source slice to the continuing VU input range. The existing `Ps2OpaqueTexturedDraw3D.vsm` address layout remains unchanged.

**Tech Stack:** C++17, PS2SDK `packet2` / `packet2_utils`, VIF1, VU1, .NET source-contract tests, PS2 ISO build.

---

## File structure

- `src/platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.hpp` owns aligned, packed local-space source triangles and the cache slice capacity.
- `src/platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.cpp` creates cache entries from packed runtime model data.
- `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp` owns the per-frame count that limits the B252 reference experiment to one source slice.
- `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp` constructs VIF DMA tags for shared state, referenced cached source data, and VU dispatch.
- `builder.tests/Ps2TexturedVuReferencePayloadSourceTests.cs` locks the source-route contract to the safe double-buffered VIF commands.
- `builder.tests/Ps2TexturedVuBatchWidthSourceTests.cs` locks the 32-triangle source-slice capacity used by both cache and VIF builder.

### Task 1: Lock the safe source cache contract

**Files:**
- Modify: `builder.tests/Ps2TexturedVuBatchWidthSourceTests.cs`
- Modify: `builder.tests/Ps2TexturedVuReferencePayloadSourceTests.cs`

- [ ] **Step 1: Write the failing source-cache capacity assertion**

```csharp
Assert.Contains(
    "static constexpr std::size_t TexturedVuSourceSliceTriangleCapacity = 32u;",
    packetCacheHeader);
```

- [ ] **Step 2: Run the capacity source test and verify it fails**

Run:

```powershell
rtk dotnet test builder.tests --filter FullyQualifiedName~Ps2TexturedVuBatchWidthSourceTests --no-restore
```

Expected: FAIL because the packet cache still declares the obsolete 55-triangle capacity.

- [ ] **Step 3: Write the failing VIF reference-submission assertion**

```csharp
Assert.Contains("packet2_utils_vu_add_unpack_data(", packetBuilderSource);
Assert.Contains("cachedSourceTriangles.data()", packetBuilderSource);
Assert.DoesNotContain("std::vector<Ps2VuTexturedSourceTriangle> sourceTriangles", packetBuilderSource);
```

- [ ] **Step 4: Run the reference-payload source test and verify it fails**

Run:

```powershell
rtk dotnet test builder.tests --filter FullyQualifiedName~Ps2TexturedVuReferencePayloadSourceTests --no-restore
```

Expected: FAIL because the builder still allocates and copies a per-frame `sourceTriangles` vector.

### Task 2: Submit cached triangle records through VIF REF

**Files:**
- Modify: `src/platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`

- [ ] **Step 1: Make cache and VIF source slices use the stable capacity**

```cpp
static constexpr std::size_t TexturedVuSourceSliceTriangleCapacity = 32u;
```

Keep `MaximumTexturedVuSourceTriangleCount = 32u` in the VIF builder. The two values deliberately match the VU memory proof: 21 qwords of shared state plus 32 times 7 qwords of source data ends before the output buffer beginning at qword `0x100`.

- [ ] **Step 2: Add the one-slice diagnostic gate**

Add a private `std::size_t ReferencedTexturedVuSourceSliceCount = 0u;` field to
`Ps2VuVifPacketBuilder`. Reset it in `Reset()`. Define this temporary gate beside
the VIF constants:

```cpp
constexpr std::size_t MaximumReferencedTexturedVuSourceSliceCount = 1u;
```

For each textured VU source slice, select the reference route only while
`ReferencedTexturedVuSourceSliceCount < MaximumReferencedTexturedVuSourceSliceCount`.
Increment the count only after emitting the reference commands. The other slices
must retain their existing copied-payload route for B252.

- [ ] **Step 3: Use the packed cache records directly for the selected slice**

Replace the per-frame source conversion with the cache-owned packed records:

```cpp
const std::vector<Ps2VuTexturedPackedTriangleSource>& cachedSourceTriangles =
    TexturedPacketCache.ResolvePackedTriangleSources(*batch->Model, runtimeModel);
const Ps2VuTexturedPackedTriangleSource* sourceSlice =
    cachedSourceTriangles.data() + firstSourceTriangle;
const std::size_t sourceSliceByteCount =
    batchSlice.SourceTriangleCount * sizeof(Ps2VuTexturedPackedTriangleSource);
const std::size_t sourceSliceQwordCount = sourceSliceByteCount / 16u;
```

Validate that the slice is non-empty, fits `MaximumTexturedVuSourceTriangleCount`, is a whole number of qwords, and has 16-byte alignment. Throw `std::invalid_argument` if any invariant fails.

- [ ] **Step 4: Separate dynamic shared-state and immutable-source UNPACKs**

```cpp
packet2_utils_vu_open_unpack(packet.get(), XtopGifPacketAddress, 1);
std::memcpy(packet.get()->next, &sharedState, sizeof(sharedState));
packet2_advance_next(packet.get(), sizeof(sharedState));
packet2_utils_vu_close_unpack(packet.get());

packet2_utils_vu_add_unpack_data(
    packet.get(),
    XtopGifPacketAddress + (sizeof(Ps2VuTexturedSharedState) / 16u),
    const_cast<Ps2VuTexturedPackedTriangleSource*>(sourceSlice),
    static_cast<std::uint32_t>(sourceSliceQwordCount),
    1);
```

Immediately append the existing CNT/FLUSH/MSCAL tag. Do not change `Ps2OpaqueTexturedDraw3D.vsm`, VIF base/offset state, or the 32-triangle maximum.

For non-selected slices, retain the existing combined CNT/UNPACK and copied
source records. B252 must not alter their behavior.

- [ ] **Step 5: Remove the redundant per-frame source representation only from the selected path**

Remove `Ps2VuTexturedSourceTriangle`, the loop that fills
`std::vector<Ps2VuTexturedSourceTriangle>`, and its associated memcpy from the
reference path. The copied fallback may directly copy the same packed cache
records; it must not recreate another source representation.

- [ ] **Step 6: Run focused source-contract tests**

Run:

```powershell
rtk dotnet test builder.tests --filter "FullyQualifiedName~Ps2TexturedVuBatchWidthSourceTests|FullyQualifiedName~Ps2TexturedVuReferencePayloadSourceTests|FullyQualifiedName~Ps2TexturedVuSourceCapacityTests" --no-restore --results-directory "C:\dev\helworks\builds\helengine-ps2\test-results\B252"
```

Expected: PASS with zero failed tests.

### Task 3: Package and validate the one-slice diagnostic build

**Files:**
- Modify: `src/platform/ps2/Ps2BootHost.cpp`

- [ ] **Step 1: Set the build identifier**

```cpp
constexpr const char* FrameTimingOverlayBuildNumber = "B252";
```

- [ ] **Step 2: Package through the deterministic build waiter**

Run:

```powershell
rtk dotnet run --no-build --project "C:\dev\helworks\helengine\tools\build-waiter\helengine.buildwaiter.csproj" -- --output "C:\dev\helworks\builds\demodisc\ps2\B252-tilt-play-vu-ref-source" --require game.iso --require disc/SYSTEM.CNF --require disc/HELENGIN.ELF -- powershell -NoProfile -ExecutionPolicy Bypass -File "C:\dev\helworks\helengine\scripts\build-platform.ps1" -Project "C:\dev\helprojs\demodisc\project.heproj" -Platform ps2 -Output "C:\dev\helworks\builds\demodisc\ps2\B252-tilt-play-vu-ref-source"
```

Expected: a fresh ISO and required disc files in the named workspace-owned output directory.

- [ ] **Step 3: Launch only the packaged ISO**

Run:

```powershell
rtk powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\launch_in_emulator.ps1" -ArtifactPath "C:\dev\helworks\builds\demodisc\ps2\B252-tilt-play-vu-ref-source\game.iso"
```

Expected: PCSX2 launches B252, with stable geometry and lighting. B252 proves
that a REF/UNPACK source slice can coexist with the stable copied path; its
frame-level timing is diagnostic only because all but one slice still use copies.

- [ ] **Step 4: Commit the focused implementation**

```powershell
rtk git add -- src/platform/ps2/Ps2BootHost.cpp src/platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.hpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp builder.tests/Ps2TexturedVuBatchWidthSourceTests.cs builder.tests/Ps2TexturedVuReferencePayloadSourceTests.cs
rtk git commit -m "perf(ps2): reference cached textured VU source slices"
```

Expected: the commit includes only the VIF cached-source submission change and its focused tests.

### Task 4: Expand references after B252 acceptance

**Files:**
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Modify: `src/platform/ps2/Ps2BootHost.cpp`

- [ ] **Step 1: Remove the diagnostic cap**

Replace the B252 gate with a full-route selection:

```cpp
const bool useCachedSourceReference = true;
```

The copied fallback remains in the source as an explicit diagnostic switch but
is not selected by the normal route. Do not alter source destination addresses
or the VU program.

- [ ] **Step 2: Package B253 and compare metrics**

Set `FrameTimingOverlayBuildNumber` to `"B253"`, package with the same build
waiter command as Task 3 using output
`C:\dev\helworks\builds\demodisc\ps2\B253-tilt-play-vu-ref-source-all`, and
launch its `game.iso` through `launch_in_emulator.ps1`.

Expected: all textured slices render stably with lower `Enc` and `Asm` than
B251. A crash, FPS N/A, or geometry/lighting corruption rejects this expansion
and returns the normal route to B251's copied source path.
