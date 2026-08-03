# PS2 Hybrid Clipped Batch Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve the current fast textured VU1 renderer for safe geometry while clipping only camera-intersecting textured triangles on the EE and submitting the generated fan triangles through one small, batched, pretransformed VU1 program.

**Architecture:** Keep outer-slice and per-triangle classification in `Ps2VuVifPacketBuilder`. Safe source runs continue to reference immutable packed source records and call `Ps2OpaqueTexturedDraw3D.vsm`; rejected source triangles emit nothing. Intersecting triangles are transformed and clipped through two fixed nine-vertex buffers, converted into fixed 7-qword pretransformed records, accumulated by the already coherent material/texture batch, and submitted through `Ps2OpaqueTexturedPretransformedDraw3D.vsm`. The ordinary 2,048-qword VIF packet allocation remains unchanged unless the bounded route prepass finds an intersecting slice.

**Tech Stack:** C++20, PS2SDK `packet2`/VIF1/GIF DMA, VU1 assembly (`.vsm`), Docker PS2 toolchain, C# xUnit source-contract tests, a host-native C++ clipping test target, DemoDisc cook/export, PCSX2, HelenUI OCR.

## Global Constraints

- Work directly on `main`; do not create or use a worktree.
- Preserve unrelated dirty files and stage every commit with explicit paths.
- Do not edit generated C++ output. Fix source, build registration, or code generation at its owner.
- Do not direct builds, logs, generated sources, ISOs, or native-test outputs to `%TEMP%`. Use `C:\dev\helworks\builds\helengine-ps2\...`.
- Keep `Ps2OpaqueTexturedDraw3D.vsm` byte-for-byte unchanged until the final validation task proves a fast-path change is required. The intended implementation does not require one.
- Do not add heap allocation to the per-triangle clipping or fan-generation path. `packet2`'s existing packet allocation and immutable model caches remain allowed.
- Do not add clock reads, per-triangle logs, or diagnostic bypass constants inside the textured triangle loops.
- Limit this rollout to opaque textured geometry. Do not change untextured, alpha, far-plane, material-cook, texture-cook, tessellation, or scene-authoring behavior.
- Use substantive `/// <summary>` comments for every new type, field, constructor, property, and function. Keep one class per file.
- Use the existing right-handed convention: near-plane inside is `viewZ <= -nearPlaneDistance`; homogeneous side-plane inside is a non-negative distance.
- Build numbers for this rollout are `B321` for the first hybrid-path ISO and `B322` for the cleaned final ISO.
- The user controls the camera and is the authority for visual continuity. HelenUI may OCR text; do not capture or inspect screenshots.
- Every command with potentially large output writes its full log under `C:\dev\helworks\builds\helengine-ps2\logs` and prints only the final bounded section.
- Treat the command blocks below as the command payload. When executing one, redirect its full output to the workspace log directory, preserve `$LASTEXITCODE`, and print at most the final 4,000 characters before returning that exit code.

---

### Task 1: Add a host-tested, allocation-free textured triangle clipper

**Files:**

- Create: `src/platform/ps2/rendering/vu/Ps2VuTexturedClipVertex.hpp`
- Create: `src/platform/ps2/rendering/vu/Ps2VuTexturedClipPolygon.hpp`
- Create: `src/platform/ps2/rendering/vu/Ps2VuTexturedClipPolygon.cpp`
- Create: `src/platform/ps2/rendering/vu/Ps2VuTexturedTriangleClipper.hpp`
- Create: `src/platform/ps2/rendering/vu/Ps2VuTexturedTriangleClipper.cpp`
- Create: `tests/native/Ps2VuTexturedTriangleClipperTests.cpp`
- Modify: `Makefile`

- [ ] **Step 1: Add the failing native clipping tests**

Create `tests/native/Ps2VuTexturedTriangleClipperTests.cpp` as a dependency-free host executable. It must cover:

- one fully inside triangle returning the same three vertices in the same order;
- one triangle fully outside the near plane returning zero vertices;
- one vertex outside the near plane returning four polygon vertices and correctly interpolated UVs;
- two vertices outside the near plane returning three polygon vertices;
- left, right, bottom, and top crossings using `x + w`, `w - x`, `y + w`, and `w - y`;
- a vertex exactly on a plane remaining inside without a duplicate;
- finite interpolation factors clamped to `[0, 1]`;
- an input containing `NaN` or infinity throwing `std::invalid_argument`;
- the fixed polygon capacity being exactly nine and never silently truncating.

The test executable should return non-zero after printing the first failed case. Keep assertions deterministic; do not use randomized inputs.

- [ ] **Step 2: Register the native test target and prove RED**

Add this bounded target to `Makefile`:

```make
PS2_RENDERING_TEST_TARGET := $(BUILD_DIR)/tests/ps2-vu-textured-clipper-tests
PS2_RENDERING_TEST_SOURCES := \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuTexturedClipPolygon.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuTexturedTriangleClipper.cpp \
	tests/native/Ps2VuTexturedTriangleClipperTests.cpp

.PHONY: ps2-rendering-tests

ps2-rendering-tests: $(PS2_RENDERING_TEST_TARGET)
	$(PS2_RENDERING_TEST_TARGET)

$(PS2_RENDERING_TEST_TARGET): $(PS2_RENDERING_TEST_SOURCES)
	@mkdir -p $(dir $@)
	$(HOST_CXX) $(HOST_CXXFLAGS) -I$(SOURCE_DIR) $^ -o $@
```

Run:

```powershell
docker build -t helengine-ps2 C:\dev\helworks\helengine-ps2
docker run --rm `
  -v "C:\dev\helworks\helengine-ps2:/workspace:ro" `
  -v "C:\dev\helworks\builds\helengine-ps2\native-tests\task-1-red:/build-output" `
  -w /workspace helengine-ps2 `
  make ps2-rendering-tests BUILD_DIR=/build-output
```

Expected: FAIL because the three clipping production files do not exist yet.

- [ ] **Step 3: Add the fixed clipping data contracts**

Define the vertex as a pure aggregate with both coordinate systems already calculated by the caller:

```cpp
namespace helengine::ps2 {
    /// <summary>
    /// Stores one textured clipping vertex in view and homogeneous clip space without owning dynamic memory.
    /// </summary>
    struct Ps2VuTexturedClipVertex final {
        float ViewX;
        float ViewY;
        float ViewZ;
        float ClipX;
        float ClipY;
        float ClipZ;
        float ClipW;
        float TextureU;
        float TextureV;
    };
}
```

Define `Ps2VuTexturedClipPolygon` with this interface and a `std::array<Ps2VuTexturedClipVertex, 9u>` field:

```cpp
class Ps2VuTexturedClipPolygon final {
public:
    static constexpr std::size_t Capacity = 9u;

    Ps2VuTexturedClipPolygon();
    void Clear();
    void Append(const Ps2VuTexturedClipVertex& vertex);
    const Ps2VuTexturedClipVertex& GetVertex(std::size_t index) const;
    std::size_t GetVertexCount() const;

private:
    std::array<Ps2VuTexturedClipVertex, Capacity> Vertices;
    std::size_t VertexCount;
};
```

`Append` must throw `std::overflow_error` at the tenth vertex. `GetVertex` must throw `std::out_of_range`; it must not return a default vertex.

- [ ] **Step 4: Implement the five-plane Sutherland-Hodgman clipper**

Expose exactly this public entry point:

```cpp
class Ps2VuTexturedTriangleClipper final {
public:
    static void ClipTriangle(
        const Ps2VuTexturedClipVertex& vertexA,
        const Ps2VuTexturedClipVertex& vertexB,
        const Ps2VuTexturedClipVertex& vertexC,
        float nearPlaneDistance,
        Ps2VuTexturedClipPolygon& outputPolygon);
};
```

The implementation owns two local `Ps2VuTexturedClipPolygon` instances and performs passes in this exact order:

1. near distance: `-nearPlaneDistance - ViewZ`;
2. left distance: `ClipX + ClipW`;
3. right distance: `ClipW - ClipX`;
4. bottom distance: `ClipY + ClipW`;
5. top distance: `ClipW - ClipY`.

For a crossing edge, use:

```cpp
const float amount = std::clamp(
    previousDistance / (previousDistance - currentDistance),
    0.0f,
    1.0f);
```

Interpolate every view component, clip component, and raw UV with the same `amount`. Reject a non-positive or non-finite `nearPlaneDistance`. Validate every input and generated component with `std::isfinite`. A crossing denominator whose absolute value is at or below `0.0000001f` is an invariant failure and throws `std::runtime_error`; it is never silently skipped.

- [ ] **Step 5: Prove GREEN and check allocation contracts**

Run the same Docker target with output `C:\dev\helworks\builds\helengine-ps2\native-tests\task-1-green`.

Expected: PASS for every geometry and UV case.

Then run:

```powershell
rg -n "std::vector|new |malloc|realloc|push_back" `
  src/platform/ps2/rendering/vu/Ps2VuTexturedClipVertex.hpp `
  src/platform/ps2/rendering/vu/Ps2VuTexturedClipPolygon.* `
  src/platform/ps2/rendering/vu/Ps2VuTexturedTriangleClipper.*
```

Expected: no matches.

- [ ] **Step 6: Commit Task 1**

```powershell
git add -- `
  Makefile `
  tests/native/Ps2VuTexturedTriangleClipperTests.cpp `
  src/platform/ps2/rendering/vu/Ps2VuTexturedClipVertex.hpp `
  src/platform/ps2/rendering/vu/Ps2VuTexturedClipPolygon.hpp `
  src/platform/ps2/rendering/vu/Ps2VuTexturedClipPolygon.cpp `
  src/platform/ps2/rendering/vu/Ps2VuTexturedTriangleClipper.hpp `
  src/platform/ps2/rendering/vu/Ps2VuTexturedTriangleClipper.cpp
git commit -m "feat(ps2): add fixed textured triangle clipper"
```

---

### Task 2: Add fixed generated-fan and clipped-batch records

**Files:**

- Create: `src/platform/ps2/rendering/vu/Ps2VuClippedTexturedTriangleSource.hpp`
- Create: `src/platform/ps2/rendering/vu/Ps2VuClippedTexturedTriangleFan.hpp`
- Create: `src/platform/ps2/rendering/vu/Ps2VuClippedTexturedTriangleFan.cpp`
- Create: `src/platform/ps2/rendering/vu/Ps2VuClippedTexturedBatch.hpp`
- Create: `src/platform/ps2/rendering/vu/Ps2VuClippedTexturedBatch.cpp`
- Create: `src/platform/ps2/rendering/vu/Ps2VuClippedTexturedBatchBuilder.hpp`
- Create: `src/platform/ps2/rendering/vu/Ps2VuClippedTexturedBatchBuilder.cpp`
- Create: `builder.tests/Ps2VuHybridClippedBatchSourceTests.cs`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuTexturedSourceLimits.hpp`
- Modify: `tests/native/Ps2VuTexturedTriangleClipperTests.cpp`
- Modify: `Makefile`

- [ ] **Step 1: Add failing fan, memory, and flush tests**

Extend the native executable with deterministic cases that require:

- a three-vertex clipped polygon to produce one triangle;
- a four-vertex polygon to produce `(0, 1, 2)` and `(0, 2, 3)`;
- all generated fan records to retain the original source face normal unchanged;
- generated records to copy homogeneous XYZW and raw UV, not divided coordinates;
- one polygon to be appended atomically or not at all;
- filling the final available batch slot to succeed;
- the next append to report insufficient capacity without changing count or data;
- reset to clear only the logical count;
- the capacity formula to fit both the input region below qword `0x100` and the worst-case output region below qword `1024`.

Run the Task 1 Docker command against `task-2-red`.

Expected: FAIL because the generated-fan and batch classes do not exist.

- [ ] **Step 2: Define the pretransformed 7-qword source record**

Use a dedicated semantic type even though its byte size matches the immutable fast source record:

```cpp
struct alignas(16) Ps2VuClippedTexturedTriangleSource final {
    float ClipPositionA[4];
    float ClipPositionB[4];
    float ClipPositionC[4];
    float TexCoordA[4];
    float TexCoordB[4];
    float TexCoordC[4];
    float FaceNormal[4];
};

static_assert(sizeof(Ps2VuClippedTexturedTriangleSource) == 7u * 16u);
```

The UV qwords use X and Y only; initialize Z and W to `0.0f` so DMA payloads remain deterministic. Copy all four packed face-normal components from the source triangle.

- [ ] **Step 3: Derive clipped capacity from VU1 memory**

In `Ps2VuTexturedSourceLimits.hpp`, retain the existing fast constants and add:

```cpp
constexpr std::size_t TexturedVuSharedStateQwordCount = 21u;
constexpr std::size_t TexturedVuClippedTriangleSourceQwordCount = 7u;
constexpr std::size_t TexturedVuClippedInputTriangleCapacity =
    (TexturedVuOutputStartQword - TexturedVuSharedStateQwordCount)
    / TexturedVuClippedTriangleSourceQwordCount;
constexpr std::size_t TexturedVuClippedOutputTriangleCapacity =
    (TexturedVuDataMemoryQwordCount - TexturedVuOutputStartQword - TexturedVuGifStateQwordCount)
    / TexturedVuOutputQwordsPerTriangle;
constexpr std::size_t TexturedVuClippedTriangleCapacity =
    TexturedVuClippedInputTriangleCapacity < TexturedVuClippedOutputTriangleCapacity
        ? TexturedVuClippedInputTriangleCapacity
        : TexturedVuClippedOutputTriangleCapacity;
```

Add static assertions proving:

```cpp
static_assert(TexturedVuClippedTriangleCapacity == 33u);
static_assert(TexturedVuSharedStateQwordCount
    + (TexturedVuClippedTriangleCapacity * TexturedVuClippedTriangleSourceQwordCount)
    <= TexturedVuOutputStartQword);
static_assert(TexturedVuOutputStartQword + TexturedVuGifStateQwordCount
    + (TexturedVuClippedTriangleCapacity * TexturedVuOutputQwordsPerTriangle)
    <= TexturedVuDataMemoryQwordCount);
```

Remove the old eight-source/full-frustum expansion constants only after all references move off them in Task 6.

- [ ] **Step 4: Implement the fixed fan and batch classes**

`Ps2VuClippedTexturedTriangleFan` owns an array of seven records because a nine-vertex polygon yields at most seven fan triangles. It exposes `Clear`, `BuildFromClippedPolygon`, `GetTriangle`, and `GetTriangleCount`; overflow and invalid indices throw. `BuildFromClippedPolygon` is dependency-free, accepts the clipped polygon plus a four-float source normal, and builds stable `(0, index, index + 1)` records. This is the native-tested owner of homogeneous-position, raw-UV, fan-order, and flat-normal copying.

`Ps2VuClippedTexturedBatch` owns an array of `TexturedVuClippedTriangleCapacity` records and exposes:

```cpp
void Clear();
bool CanAppend(std::size_t triangleCount) const;
void Append(const Ps2VuClippedTexturedTriangleSource& triangle);
void Append(const Ps2VuClippedTexturedTriangleFan& fan);
const Ps2VuClippedTexturedTriangleSource* GetTriangles() const;
std::size_t GetTriangleCount() const;
```

`Append(fan)` first validates the full fan count and then copies; it must never partially append.

- [ ] **Step 5: Implement source-to-fan conversion**

Expose this builder interface:

```cpp
class Ps2VuClippedTexturedBatchBuilder final {
public:
    static void BuildTriangleFan(
        const Ps2VuTexturedPackedTriangleSource& sourceTriangle,
        const ::float4x4& worldView,
        const ::float4x4& projection,
        float nearPlaneDistance,
        Ps2VuClippedTexturedTriangleFan& outputFan);
};
```

Implementation sequence:

1. transform each local source position to view space;
2. project each view position to homogeneous clip XYZW without dividing;
3. create three `Ps2VuTexturedClipVertex` values with raw source UV;
4. call `Ps2VuTexturedTriangleClipper::ClipTriangle`;
5. return an empty fan if fewer than three vertices survive;
6. validate every surviving `ClipW` is finite and greater than `0.0001f`;
7. call `outputFan.BuildFromClippedPolygon(...)` with the original packed normal.

Do not normalize or recalculate the normal. The VU1 program must receive the same flat-lighting input as the fast path.

- [ ] **Step 6: Add the source contract for matrix-to-clip integration**

Create `Ps2VuHybridClippedBatchSourceTests.cs` with a test that requires `Ps2VuClippedTexturedBatchBuilder` to transform local positions to view and homogeneous clip space, call `Ps2VuTexturedTriangleClipper::ClipTriangle`, reject invalid W, and delegate stable fan generation to `BuildFromClippedPolygon`. It must also require that the builder source contains no `std::vector`, `new`, `malloc`, or `realloc`.

- [ ] **Step 7: Prove GREEN and verify production compilation inputs**

Add all three new Task 2 `.cpp` files to `PS2_SOURCES`. Add only the dependency-free fan and batch `.cpp` files to the native test target; `Ps2VuClippedTexturedBatchBuilder.cpp` depends on generated engine math headers and is covered by the source contract plus the native PS2 build. Run `ps2-rendering-tests` against `task-2-green`.

Expected: PASS, including capacity exhaustion without truncation.

Run the relevant source tests:

```powershell
dotnet test builder.tests\helengine.ps2.builder.tests.csproj `
  -p:HelengineRoot=C:\dev\helworks\helengine `
  --filter "FullyQualifiedName~Ps2TexturedVuSourceCapacityTests|FullyQualifiedName~Ps2VuFullFrustumClippingSourceTests" `
  -v minimal
```

Expected at this checkpoint: existing capacity tests pass or fail only where they still assert the old eight-source clipping layout; update those assertions in Task 6 when the old program is removed.

- [ ] **Step 8: Commit Task 2**

Stage only the files named in this task and commit:

```powershell
git commit -m "feat(ps2): build bounded pretransformed clip batches"
```

---

### Task 3: Add the small pretransformed VU1 draw program

**Files:**

- Create: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedPretransformedDraw3D.vsm`
- Modify: `builder.tests/Ps2VuHybridClippedBatchSourceTests.cs`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuMicroProgramAddresses.hpp`

- [ ] **Step 1: Add failing microprogram and registration contracts**

Create `Ps2VuHybridClippedBatchSourceTests.cs` with tests that require:

- symbol names `Ps2OpaqueTexturedPretransformedDraw3D_CodeStart` and `CodeEnd`;
- reservation of `TexturedPretransformedMicroProgramAddress` without an active boot upload until Task 4;
- no matrix multiply of source positions in the new VSM;
- no polygon-plane loop, `clipw`, `fcand`, or scratch polygon addresses;
- reciprocal W before both STQ and XYZ projection;
- raw UV multiplied by Q;
- Q stored in the RGBAQ W component;
- the same world-normal/light flat-diffuse sequence as the fast VSM;
- the same signed-area winding decision and double-sided bypass as the fast VSM;
- output cursor advancement only for accepted triangles;
- dynamic GIF NLOOP patching from accepted triangles;
- exactly one `xgkick`, guarded so zero accepted triangles do not kick.

Run:

```powershell
dotnet test builder.tests\helengine.ps2.builder.tests.csproj `
  -p:HelengineRoot=C:\dev\helworks\helengine `
  --filter "FullyQualifiedName~Ps2VuHybridClippedBatchSourceTests" `
  -v minimal
```

Expected: FAIL because the program and upload symbols do not exist.

- [ ] **Step 2: Implement the VU1 input contract**

Reuse the existing 21-qword `Ps2VuTexturedSharedState` layout so packet state construction and lighting inputs remain identical:

- qwords `0..20`: existing shared state;
- source begins at `XTOP + 0x15`;
- each source record advances by seven qwords;
- GIF state is copied from shared-state qwords `13..20` to absolute qword `0x100`;
- generated GIF vertices start at qword `0x108`.

For every source triangle:

1. load three homogeneous clip positions, three raw UVs, and the source normal;
2. compute flat lighting exactly once from the source normal;
3. calculate `Q = 1 / W` separately for A, B, and C;
4. write `(U * Q, V * Q)` to ST and write `Q` into the corresponding RGBAQ W lane;
5. divide clip XYZ by W, apply the existing GS scale and offset, and convert final XY with `ftoi4` and Z with `ftoi0`;
6. apply the fast program's winding test unless the shared double-sided flag is set;
7. compact accepted records at the output cursor.

After the loop, return without `XGKICK` when accepted count is zero. Otherwise patch NLOOP to `acceptedTriangleCount * 3`, restore EOP, and issue the file's only `xgkick`.

- [ ] **Step 3: Reserve the replacement entry point without switching active routing yet**

Change the address constant to:

```cpp
constexpr std::uint16_t TexturedPretransformedMicroProgramAddress = 320u;
```

Keep the existing `TexturedClipMicroProgramAddress` temporarily so the Task 3 commit remains buildable while `Ps2VuVifPacketBuilder` still routes to the old program. Do not add the new VSM to the production Makefile or boot upload yet: both programs target address 320 and must never be uploaded together. Task 4 performs the routing, Makefile, and upload switch atomically.

- [ ] **Step 4: Prove source contracts GREEN**

Run the hybrid test plus the adjusted native-input and upload tests.

Expected: PASS for the new program's source and reserved address contract. Existing old-program upload tests remain green at this checkpoint.

- [ ] **Step 5: Assemble the new VSM directly**

```powershell
docker run --rm `
  -v "C:\dev\helworks\helengine-ps2:/workspace:ro" `
  -v "C:\dev\helworks\builds\helengine-ps2\vsm\task-3:/build-output" `
  -w /workspace helengine-ps2 `
  dvp-as src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedPretransformedDraw3D.vsm `
    -o /build-output/Ps2OpaqueTexturedPretransformedDraw3D.o
```

Expected: exit code 0 and a non-empty object in the visible build directory.

- [ ] **Step 6: Commit Task 3**

Stage only the new VSM, registration files, and named tests. Commit:

```powershell
git commit -m "feat(ps2): add pretransformed textured VU program"
```

---

### Task 4: Route intersecting triangles into coherent clipped batches

**Files:**

- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuTexturedSourceLimits.hpp`
- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Modify: `Makefile`
- Modify: `builder.tests/Ps2VuHybridClippedBatchSourceTests.cs`
- Modify: `builder.tests/Ps2VuTriangleRefinedClippingSourceTests.cs`
- Modify: `builder.tests/Ps2TexturedVuReferencePayloadSourceTests.cs`
- Modify: `builder.tests/Ps2NativeBuildInputsTests.cs`
- Modify: `builder.tests/Ps2VuNearPlaneClippingSourceTests.cs`

- [ ] **Step 1: Add failing routing and packet-budget tests**

Add source-contract tests that require all of the following:

- a fully safe outer slice still emits one immutable REF/UNPACK submission to `TexturedMicroProgramAddress`;
- an intersecting outer slice is refined one source triangle at a time;
- contiguous refined `Fast` triangles are coalesced into immutable fast runs instead of one MSCAL per triangle;
- refined `Rejected` triangles add no source record and no MSCAL;
- refined `Clipped` triangles call `Ps2VuClippedTexturedBatchBuilder::BuildTriangleFan` and never call the old clip microprogram;
- generated fan records use copied UNPACK data because they are frame-local, not immutable REF sources;
- a clipped batch flushes before overflow and once at the material/texture batch boundary;
- batch exhaustion never truncates a fan;
- the safe-only packet keeps `MaximumTexturedVuSourcePacketQwords = 2048u`;
- a preclassified packet containing an intersecting outer slice uses `MaximumTexturedVuExceptionalPacketQwords = 4096u`;
- outer routes and world-view/world-view-projection matrices are cached in fixed arrays during the allocation prepass, so safe geometry is not classified or multiplied twice.

Run the three named test classes.

Expected: FAIL because clipped triangles still dispatch the six-pass VU clip program.

- [ ] **Step 2: Add bounded route preplanning without changing the fast budget**

Introduce these local compile-time packet constants next to the current packet limits:

```cpp
constexpr std::uint16_t MaximumTexturedVuSourcePacketQwords = 2048u;
constexpr std::uint16_t MaximumTexturedVuExceptionalPacketQwords = 4096u;
constexpr std::size_t MaximumTexturedVuSourceBatchCount =
    (MaximumTexturedVuSourcePacketQwords - MinimumVifPacketOverheadQwords)
    / TexturedVuMaximumSourceBatchPacketQwordCount;
```

Before creating `packet2_t`, classify the at-most-`MaximumTexturedVuSourceBatchCount` outer slices into a fixed `std::array<Ps2VuNearPlaneRoute, MaximumTexturedVuSourceBatchCount>`. Cache the matching world-view and world-view-projection matrices in fixed arrays. Select 2,048 qwords if no route is `Clipped`; select 4,096 only if at least one outer route is `Clipped`.

Add a compile-time worst-case proof that two outer source batches, each containing 32 source triangles and each expanding to seven generated triangles, fit the 4,096-qword packet after all shared-state and submission overhead.

- [ ] **Step 3: Preserve the fast and rejected routes**

For an outer `Fast` route, keep the existing immutable source pointer, REF/UNPACK, 21-qword shared state, GIF count, and `TexturedMicroProgramAddress` call unchanged.

For an outer `Rejected` route, add its source triangle count to the rejected-source counter and continue without source transport.

Do not copy safe sources into the new clipped batch.

- [ ] **Step 4: Refine and coalesce an intersecting outer slice**

For each source triangle in an intersecting outer slice:

- classify through `GetTexturedSourceTriangleBounds` and the cached WVP;
- extend the current contiguous fast run for `Fast`;
- omit `Rejected`;
- flush any pending fast run before handling `Clipped`;
- call `BuildTriangleFan` for `Clipped` using the cached world-view matrix, projection, and `nearPlaneDistance`;
- flush the current clipped batch if `CanAppend(fanCount)` is false, then append the complete fan;
- throw if one fan cannot fit an empty clipped batch, because seven must fit the derived capacity of 33.

After the source loop, emit the final fast run and the final clipped batch. The containing `Ps2VuOpaqueBatchSlice` already guarantees one material, texture, GS context, and double-sided state, so flushing at its boundary enforces the required state coherence without a second hash map.

- [ ] **Step 5: Emit a generated clipped submission**

For each non-empty clipped batch:

- copy the cached 21-qword shared state;
- set `TriangleCount[0]` to generated triangle count;
- retain `TriangleCount[1]` as the material's double-sided flag;
- patch GIF NLOOP to `generatedTriangleCount * 3`;
- copy-UNPACK the fixed generated records immediately after shared state;
- `FLUSH` and `MSCAL TexturedPretransformedMicroProgramAddress` once;
- increment generated-triangle and clipped-batch aggregate counters.

Do not issue one MSCAL per generated triangle.

- [ ] **Step 6: Atomically switch active program assembly, upload, and routing**

Replace the old clipping VSM with `Ps2OpaqueTexturedPretransformedDraw3D.vsm` in `VU_PROGRAM_SOURCES` and `VU_PROGRAM_OBJECTS`. In `Ps2BootHost.cpp`, replace the old clipping externs, byte-count checks, packet sizing, and upload call with the pretransformed symbols and `TexturedPretransformedMicroProgramAddress`. Update native-input and upload tests in the same change.

After this step, the old VSM remains only as inactive source for comparison until Task 6. It is not assembled, linked, uploaded, or routed.

- [ ] **Step 7: Remove runtime diagnostic branches from routing**

Delete these constants and every branch that depends on them:

```cpp
DropClippedTexturedSlicesForDiagnostics
UseFastProgramForClippedSliceDiagnostics
ForceAllTexturedSlicesThroughClipProgramDiagnostics
```

Retain aggregate counters. Keep `EnableVuPerTriangleTimingDiagnostics = false` until Task 6 removes the dead timer branch entirely.

- [ ] **Step 8: Prove GREEN**

Run:

```powershell
dotnet test builder.tests\helengine.ps2.builder.tests.csproj `
  -p:HelengineRoot=C:\dev\helworks\helengine `
  --filter "FullyQualifiedName~Ps2VuHybridClippedBatchSourceTests|FullyQualifiedName~Ps2VuTriangleRefinedClippingSourceTests|FullyQualifiedName~Ps2TexturedVuReferencePayloadSourceTests" `
  -v minimal
```

Then run `ps2-rendering-tests` again.

Expected: all selected tests pass; `rg -n "TexturedClipMicroProgramAddress|useClippingMicroProgram" src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp` returns no matches.

- [ ] **Step 9: Commit Task 4**

```powershell
git commit -m "feat(ps2): batch exceptional clipped triangles"
```

---

### Task 5: Publish aggregate hybrid-path metrics without hot-loop timing

**Files:**

- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.hpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Modify: `builder.tests/Ps2VuHybridClippedBatchSourceTests.cs`
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Add failing counter-semantics tests**

Require these exact frame aggregates at packet-builder and render-manager boundaries:

```cpp
GetFastTexturedSourceTriangleCount()
GetClippedTexturedSourceTriangleCount()
GetRejectedTexturedSourceTriangleCount()
GetGeneratedClippedTexturedTriangleCount()
GetClippedTexturedBatchCount()
```

The tests must require counters to reset once per packet/frame, aggregate across renderer packet submissions, and contain no `std::clock()` call in the triangle-refinement range.

Require the compact overlay text:

```text
F <fast> C <clipped-source> R <rejected-source> G <generated> CB <clipped-batches>
```

Require `FrameTimingOverlayBuildNumber = "B321"`.

- [ ] **Step 2: Replace ambiguous slice counters with source-triangle counters**

Rename the current `FastTexturedSliceCount`, `ClippedTexturedSliceCount`, and `RejectedTexturedSliceCount` fields and getters through both classes. Increment by source triangle count, not dispatch count. Add generated and clipped-batch counters.

Keep `SubmittedTriangleCount` as the count actually sent to VU1: safe source triangles plus generated clipped fan triangles. Rejected and original clipped source triangles are not submitted counts.

- [ ] **Step 3: Update the profiler-only overlay**

Replace `Fast/Clip/Rej` wording with the compact `F/C/R/G/CB` string. Do not add a fourth line. Keep the existing build, physics, 3D, and 2D timing lines unchanged. This diagnostic text remains visible only where the authored FPS/profiler component requests it; do not force it into normal DemoDisc scenes.

- [ ] **Step 4: Prove GREEN and commit**

Run the hybrid and renderer source tests. Use `rg -n "std::clock" src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp` and inspect the bounded context to prove no clock read occurs in the refined triangle loop.

Commit:

```powershell
git commit -m "feat(ps2): report hybrid clipping aggregates"
```

---

### Task 6: Prove the failed full-frustum VU clipper is inactive and remove its diagnostics

**Files:**

- Retain for B321 comparison only: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedClipDraw3D.vsm`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuTexturedSourceLimits.hpp`
- Modify: `builder.tests/Ps2VuNearPlaneClippingSourceTests.cs`
- Modify: `builder.tests/Ps2VuFullFrustumClippingSourceTests.cs`
- Modify: `builder.tests/Ps2NativeBuildInputsTests.cs`
- Modify: `builder.tests/Ps2VuHybridClippedBatchSourceTests.cs`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`

- [ ] **Step 1: Add the failing inactive-path contract**

Require that:

- no Makefile entry, extern, upload, address, or route references `Ps2OpaqueTexturedClipDraw3D`;
- no clipping scratch qword constants remain;
- no eight-source full-frustum capacity remains;
- no B318-B320 diagnostic bypass constants remain;
- `EnableVuPerTriangleTimingDiagnostics` and its dead branches are absent;
- the only active exceptional textured VU program is `Ps2OpaqueTexturedPretransformedDraw3D.vsm`;
- the old VSM source is allowed to remain only as an inactive comparison artifact until B321 receives visual acceptance.

Expected: FAIL while old upload/routing assertions and diagnostic constants remain.

- [ ] **Step 2: Convert tests to the new architecture while retaining the comparison source**

Replace tests that parsed the old VSM's internal six-pass labels with contracts for:

- the five-plane host clipper;
- the 9-vertex fixed buffers;
- the 33-triangle generated batch capacity;
- the pretransformed program's regular loop;
- active build/upload/routing registration.

Do not weaken the behavioral requirements; move them to their new owner. Do not delete `Ps2OpaqueTexturedClipDraw3D.vsm` before the user accepts B321.

- [ ] **Step 3: Remove obsolete constants and dead timing code**

Remove camera-W scratch-buffer, clipped-source-capacity-eight, maximum-expanded-output, and full-frustum VU polygon constants that have no active references. Retain the shared output-region constants used by both fast and pretransformed programs.

- [ ] **Step 4: Run the focused and full builder suites**

Run the native clipping tests, all `Ps2Vu*Clipping*` C# tests, `Ps2NativeBuildInputsTests`, and then the full `builder.tests` project.

Expected: PASS with no skipped cleanup assertions.

- [ ] **Step 5: Commit Task 6**

```powershell
git commit -m "refactor(ps2): retire active per-triangle VU clipping"
```

---

### Task 7: Build B321 and validate the deterministic Tilt render scene

**Files:**

- Modify: `src/platform/ps2/Ps2BootHost.cpp` only if `B321` was not already set in Task 5
- Test project: `C:\dev\helprojs\demodisc`
- Output: `C:\dev\helworks\builds\helengine-ps2\ps2\B321-hybrid-clip`

- [ ] **Step 1: Verify source and tests before the expensive cook**

Run:

```powershell
git status --short
dotnet test builder.tests\helengine.ps2.builder.tests.csproj `
  -p:HelengineRoot=C:\dev\helworks\helengine `
  -v minimal
```

Expected: tests pass. Dirty files outside this plan remain untouched and are recorded before building.

- [ ] **Step 2: Select only the deterministic render scene for this build**

In the DemoDisc PS2 entry of `user_settings/build_config.json`, temporarily set both `selectedSceneIds` and `sceneOrders` to only:

```json
"test_scene_tilt_trial_level_01_render"
```

Preserve the exact prior PS2 entry and restore it with `apply_patch` immediately after the ISO is produced. Do not commit this temporary project selection.

- [ ] **Step 3: Build through the deterministic waiter**

```powershell
dotnet run --project C:\dev\helworks\helengine\tools\build-waiter\helengine.buildwaiter.csproj -- `
  --output C:\dev\helworks\builds\helengine-ps2\ps2\B321-hybrid-clip `
  --require game.iso `
  --require disc/SYSTEM.CNF `
  --require disc/HELENGIN.ELF `
  -- powershell -NoProfile -ExecutionPolicy Bypass `
  -File C:\dev\helworks\helengine\scripts\build-platform.ps1 `
  -Project C:\dev\helprojs\demodisc\project.heproj `
  -Platform ps2 `
  -Configuration Debug `
  -BuildProfile ps2-default `
  -Output C:\dev\helworks\builds\helengine-ps2\ps2\B321-hybrid-clip
```

Expected: the waiter returns only after a fresh, non-empty ISO and disc boot contract exist. Do not poll manually and do not impose an arbitrary timeout.

- [ ] **Step 4: Restore DemoDisc's exact previous PS2 scene selection**

Use `apply_patch` to restore the captured PS2 JSON entry. Confirm `git -C C:\dev\helprojs\demodisc diff -- user_settings/build_config.json` shows only changes that existed before this task.

- [ ] **Step 5: Launch the exact B321 ISO once**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File C:\dev\helworks\helengine-ps2\scripts\launch_in_emulator.ps1 `
  -ArtifactPath C:\dev\helworks\builds\helengine-ps2\ps2\B321-hybrid-clip\game.iso
```

Expected: one PCSX2 instance launches the exact B321 artifact and boots directly into the Level 1 render test.

- [ ] **Step 6: Record the safe-view baseline**

Use HelenUI OCR for the compact overlay. Record `B321`, 3D time, `F/C/R/G/CB`, submitted triangle count, VIF packet bytes, and dispatch count in:

`C:\dev\helworks\builds\helengine-ps2\ps2\B321-hybrid-clip\validation.md`

Acceptance: safe-view 3D time is no more than 0.2 ms above the immediately preceding fast baseline and `C/G/CB` are zero when nothing intersects.

- [ ] **Step 7: Ask the user for intersection feedback**

Ask the user to move the camera into the deterministic large cube and across a side edge. The required visual result is:

- no vertex explosion;
- no full-face or full-object disappearance at first contact;
- no flashing;
- no affine texture warping;
- no new double-sided faces;
- clipped faces remain continuous as the camera crosses near and side boundaries.

Use HelenUI only for metric text. Do not infer the visual result from OCR.

- [ ] **Step 8: Record intersection performance**

Acceptance: the intersection view adds approximately 0.5 ms, does not halve frame rate, and increments `C`, `G`, and `CB` consistently. `CB` must be far lower than `G`, proving batching rather than per-triangle dispatch.

If the visual test fails, stop rollout and debug only the owner identified by the symptom:

- wrong intersection/shape: host clipper or fan builder;
- correct shape with wrong UV: STQ/Q output in pretransformed VSM;
- wrong lighting: normal/shared-state offsets in pretransformed VSM;
- missing backs or double-sided regression: winding sign/double-sided flag in pretransformed VSM;
- stable image with excessive cost: clipped batch count, packet bytes, or exceptional packet allocation.

Do not modify the fast VSM as a speculative response.

---

### Task 8: Clean B322 rollout, full build, and final commit

**Files:**

- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Delete: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedClipDraw3D.vsm`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuTexturedSourceLimits.hpp`
- Modify: `builder.tests/Ps2VuNearPlaneClippingSourceTests.cs`
- Modify: `builder.tests/Ps2VuFullFrustumClippingSourceTests.cs`
- Modify: `builder.tests/Ps2NativeBuildInputsTests.cs`
- Modify: `builder.tests/Ps2VuHybridClippedBatchSourceTests.cs`
- Modify: `GRAPHICS.md` only if its active clipping description still documents the removed six-pass VU program
- Test project: `C:\dev\helprojs\demodisc`
- Output: `C:\dev\helworks\builds\helengine-ps2\ps2\B322-full-demodisc`

- [ ] **Step 1: Delete the inactive old VSM only after B321 visual acceptance**

After the user confirms B321 intersection continuity, delete `Ps2OpaqueTexturedClipDraw3D.vsm`. Tighten cleanup tests so the old name has no source, Makefile, boot-upload, address, route, or test references. Run the focused clipping and native-input tests before continuing.

- [ ] **Step 2: Apply only evidence-backed B321 corrections**

Address any B321 defect in the owning class. Add or tighten a native/source regression test first, prove RED, make the minimum change, and prove GREEN. Do not add diagnostic route switches.

- [ ] **Step 3: Update final build identity and documentation**

Set `FrameTimingOverlayBuildNumber` to `B322`. Update `GRAPHICS.md` to state that textured opaque intersection clipping is EE fixed-buffer clipping plus a batched pretransformed VU1 draw, while preserving the existing rules that affine warping, vertex explosion, unintended double-sided rendering, and geometry truncation are unacceptable.

- [ ] **Step 4: Run complete automated verification**

Run:

- `ps2-rendering-tests` in Docker;
- full `builder.tests`;
- direct assembly of all three active VSM files;
- a native PS2 build through the normal export path.

Expected: every command exits zero. Search the repository for `Ps2OpaqueTexturedClipDraw3D`, `TexturedClipMicroProgramAddress`, `DropClippedTexturedSlicesForDiagnostics`, `UseFastProgramForClippedSliceDiagnostics`, and `ForceAllTexturedSlicesThroughClipProgramDiagnostics`; expect no production matches.

- [ ] **Step 5: Build the full DemoDisc with the deterministic waiter**

Use the restored full PS2 scene selection and the same waiter command, changing only output to:

`C:\dev\helworks\builds\helengine-ps2\ps2\B322-full-demodisc`

Expected: fresh `game.iso`, `SYSTEM.CNF`, and `HELENGIN.ELF` all exist and are non-empty.

- [ ] **Step 6: Launch B322 and let the user navigate**

Launch with `scripts/launch_in_emulator.ps1`. Do not automate menu navigation. Ask the user to exercise:

- colored cubes;
- textured cubes;
- Stacked Boxes;
- Stacked Spheres;
- Tilt render test;
- Tilt Play Scene 01.

Acceptance: no boot regression, no lost ground meshes, no clipping explosion/disappearance, no unexpected double-sided geometry, and no profiler text in normal scenes that do not author it.

- [ ] **Step 7: Compare performance and packet behavior**

Record safe and intersection metrics. Final acceptance requires:

- safe-view regression at or below 0.2 ms;
- isolated intersection overhead near 0.5 ms and no half-rate collapse;
- one clipped MSCAL/XGKICK per bounded clipped batch rather than per generated triangle;
- no clipped-batch overflow or dropped geometry;
- stable perspective-correct textures.

- [ ] **Step 8: Commit final rollout**

Review `git diff --check`, run `git status --short`, and stage only plan-owned files. Commit:

```powershell
git commit -m "fix(ps2): clip textured camera intersections in batches"
```

- [ ] **Step 9: Report the evidence**

Report commit hashes, B321/B322 artifact paths, automated test results, safe/intersection 3D timings, `F/C/R/G/CB` values, and the user's visual verdict. Do not claim success if the user has not confirmed the intersection behavior.
