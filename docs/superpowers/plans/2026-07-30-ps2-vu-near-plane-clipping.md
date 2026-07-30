# PS2 VU1 Near-Plane Clipping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add proper near-plane clipping to the PS2 textured VU1 renderer without changing the B280 fast path or adding more than 0.2 ms to normal-view rendering.

**Architecture:** Cache conservative local bounds for each 32-triangle packed-model slice, classify those bounds against homogeneous `clipZ = 0` on the EE, and route only intersecting slices to a dedicated clipping microprogram. Fully visible slices retain the exact B280 microprogram, while fully hidden slices are omitted before submission.

**Tech Stack:** C++20, PS2SDK/gsKit, VIF1/VU1 micro assembly, xUnit source-contract tests, PowerShell platform build scripts, PCSX2, HelenUI OCR.

---

## File Map

- Create `src/platform/ps2/rendering/vu/Ps2VuTexturedSourceLimits.hpp`: shared source-slice and VU memory constants.
- Create `src/platform/ps2/rendering/vu/Ps2VuSourceSliceBounds.hpp`: immutable local center/extents value used by the classifier.
- Create `src/platform/ps2/rendering/vu/Ps2VuNearPlaneRoute.hpp`: fast, clipped, and rejected route values.
- Create `src/platform/ps2/rendering/vu/Ps2VuMicroProgramAddresses.hpp`: shared VU1 upload and dispatch addresses.
- Create `src/platform/ps2/rendering/vu/Ps2VuNearPlaneSliceClassifier.hpp`: public classifier interface.
- Create `src/platform/ps2/rendering/vu/Ps2VuNearPlaneSliceClassifier.cpp`: conservative homogeneous near-plane interval test.
- Modify `src/platform/ps2/rendering/vu/Ps2VuPackedModel.hpp`: expose cached slice bounds.
- Modify `src/platform/ps2/rendering/vu/Ps2VuPackedModel.cpp`: calculate bounds once after packed-model load.
- Create `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedClipDraw3D.vsm`: mixed-triangle near-plane clipper and compact GIF emitter.
- Modify `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`: expose route counters.
- Modify `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`: classify slices, skip rejected slices, and select the fast or clipping microprogram.
- Modify `src/platform/ps2/rendering/Ps2RenderManager3D.hpp`: expose accumulated frame route counters.
- Modify `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`: aggregate route counters from every textured packet.
- Modify `src/platform/ps2/Ps2BootHost.cpp`: upload the clipping microprogram, display compact route telemetry, and advance the build number.
- Modify `Makefile`: compile/link the classifier and clipping VU program.
- Modify `builder.tests/Ps2RenderManager3DSourceTests.cs`: source contracts for bounds, route selection, clipping safety, memory limits, upload, and diagnostics.

### Task 1: Define shared bounds, limits, and route types

**Files:**
- Create: `src/platform/ps2/rendering/vu/Ps2VuTexturedSourceLimits.hpp`
- Create: `src/platform/ps2/rendering/vu/Ps2VuSourceSliceBounds.hpp`
- Create: `src/platform/ps2/rendering/vu/Ps2VuNearPlaneRoute.hpp`
- Test: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Write the failing source-contract test**

Add this xUnit test beside the existing textured VU source-layout tests:

```csharp
/// <summary>
/// Ensures textured VU source slices share one capacity and carry conservative local bounds for near-plane routing.
/// </summary>
[Fact]
public void Ps2TexturedVuSlices_WhenPreparedForNearPlaneRouting_ExposeSharedLimitsAndBounds() {
    string repositoryRootPath = GetRepositoryRootPath();
    string limitsSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuTexturedSourceLimits.hpp"));
    string boundsSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuSourceSliceBounds.hpp"));
    string routeSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuNearPlaneRoute.hpp"));

    Assert.Contains("TexturedVuSourceTriangleCapacity = 32u", limitsSource, StringComparison.Ordinal);
    Assert.Contains("TexturedVuMaximumClippedTriangleCount = TexturedVuSourceTriangleCapacity * 2u", limitsSource, StringComparison.Ordinal);
    Assert.Contains("::float3 Center", boundsSource, StringComparison.Ordinal);
    Assert.Contains("::float3 Extents", boundsSource, StringComparison.Ordinal);
    Assert.Contains("Fast", routeSource, StringComparison.Ordinal);
    Assert.Contains("Clipped", routeSource, StringComparison.Ordinal);
    Assert.Contains("Rejected", routeSource, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
rtk dotnet test builder.tests --filter "FullyQualifiedName~Ps2TexturedVuSlices_WhenPreparedForNearPlaneRouting_ExposeSharedLimitsAndBounds" --no-restore --results-directory C:\dev\helworks\builds\helengine-ps2\test-results\near-plane-task-1-red
```

Expected: FAIL because the three headers do not exist.

- [ ] **Step 3: Add the shared value types**

Create `Ps2VuTexturedSourceLimits.hpp`:

```cpp
#pragma once
#include <cstddef>
namespace helengine::ps2 {
    constexpr std::size_t TexturedVuSourceTriangleCapacity = 32u;
    constexpr std::size_t TexturedVuMaximumClippedTriangleCount = TexturedVuSourceTriangleCapacity * 2u;
    constexpr std::size_t TexturedVuGifStateQwordCount = 8u;
    constexpr std::size_t TexturedVuOutputQwordsPerTriangle = 9u;
    constexpr std::size_t TexturedVuOutputStartQword = 0x100u;
    constexpr std::size_t TexturedVuDataMemoryQwordCount = 1024u;
    constexpr std::size_t TexturedVuMaximumOutputEndQword = TexturedVuOutputStartQword
        + TexturedVuGifStateQwordCount
        + (TexturedVuMaximumClippedTriangleCount * TexturedVuOutputQwordsPerTriangle);
    static_assert(TexturedVuMaximumOutputEndQword <= TexturedVuDataMemoryQwordCount);
}
```

Create `Ps2VuSourceSliceBounds.hpp`:

```cpp
#pragma once
#include "float3.hpp"
namespace helengine::ps2 {
    /// <summary>
    /// Stores conservative local-space center and extents for one fixed-capacity textured VU source slice.
    /// </summary>
    struct Ps2VuSourceSliceBounds final {
        ::float3 Center;
        ::float3 Extents;
    };
}
```

Create `Ps2VuNearPlaneRoute.hpp`:

```cpp
#pragma once
namespace helengine::ps2 {
    /// <summary>
    /// Selects the safe textured VU submission path for one conservatively classified source slice.
    /// </summary>
    enum class Ps2VuNearPlaneRoute {
        Fast,
        Clipped,
        Rejected
    };
}
```

- [ ] **Step 4: Replace the packet builder's private capacity constant**

Include `Ps2VuTexturedSourceLimits.hpp` in `Ps2VuVifPacketBuilder.cpp`, remove `MaximumTexturedVuSourceTriangleCount`, and replace its uses with `TexturedVuSourceTriangleCapacity`.

- [ ] **Step 5: Run the focused test and verify GREEN**

Run the Task 1 command again.

Expected: PASS, 1 test passed.

- [ ] **Step 6: Commit Task 1**

```powershell
rtk git add -- builder.tests/Ps2RenderManager3DSourceTests.cs src/platform/ps2/rendering/vu/Ps2VuTexturedSourceLimits.hpp src/platform/ps2/rendering/vu/Ps2VuSourceSliceBounds.hpp src/platform/ps2/rendering/vu/Ps2VuNearPlaneRoute.hpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp
rtk git diff --cached --check
rtk git commit -m "feat(ps2): define textured VU slice routing data"
```

### Task 2: Cache source-slice bounds when packed models load

**Files:**
- Modify: `src/platform/ps2/rendering/vu/Ps2VuPackedModel.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuPackedModel.cpp`
- Test: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Write the failing bounds-cache test**

```csharp
/// <summary>
/// Ensures packed PS2 models calculate textured VU slice bounds once during load and expose them by source range.
/// </summary>
[Fact]
public void Ps2VuPackedModel_WhenLoaded_CachesTexturedSourceSliceBounds() {
    string repositoryRootPath = GetRepositoryRootPath();
    string headerSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuPackedModel.hpp"));
    string implementationSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuPackedModel.cpp"));

    Assert.Contains("GetTexturedSourceSliceBounds", headerSource, StringComparison.Ordinal);
    Assert.Contains("BuildTexturedSourceSliceBounds", headerSource, StringComparison.Ordinal);
    Assert.Contains("std::vector<Ps2VuSourceSliceBounds> TexturedSourceSliceBounds", headerSource, StringComparison.Ordinal);
    Assert.Contains("BuildTexturedSourceSliceBounds();", implementationSource, StringComparison.Ordinal);
    Assert.Contains("TexturedVuSourceTriangleCapacity", implementationSource, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the test and verify RED**

```powershell
rtk dotnet test builder.tests --filter "FullyQualifiedName~Ps2VuPackedModel_WhenLoaded_CachesTexturedSourceSliceBounds" --no-restore --results-directory C:\dev\helworks\builds\helengine-ps2\test-results\near-plane-task-2-red
```

Expected: FAIL because the cache API is absent.

- [ ] **Step 3: Add the packed-model cache API**

Add these members to `Ps2VuPackedModel.hpp`:

```cpp
#include "platform/ps2/rendering/vu/Ps2VuSourceSliceBounds.hpp"

const Ps2VuSourceSliceBounds& GetTexturedSourceSliceBounds(
    std::size_t firstSourceTriangle,
    std::size_t sourceTriangleCount) const;

void BuildTexturedSourceSliceBounds();
std::vector<Ps2VuSourceSliceBounds> TexturedSourceSliceBounds;
```

`BuildTexturedSourceSliceBounds` must iterate triangle ranges in increments of `TexturedVuSourceTriangleCapacity`, include every position from the range, and store:

```cpp
const ::float3 center(
    (minimum.X + maximum.X) * 0.5f,
    (minimum.Y + maximum.Y) * 0.5f,
    (minimum.Z + maximum.Z) * 0.5f);
const ::float3 extents(
    (maximum.X - minimum.X) * 0.5f,
    (maximum.Y - minimum.Y) * 0.5f,
    (maximum.Z - minimum.Z) * 0.5f);
TexturedSourceSliceBounds.push_back(Ps2VuSourceSliceBounds { center, extents });
```

Call `BuildTexturedSourceSliceBounds()` at the end of successful packed-byte validation in `LoadFromPackedBytes`. `GetTexturedSourceSliceBounds` must reject unaligned starts, zero counts, counts above capacity, and ranges beyond the model.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Task 2 test command again.

Expected: PASS, 1 test passed.

- [ ] **Step 5: Commit Task 2**

```powershell
rtk git add -- builder.tests/Ps2RenderManager3DSourceTests.cs src/platform/ps2/rendering/vu/Ps2VuPackedModel.hpp src/platform/ps2/rendering/vu/Ps2VuPackedModel.cpp
rtk git diff --cached --check
rtk git commit -m "feat(ps2): cache textured VU slice bounds"
```

### Task 3: Implement conservative EE near-plane classification

**Files:**
- Create: `src/platform/ps2/rendering/vu/Ps2VuNearPlaneSliceClassifier.hpp`
- Create: `src/platform/ps2/rendering/vu/Ps2VuNearPlaneSliceClassifier.cpp`
- Modify: `Makefile`
- Test: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Write the failing classifier contract test**

```csharp
/// <summary>
/// Ensures near-plane routing uses the homogeneous clip-Z interval of conservative local bounds.
/// </summary>
[Fact]
public void Ps2VuNearPlaneSliceClassifier_WhenBoundsAreClassified_UsesConservativeClipZInterval() {
    string repositoryRootPath = GetRepositoryRootPath();
    string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuNearPlaneSliceClassifier.cpp"));

    Assert.Contains("centerClipZ", source, StringComparison.Ordinal);
    Assert.Contains("radiusClipZ", source, StringComparison.Ordinal);
    Assert.Contains("minimumClipZ >= NearPlaneClassificationEpsilon", source, StringComparison.Ordinal);
    Assert.Contains("maximumClipZ < -NearPlaneClassificationEpsilon", source, StringComparison.Ordinal);
    Assert.Contains("Ps2VuNearPlaneRoute::Clipped", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the test and verify RED**

```powershell
rtk dotnet test builder.tests --filter "FullyQualifiedName~Ps2VuNearPlaneSliceClassifier_WhenBoundsAreClassified_UsesConservativeClipZInterval" --no-restore --results-directory C:\dev\helworks\builds\helengine-ps2\test-results\near-plane-task-3-red
```

Expected: FAIL because the classifier source does not exist.

- [ ] **Step 3: Implement the classifier**

Create a static `Ps2VuNearPlaneSliceClassifier::Classify(const Ps2VuSourceSliceBounds&, const ::float4x4&)` method. Its implementation is:

```cpp
constexpr float NearPlaneClassificationEpsilon = 0.00001f;
const float centerClipZ = (bounds.Center.X * worldViewProjection.M13)
    + (bounds.Center.Y * worldViewProjection.M23)
    + (bounds.Center.Z * worldViewProjection.M33)
    + worldViewProjection.M43;
const float radiusClipZ = (std::abs(worldViewProjection.M13) * bounds.Extents.X)
    + (std::abs(worldViewProjection.M23) * bounds.Extents.Y)
    + (std::abs(worldViewProjection.M33) * bounds.Extents.Z);
const float minimumClipZ = centerClipZ - radiusClipZ;
const float maximumClipZ = centerClipZ + radiusClipZ;
if (minimumClipZ >= NearPlaneClassificationEpsilon) {
    return Ps2VuNearPlaneRoute::Fast;
}
if (maximumClipZ < -NearPlaneClassificationEpsilon) {
    return Ps2VuNearPlaneRoute::Rejected;
}
return Ps2VuNearPlaneRoute::Clipped;
```

Add `Ps2VuNearPlaneSliceClassifier.cpp` to `CPP_SOURCES` in `Makefile`.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Task 3 command again.

Expected: PASS, 1 test passed.

- [ ] **Step 5: Commit Task 3**

```powershell
rtk git add -- builder.tests/Ps2RenderManager3DSourceTests.cs Makefile src/platform/ps2/rendering/vu/Ps2VuNearPlaneSliceClassifier.hpp src/platform/ps2/rendering/vu/Ps2VuNearPlaneSliceClassifier.cpp
rtk git diff --cached --check
rtk git commit -m "feat(ps2): classify textured VU slices at near plane"
```

### Task 4: Route slices without modifying the B280 fast microprogram

**Files:**
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Test: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Write the failing route-selection test**

```csharp
/// <summary>
/// Ensures textured packet assembly rejects hidden slices and selects a separate clipping microprogram only for intersecting slices.
/// </summary>
[Fact]
public void Ps2VuVifPacketBuilder_WhenRoutingNearPlaneSlices_PreservesTheFastProgram() {
    string repositoryRootPath = GetRepositoryRootPath();
    string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

    Assert.Contains("Ps2VuNearPlaneSliceClassifier::Classify", source, StringComparison.Ordinal);
    Assert.Contains("Ps2VuNearPlaneRoute::Rejected", source, StringComparison.Ordinal);
    Assert.Contains("TexturedClipMicroProgramAddress", source, StringComparison.Ordinal);
    Assert.Contains("route == Ps2VuNearPlaneRoute::Clipped", source, StringComparison.Ordinal);
    Assert.Contains("packet2_vif_mscal(packet.get(), microProgramAddress, 0);", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the test and verify RED**

```powershell
rtk dotnet test builder.tests --filter "FullyQualifiedName~Ps2VuVifPacketBuilder_WhenRoutingNearPlaneSlices_PreservesTheFastProgram" --no-restore --results-directory C:\dev\helworks\builds\helengine-ps2\test-results\near-plane-task-4-red
```

Expected: FAIL because route selection and the clipping address are absent.

- [ ] **Step 3: Add route counters and selection**

Add builder counters and getters:

```cpp
std::size_t GetFastTexturedSliceCount() const;
std::size_t GetClippedTexturedSliceCount() const;
std::size_t GetRejectedTexturedSliceCount() const;

std::size_t FastTexturedSliceCount = 0u;
std::size_t ClippedTexturedSliceCount = 0u;
std::size_t RejectedTexturedSliceCount = 0u;
```

Reset them in `Reset`. In `AddOpaqueTexturedVuBatches`, retrieve cached bounds, classify with the already calculated WVP, skip rejected slices before writing shared/source data, and select:

```cpp
const Ps2VuNearPlaneRoute route = Ps2VuNearPlaneSliceClassifier::Classify(bounds, worldViewProjection);
if (route == Ps2VuNearPlaneRoute::Rejected) {
    RejectedTexturedSliceCount++;
    continue;
}
const std::uint16_t microProgramAddress = route == Ps2VuNearPlaneRoute::Clipped
    ? TexturedClipMicroProgramAddress
    : TexturedMicroProgramAddress;
if (route == Ps2VuNearPlaneRoute::Clipped) {
    ClippedTexturedSliceCount++;
} else {
    FastTexturedSliceCount++;
}
```

Keep `Ps2OpaqueTexturedDraw3D.vsm` byte-for-byte unchanged in this task.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Task 4 command again.

Expected: PASS, 1 test passed.

- [ ] **Step 5: Commit Task 4**

```powershell
rtk git add -- builder.tests/Ps2RenderManager3DSourceTests.cs src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp
rtk git diff --cached --check
rtk git commit -m "feat(ps2): route textured VU slices by near plane"
```

### Task 5: Add the dedicated VU1 near-plane clipping program

**Files:**
- Create: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedClipDraw3D.vsm`
- Modify: `Makefile`
- Test: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Write the failing clipping-program contract test**

```csharp
/// <summary>
/// Ensures the clipping VU program classifies clip Z before reciprocal W and can emit the two-triangle worst case.
/// </summary>
[Fact]
public void Ps2OpaqueTexturedClipDraw3D_WhenTriangleCrossesNearPlane_ClipsBeforePerspectiveDivision() {
    string repositoryRootPath = GetRepositoryRootPath();
    string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedClipDraw3D.vsm"));
    int classificationIndex = source.IndexOf("texturedClipClassifyTriangle:", StringComparison.Ordinal);
    int divisionIndex = source.IndexOf("div           Q", StringComparison.Ordinal);

    Assert.True(classificationIndex >= 0);
    Assert.True(divisionIndex > classificationIndex);
    Assert.Contains("texturedClipEmitSecondTriangle:", source, StringComparison.Ordinal);
    Assert.Contains("isw.x VI07, 7(VI04)", source, StringComparison.Ordinal);
    Assert.Contains("xgkick VI04", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the test and verify RED**

```powershell
rtk dotnet test builder.tests --filter "FullyQualifiedName~Ps2OpaqueTexturedClipDraw3D_WhenTriangleCrossesNearPlane_ClipsBeforePerspectiveDivision" --no-restore --results-directory C:\dev\helworks\builds\helengine-ps2\test-results\near-plane-task-5-red
```

Expected: FAIL because the clipping VSM file does not exist.

- [ ] **Step 3: Create the clipping program from the stable fast program**

Copy `Ps2OpaqueTexturedDraw3D.vsm` to the new filename, rename its exported symbols to `Ps2OpaqueTexturedClipDraw3D_CodeStart` and `Ps2OpaqueTexturedClipDraw3D_CodeEnd`, and preserve its matrix, lighting, STQ, winding, compact-output, and GIF-state sequences.

Replace the per-triangle projection flow with these explicit stages:

```text
texturedClipTriangleLoop:
    load three local positions and three raw UV values
    calculate face lighting once
    transform all three positions to clip-space XYZW
texturedClipClassifyTriangle:
    build the three-bit inside mask from clip Z >= epsilon
    branch mask 0 to texturedClipTriangleLoopTail
    branch mask 7 to texturedClipEmitOriginalTriangle
    run one-plane Sutherland-Hodgman over edges C-A, A-B, B-C
    for each crossing edge calculate t = (epsilon - startZ) / (endZ - startZ)
    clamp t to [0,1]
    interpolate clip XYZW and raw UV
    force generated clip Z to epsilon
    emit the first fan triangle when the polygon has at least three vertices
texturedClipEmitSecondTriangle:
    emit vertices 0, 2, 3 only when the polygon has four vertices
texturedClipTriangleLoopTail:
    advance the seven-qword source record and continue
```

For every emitted vertex, execute the stable STQ order only after clipping:

```text
DIV Q, VF00w, clipPositionW
WAITQ
MULQ.xy textureCoordinate, textureCoordinate, Q
ADDQ.z textureCoordinate, VF00, Q
MULQ.xyz projectedPosition, clipPosition, Q
```

Every emitted fan triangle must run the B280 projected winding test unless `DoubleSided` is set. Advance the output pointer and emitted-triangle counter only for accepted triangles. Finish by writing `emittedTriangleCount * 3` into the GIF tag `NLOOP` before `XGKICK`.

Add the new VSM source/object to `VU_PROGRAM_SOURCES` and `VU_PROGRAM_OBJECTS` in `Makefile`.

- [ ] **Step 4: Assemble the VU program through the native build**

Run:

```powershell
rtk powershell -NoProfile -ExecutionPolicy Bypass -File C:\dev\helworks\helengine\scripts\build-platform.ps1 -Project C:\dev\helprojs\demodisc\project.heproj -Platform ps2 -Output C:\dev\helworks\builds\demodisc\ps2\near-plane-task-5-native
```

Expected: `dvp-as` assembles both textured programs and the native build reaches `native build completed`. If assembly fails, change only the rejected opcode/register schedule and rerun this step.

- [ ] **Step 5: Run the focused test and verify GREEN**

Run the Task 5 test command again.

Expected: PASS, 1 test passed.

- [ ] **Step 6: Commit Task 5**

```powershell
rtk git add -- builder.tests/Ps2RenderManager3DSourceTests.cs Makefile src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedClipDraw3D.vsm
rtk git diff --cached --check
rtk git commit -m "feat(ps2): clip textured triangles on VU1"
```

### Task 6: Upload the clipping microprogram and enforce memory limits

**Files:**
- Create: `src/platform/ps2/rendering/vu/Ps2VuMicroProgramAddresses.hpp`
- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Test: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Write the failing upload and memory test**

```csharp
/// <summary>
/// Ensures the clipping VU program is linked, uploaded at its own address, and bounded by the VU1 memory contract.
/// </summary>
[Fact]
public void Ps2BootHost_WhenUploadingTexturedPrograms_UploadsTheNearClipProgramWithinVuLimits() {
    string repositoryRootPath = GetRepositoryRootPath();
    string bootSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2BootHost.cpp"));
    string limitsSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuTexturedSourceLimits.hpp"));
    string addressSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuMicroProgramAddresses.hpp"));

    Assert.Contains("Ps2OpaqueTexturedClipDraw3D_CodeStart", bootSource, StringComparison.Ordinal);
    Assert.Contains("Ps2OpaqueTexturedClipDraw3D_CodeEnd", bootSource, StringComparison.Ordinal);
    Assert.Contains("Ps2VuMicroProgramAddresses.hpp", bootSource, StringComparison.Ordinal);
    Assert.Contains("TexturedClipMicroProgramAddress = 320u", addressSource, StringComparison.Ordinal);
    Assert.Contains("TexturedVuMaximumOutputEndQword <= TexturedVuDataMemoryQwordCount", limitsSource, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the test and verify RED**

```powershell
rtk dotnet test builder.tests --filter "FullyQualifiedName~Ps2BootHost_WhenUploadingTexturedPrograms_UploadsTheNearClipProgramWithinVuLimits" --no-restore --results-directory C:\dev\helworks\builds\helengine-ps2\test-results\near-plane-task-6-red
```

Expected: FAIL because the new symbols are not uploaded.

- [ ] **Step 3: Upload at a non-overlapping micro address**

Declare the clipping start/end symbols beside the existing VU symbols. Create `Ps2VuMicroProgramAddresses.hpp` with `TexturedClipMicroProgramAddress = 320u`, then include that shared constant in the boot uploader and packet builder. Before uploading, calculate the existing textured program's assembled instruction count from its start/end symbols and throw if `TexturedMicroProgramAddress + instructionCount` exceeds `TexturedClipMicroProgramAddress`. Include the clipping packet size in the upload packet and call:

```cpp
packet2_vif_add_micro_program(
    packet2,
    TexturedClipMicroProgramAddress,
    &Ps2OpaqueTexturedClipDraw3D_CodeStart,
    &Ps2OpaqueTexturedClipDraw3D_CodeEnd);
```

The uploader must reject an end address above the 1024-instruction VU1 micro-memory limit rather than silently overwriting another program.

- [ ] **Step 4: Run the focused test and native build**

Run the Task 6 test command, then repeat the Task 5 native-build command with output `C:\dev\helworks\builds\demodisc\ps2\near-plane-task-6-native`.

Expected: test PASS and packaged PS2 output produced without VU overlap diagnostics.

- [ ] **Step 5: Commit Task 6**

```powershell
rtk git add -- builder.tests/Ps2RenderManager3DSourceTests.cs src/platform/ps2/Ps2BootHost.cpp src/platform/ps2/rendering/vu/Ps2VuMicroProgramAddresses.hpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp
rtk git diff --cached --check
rtk git commit -m "feat(ps2): upload textured near-clip VU program"
```

### Task 7: Aggregate route diagnostics without per-triangle timers

**Files:**
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.hpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Test: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Write the failing diagnostics test**

```csharp
/// <summary>
/// Ensures near-plane diagnostics report slice routes without enabling per-triangle timing calls.
/// </summary>
[Fact]
public void Ps2NearPlaneDiagnostics_WhenDisplayed_ReportSliceRoutesWithoutPerTriangleTimers() {
    string repositoryRootPath = GetRepositoryRootPath();
    string rendererSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp"));
    string bootSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2BootHost.cpp"));

    Assert.Contains("FastTexturedSliceCount", rendererSource, StringComparison.Ordinal);
    Assert.Contains("ClippedTexturedSliceCount", rendererSource, StringComparison.Ordinal);
    Assert.Contains("RejectedTexturedSliceCount", rendererSource, StringComparison.Ordinal);
    Assert.Contains("Fast ", bootSource, StringComparison.Ordinal);
    Assert.Contains("Clip ", bootSource, StringComparison.Ordinal);
    Assert.Contains("Rej ", bootSource, StringComparison.Ordinal);
    Assert.Contains("constexpr bool EnableVuPerTriangleTimingDiagnostics = false;", rendererSource, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the test and verify RED**

```powershell
rtk dotnet test builder.tests --filter "FullyQualifiedName~Ps2NearPlaneDiagnostics_WhenDisplayed_ReportSliceRoutesWithoutPerTriangleTimers" --no-restore --results-directory C:\dev\helworks\builds\helengine-ps2\test-results\near-plane-task-7-red
```

Expected: FAIL because route counters are not aggregated or displayed.

- [ ] **Step 3: Aggregate and display compact counters**

Reset frame counters before textured submissions, add each packet builder's counters after packet assembly, expose read-only getters, and append one compact overlay segment:

```text
Fast <count> Clip <count> Rej <count>
```

Do not add `std::clock`, GS synchronization, VIF waits, or readbacks inside the triangle loop.

- [ ] **Step 4: Advance the hardcoded build number**

Set `FrameTimingOverlayBuildNumber` to `B281` so the validation build is unambiguous and does not reuse B280.

- [ ] **Step 5: Run the focused test and verify GREEN**

Run the Task 7 command again.

Expected: PASS, 1 test passed.

- [ ] **Step 6: Commit Task 7**

```powershell
rtk git add -- builder.tests/Ps2RenderManager3DSourceTests.cs src/platform/ps2/rendering/Ps2RenderManager3D.hpp src/platform/ps2/rendering/Ps2RenderManager3D.cpp src/platform/ps2/Ps2BootHost.cpp
rtk git diff --cached --check
rtk git commit -m "feat(ps2): report textured near-plane routes"
```

### Task 8: Build, launch, and validate correctness and performance

**Files:**
- Modify only if evidence identifies a defect: files from Tasks 1-7
- Validate: `C:\dev\helprojs\demodisc\project.heproj`

- [ ] **Step 1: Run all focused source tests**

```powershell
rtk dotnet test builder.tests --filter "FullyQualifiedName~Ps2TexturedVuSlices_WhenPreparedForNearPlaneRouting_ExposeSharedLimitsAndBounds|FullyQualifiedName~Ps2VuPackedModel_WhenLoaded_CachesTexturedSourceSliceBounds|FullyQualifiedName~Ps2VuNearPlaneSliceClassifier_WhenBoundsAreClassified_UsesConservativeClipZInterval|FullyQualifiedName~Ps2VuVifPacketBuilder_WhenRoutingNearPlaneSlices_PreservesTheFastProgram|FullyQualifiedName~Ps2OpaqueTexturedClipDraw3D_WhenTriangleCrossesNearPlane_ClipsBeforePerspectiveDivision|FullyQualifiedName~Ps2BootHost_WhenUploadingTexturedPrograms_UploadsTheNearClipProgramWithinVuLimits|FullyQualifiedName~Ps2NearPlaneDiagnostics_WhenDisplayed_ReportSliceRoutesWithoutPerTriangleTimers" --no-restore --results-directory C:\dev\helworks\builds\helengine-ps2\test-results\near-plane-final
```

Expected: 7 passed, 0 failed.

- [ ] **Step 2: Start a deterministic full PS2 build**

Create `C:\dev\helworks\builds\demodisc\ps2\B281-near-plane-clipping` and launch the existing build waiter with required artifacts `game.iso`, `disc/SYSTEM.CNF`, and `disc/HELENGIN.ELF`. Use `C:\dev\helworks\helengine\scripts\build-platform.ps1`; do not write build products or logs to `%TEMP%`.

- [ ] **Step 3: Wait for the artifact contract**

Poll the build-waiter log and the three required files at intervals below 60 seconds. Continue until the waiter reports completion or a concrete build error.

Expected: all required artifacts are fresh and non-empty.

- [ ] **Step 4: Launch only through the project launcher**

```powershell
rtk powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\launch_in_emulator.ps1 -ArtifactPath C:\dev\helworks\builds\demodisc\ps2\B281-near-plane-clipping\game.iso
```

- [ ] **Step 5: Capture telemetry with HelenUI OCR**

Use `C:\dev\helenui\plugins\screenshot-cli` to list the current PCSX2 handle and capture its client image into the build's `telemetry` directory. Use `C:\dev\helenui\plugins\recognition-cli` with `C:\dev\helenui\pcsx2.json` to OCR the image. Do not inspect the screenshot manually.

Expected normal view: newest build number, no `FPS: N/A`, `Clip 0`, and 3D time within 0.2 ms of B280.

- [ ] **Step 6: Validate near-plane crossing**

Move the orbit camera repeatedly through a large cube and record OCR before, during, and after intersection.

Expected: `Clip` rises above zero only during intersection, no giant triangles or flashing, perspective-correct textures remain stable, and 3D overhead remains approximately 0.5 ms or less.

- [ ] **Step 7: Validate fully behind and winding behavior**

Place the cube behind the camera and rotate around it.

Expected: rejected slices rise, no geometry projects from behind the camera, and opaque backfaces remain single-sided.

- [ ] **Step 8: Commit only evidence-driven corrections**

If validation required a correction, first add a failing focused test, apply one correction, repeat Steps 1-7, and commit only the files associated with that correction. If no correction is needed, do not create an empty validation commit.
