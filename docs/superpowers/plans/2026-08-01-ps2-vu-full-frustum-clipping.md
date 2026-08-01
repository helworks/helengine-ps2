# PS2 VU1 Full-Frustum Textured Clipping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the PS2 textured VU1 triangle-drop diagnostic with bounded geometric clipping against camera, near, left, right, bottom, and top boundaries while preserving the current fast path.

**Architecture:** The EE continues to classify conservative source-slice bounds and dispatches fully safe slices to the unchanged fast microprogram. Intersecting eight-triangle submissions use a dedicated VU1 Sutherland-Hodgman clipper with two fixed scratch buffers, perspective-correct STQ generation, post-projection winding rejection, and compact GIF output.

**Tech Stack:** C++20, PS2SDK, VIF1/VU1 micro assembly, gsKit/GIF, xUnit source-contract tests, Docker PS2 toolchain, PCSX2, HelenUI OCR.

---

## Working Constraints

- Work directly on `main`; do not create a worktree.
- Preserve unrelated dirty files and stage only task-owned paths.
- Use `apply_patch` for edits.
- Keep build inputs, logs, test results, and ISOs under `C:\dev\helworks\builds`, never `%TEMP%`.
- Use the build waiter for every ISO and `scripts\launch_in_emulator.ps1` for PCSX2.
- Do not inspect screenshots manually. Use HelenUI OCR for telemetry and ask the user for visual clipping feedback.
- Stop after a failed visual hypothesis instead of stacking another renderer change on top.

## File Map

- Create `builder.tests/Ps2VuFullFrustumClippingSourceTests.cs`: focused structural contracts for production routing, scratch initialization, all six safety boundaries, clipping-before-division, and compact fan emission.
- Modify `builder.tests/Ps2VuNearPlaneClippingSourceTests.cs`: replace diagnostic triangle-drop expectations with final clipping expectations and update shared memory limits.
- Modify `src/platform/ps2/rendering/vu/Ps2VuTexturedSourceLimits.hpp`: prove nine-vertex polygon, scratch, input, and output bounds.
- Modify `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedClipDraw3D.vsm`: implement scratch seeding, six-boundary clipping, edge interpolation, fan emission, and safe projection.
- Modify `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`: restore normal route classification and retain bounded clipped submissions.
- Modify `src/platform/ps2/Ps2BootHost.cpp`: advance the profiling build identifier without enabling the overlay for ordinary builds.
- Modify `GRAPHICS.md` only if validation reveals an invariant that is not already stated; no documentation churn is otherwise required.

### Task 1: Lock the Final Clipping Contract

**Files:**
- Create: `builder.tests/Ps2VuFullFrustumClippingSourceTests.cs`
- Modify: `builder.tests/Ps2VuNearPlaneClippingSourceTests.cs`

- [ ] **Step 1: Add a failing production-routing test**

Create one public test class in the new file with substantive XML comments. The first test reads `Ps2VuVifPacketBuilder.cpp` and requires production routing:

```csharp
[Fact]
public void Ps2TexturedVuRouting_WhenProductionClippingIsEnabled_UsesBoundsInsteadOfDiagnosticForcing() {
    string source = File.ReadAllText(Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

    Assert.Contains("Ps2VuNearPlaneSliceClassifier::Classify", source, StringComparison.Ordinal);
    Assert.Contains("constexpr bool ForceAllTexturedSlicesThroughClipProgramDiagnostics = false;", source, StringComparison.Ordinal);
    Assert.Contains("constexpr bool DropClippedTexturedSlicesForDiagnostics = false;", source, StringComparison.Ordinal);
    Assert.Contains("constexpr bool UseFastProgramForClippedSliceDiagnostics = false;", source, StringComparison.Ordinal);
}
```

Add `GetRepositoryRootPath()` using the existing `AppContext.BaseDirectory` pattern.

- [ ] **Step 2: Add failing VU data-flow tests**

Add tests that require these final labels and ordering:

```csharp
[Fact]
public void Ps2TexturedClipProgram_WhenAPlaneIntersects_SeedsClipsAndTriangulatesBeforeDivision() {
    string source = ReadClipProgram();
    int seedIndex = source.IndexOf("texturedClipSeedPolygon:", StringComparison.Ordinal);
    int cameraIndex = source.IndexOf("texturedClipPlaneCameraW:", StringComparison.Ordinal);
    int nearIndex = source.IndexOf("texturedClipPlaneNearZ:", StringComparison.Ordinal);
    int leftIndex = source.IndexOf("texturedClipPlaneLeft:", StringComparison.Ordinal);
    int rightIndex = source.IndexOf("texturedClipPlaneRight:", StringComparison.Ordinal);
    int bottomIndex = source.IndexOf("texturedClipPlaneBottom:", StringComparison.Ordinal);
    int topIndex = source.IndexOf("texturedClipPlaneTop:", StringComparison.Ordinal);
    int fanIndex = source.IndexOf("texturedClipEmitTriangleFanLoop:", StringComparison.Ordinal);
    int divisionIndex = source.IndexOf("div           Q", StringComparison.Ordinal);

    Assert.True(seedIndex >= 0);
    Assert.True(cameraIndex > seedIndex);
    Assert.True(nearIndex > cameraIndex);
    Assert.True(leftIndex > nearIndex);
    Assert.True(rightIndex > leftIndex);
    Assert.True(bottomIndex > rightIndex);
    Assert.True(topIndex > bottomIndex);
    Assert.True(fanIndex > topIndex);
    Assert.True(divisionIndex > fanIndex);
    Assert.DoesNotContain("texturedClipHardwareRejectTriangle:", source, StringComparison.Ordinal);
}
```

Add a second test requiring `texturedClipIntersectEdge`, position and UV interpolation, all boundary-snap labels, `texturedClipValidateTriangleA/B/C`, dynamic `NLOOP`, and `xgkick VI04`.

- [ ] **Step 3: Run the focused tests and verify RED**

Run:

```powershell
$Output = & dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2VuFullFrustumClippingSourceTests|FullyQualifiedName~Ps2VuNearPlaneClippingSourceTests" --no-restore --results-directory C:\dev\helworks\builds\helengine-ps2\test-results\full-clip-task-1-red 2>&1
$ExitCode = $LASTEXITCODE
$Output | Select-Object -Last 100
exit $ExitCode
```

Expected: FAIL because diagnostic forcing is true and the final seed/plane labels are absent.

- [ ] **Step 4: Commit only the failing contracts**

```powershell
git add -- builder.tests/Ps2VuFullFrustumClippingSourceTests.cs builder.tests/Ps2VuNearPlaneClippingSourceTests.cs
git diff --cached --check
git commit -m "test(ps2): define full-frustum VU clipping contract"
```

### Task 2: Prove VU1 Scratch and Output Capacity

**Files:**
- Modify: `src/platform/ps2/rendering/vu/Ps2VuTexturedSourceLimits.hpp`
- Modify: `builder.tests/Ps2VuFullFrustumClippingSourceTests.cs`
- Modify: `builder.tests/Ps2VuNearPlaneClippingSourceTests.cs`

- [ ] **Step 1: Add failing exact-capacity assertions**

Require these declarations from `Ps2VuTexturedSourceLimits.hpp`:

```csharp
Assert.Contains("TexturedVuMaximumClipPolygonVertexCount = 9u", source, StringComparison.Ordinal);
Assert.Contains("TexturedVuClipScratchQwordsPerVertex = 2u", source, StringComparison.Ordinal);
Assert.Contains("TexturedVuClipScratchBufferAQword = 0x50u", source, StringComparison.Ordinal);
Assert.Contains("TexturedVuClipScratchBufferBQword = 0x64u", source, StringComparison.Ordinal);
Assert.Contains("TexturedVuClipScratchEndQword <= TexturedVuOutputStartQword", source, StringComparison.Ordinal);
Assert.Contains("TexturedVuMaximumOutputEndQword <= TexturedVuDataMemoryQwordCount", source, StringComparison.Ordinal);
```

- [ ] **Step 2: Run the capacity test and verify RED**

Run the Task 1 command with `--filter FullyQualifiedName~Ps2VuFullFrustumClippingSourceTests`.

Expected: FAIL because the current polygon capacity is eight and scratch addresses are not shared constants.

- [ ] **Step 3: Update the shared limits**

Add these constants and assertions:

```cpp
constexpr std::size_t TexturedVuMaximumClipPolygonVertexCount = 9u;
constexpr std::size_t TexturedVuClipScratchQwordsPerVertex = 2u;
constexpr std::size_t TexturedVuClipScratchBufferAQword = 0x50u;
constexpr std::size_t TexturedVuClipScratchBufferBQword = 0x64u;
constexpr std::size_t TexturedVuClipScratchBufferQwordCount = TexturedVuMaximumClipPolygonVertexCount
    * TexturedVuClipScratchQwordsPerVertex;
constexpr std::size_t TexturedVuClipScratchEndQword = TexturedVuClipScratchBufferBQword
    + TexturedVuClipScratchBufferQwordCount;
constexpr std::size_t TexturedVuMaximumOutputTrianglesPerClippedSource = TexturedVuMaximumClipPolygonVertexCount - 2u;

static_assert(TexturedVuClipScratchBufferAQword + TexturedVuClipScratchBufferQwordCount <= TexturedVuClipScratchBufferBQword);
static_assert(TexturedVuClipScratchEndQword <= TexturedVuOutputStartQword);
static_assert(TexturedVuMaximumOutputEndQword <= TexturedVuDataMemoryQwordCount);
```

Keep `TexturedVuClippedSourceTriangleCapacity = 8u`; nine polygon vertices produce at most seven fan triangles, so eight sources produce 56 output triangles and end at qword `0x300`, below the 1024-qword limit.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Task 1 focused command.

Expected: capacity tests pass; clipping-flow tests remain red until the VSM changes.

- [ ] **Step 5: Commit the memory proof**

```powershell
git add -- builder.tests/Ps2VuFullFrustumClippingSourceTests.cs builder.tests/Ps2VuNearPlaneClippingSourceTests.cs src/platform/ps2/rendering/vu/Ps2VuTexturedSourceLimits.hpp
git diff --cached --check
git commit -m "fix(ps2): bound full-frustum VU clipping memory"
```

### Task 3: Seed the Polygon and Implement Six-Plane Clipping

**Files:**
- Modify: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedClipDraw3D.vsm`
- Modify: `builder.tests/Ps2VuFullFrustumClippingSourceTests.cs`

- [ ] **Step 1: Replace the diagnostic triangle gate with scratch seeding**

After transforming `VF18`, `VF19`, and `VF20`, preserve the triangle loop counter, initialize buffer A at qword `0x50`, set `VI13 = 3`, and store alternating position/UV pairs:

```text
texturedClipSeedPolygon:
         NOP                                                        iadd VI10, VI01, VI00
         NOP                                                        iaddiu VI12, VI00, 0x00000050
         NOP                                                        iaddiu VI13, VI00, 0x00000003
         NOP                                                        sq VF18, 0(VI12)
         NOP                                                        sq VF21, 1(VI12)
         NOP                                                        sq VF19, 2(VI12)
         NOP                                                        sq VF22, 3(VI12)
         NOP                                                        sq VF20, 4(VI12)
         NOP                                                        sq VF23, 5(VI12)
```

Remove the early whole-triangle hardware rejection and the unconditional branch at `texturedClipEmitPolygon`.

- [ ] **Step 2: Define the plane order and signed distances**

Use `VI09` values 0 through 5 and explicit labels:

```text
texturedClipPlaneCameraW: ; d = w - epsilon
texturedClipPlaneNearZ:  ; d = z - epsilon
texturedClipPlaneLeft:   ; d = x + w
texturedClipPlaneRight:  ; d = w - x
texturedClipPlaneBottom: ; d = y + w
texturedClipPlaneTop:    ; d = w - y
```

For each loaded polygon vertex, run `CLIPW` and capture the selected hardware flag before another clip instruction. For camera and near tests, construct the synthetic vector used by the proven B296 diagnostic so the negative-Z flag represents `w < epsilon` or `z < epsilon`. Preserve live loop state before `FCAND` writes an integer register.

- [ ] **Step 3: Implement the Sutherland-Hodgman edge rule**

For each previous/current pair:

```text
if previousInside != currentInside:
    denominator = previousDistance - currentDistance
    reject edge when abs(denominator) < epsilon
    t = previousDistance / denominator
    clamp t to [0, 1]
    output lerp(previous, current, t)
if currentInside:
    output current
```

Use qword `0x50` for buffer A and `0x64` for buffer B. Alternate buffers after each plane, cap output at nine vertices, and set the VU-local overflow slot before rejecting an overflowing source triangle.

- [ ] **Step 4: Snap generated intersections to the active boundary**

After interpolating XYZW and raw UV, apply exactly one snap:

```text
camera: w = epsilon
near:   z = epsilon
left:   x = -w
right:  x = w
bottom: y = -w
top:    y = w
```

Do not divide or convert UVs during clipping.

- [ ] **Step 5: Run source tests**

Run the Task 1 focused command.

Expected: seed, plane-order, interpolation, and no-triangle-drop contracts pass; fan/projection contracts may remain red until Task 4.

- [ ] **Step 6: Assemble the VU program**

Run a native compile through the deterministic waiter:

```powershell
dotnet run --project C:\dev\helworks\helengine\tools\build-waiter\helengine.buildwaiter.csproj -c Release --no-restore -- `
  --output C:\dev\helworks\builds\demodisc\ps2\full-clip-native-task-3 `
  --require game.iso `
  --require disc/SYSTEM.CNF `
  --require disc/HELENGIN.ELF `
  -- powershell -NoProfile -ExecutionPolicy Bypass -File C:\dev\helworks\helengine\scripts\build-platform.ps1 `
  -Project C:\dev\helprojs\demodisc\project.heproj `
  -Platform ps2 `
  -Output C:\dev\helworks\builds\demodisc\ps2\full-clip-native-task-3 `
  -Configuration Debug `
  -BuildProfile ps2-default `
  -WorkspaceRoot C:\dev\helworks\b
```

Expected: `Ps2OpaqueTexturedClipDraw3D.vsm` assembles without rejected opcodes, invalid branch ranges, or register syntax errors.

- [ ] **Step 7: Commit the clipping kernel**

```powershell
git add -- builder.tests/Ps2VuFullFrustumClippingSourceTests.cs src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedClipDraw3D.vsm
git diff --cached --check
git commit -m "feat(ps2): clip textured VU polygons by frustum"
```

### Task 4: Triangulate, Project, Cull, and Compact

**Files:**
- Modify: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedClipDraw3D.vsm`
- Modify: `builder.tests/Ps2VuFullFrustumClippingSourceTests.cs`

- [ ] **Step 1: Add the final pre-division validation contract**

Require `texturedClipValidateTriangleA/B/C` to occur after `texturedClipEmitTriangleFanLoop` and before the first `DIV`. Require camera-W, near-Z, and all side-plane validation labels for every generated fan vertex.

- [ ] **Step 2: Emit a triangle fan from the surviving polygon**

Use vertex 0 as the anchor and emit `(0, i, i + 1)` for `i = 1` through `vertexCount - 2`:

```text
texturedClipEmitTriangleFanLoop:
         ; VF18/VF21 retain anchor position/UV.
         ; VF19/VF22 load polygon vertex i.
         ; VF20/VF23 load polygon vertex i + 1.
         NOP                                                        bal VI15, texturedClipEmitTriangle
         NOP                                                        NOP
```

Advance the polygon cursor by one position/UV pair after each fan triangle.

- [ ] **Step 3: Reuse the proven perspective-correct emitter**

For each accepted vertex preserve this order:

```text
         NOP                                                        div Q, VF00w, VF08w
         mulq.xy       VF09, VF09, Q                                waitq
         addq.z        VF09, VF00, Q                                NOP
         mulq.xyz      VF08, VF08, Q                                NOP
```

This keeps raw UV interpolation followed by `S=U/W`, `T=V/W`, and `Q=1/W`.

- [ ] **Step 4: Preserve material winding semantics**

After projection, run the existing signed-area test. Branch directly to acceptance only when shared state marks the material double-sided. Advance `VI03` and `VI06` only for accepted triangles.

- [ ] **Step 5: Derive the final GIF loop count**

At submission end, write `emittedTriangleCount * 3` into the copied GIF tag, restore the EOP bit, and issue one `XGKICK VI04`. A source triangle that clips to zero vertices emits nothing and does not affect `NLOOP`.

- [ ] **Step 6: Run tests and native assembly**

Run the Task 1 focused tests, then run:

```powershell
dotnet run --project C:\dev\helworks\helengine\tools\build-waiter\helengine.buildwaiter.csproj -c Release --no-restore -- `
  --output C:\dev\helworks\builds\demodisc\ps2\full-clip-native-task-4 `
  --require game.iso `
  --require disc/SYSTEM.CNF `
  --require disc/HELENGIN.ELF `
  -- powershell -NoProfile -ExecutionPolicy Bypass -File C:\dev\helworks\helengine\scripts\build-platform.ps1 `
  -Project C:\dev\helprojs\demodisc\project.heproj `
  -Platform ps2 `
  -Output C:\dev\helworks\builds\demodisc\ps2\full-clip-native-task-4 `
  -Configuration Debug `
  -BuildProfile ps2-default `
  -WorkspaceRoot C:\dev\helworks\b
```

Expected: all full-frustum source contracts pass and `dvp-as` completes.

- [ ] **Step 7: Commit the compact emitter**

```powershell
git add -- builder.tests/Ps2VuFullFrustumClippingSourceTests.cs src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedClipDraw3D.vsm
git diff --cached --check
git commit -m "feat(ps2): emit clipped textured triangle fans"
```

### Task 5: Restore Production Slice Routing

**Files:**
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Modify: `builder.tests/Ps2VuFullFrustumClippingSourceTests.cs`
- Modify: `builder.tests/Ps2VuNearPlaneClippingSourceTests.cs`

- [ ] **Step 1: Disable diagnostic force routing**

Set only this constant:

```cpp
constexpr bool ForceAllTexturedSlicesThroughClipProgramDiagnostics = false;
```

Keep `DropClippedTexturedSlicesForDiagnostics` and `UseFastProgramForClippedSliceDiagnostics` false.

- [ ] **Step 2: Verify route capacities**

Retain `TexturedVuSourceTriangleCapacity` for `Fast` and `TexturedVuClippedSourceTriangleCapacity` for `Clipped`. Retain complete-slice rejection before packet emission. Do not add CPU clipping or per-triangle route classification.

- [ ] **Step 3: Run the complete builder test project**

```powershell
$Output = & dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --no-restore --results-directory C:\dev\helworks\builds\helengine-ps2\test-results\full-clip-task-5 2>&1
$ExitCode = $LASTEXITCODE
$Output | Select-Object -Last 120
exit $ExitCode
```

Expected: PASS with zero failed tests.

- [ ] **Step 4: Commit production routing**

```powershell
git add -- builder.tests/Ps2VuFullFrustumClippingSourceTests.cs builder.tests/Ps2VuNearPlaneClippingSourceTests.cs src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp
git diff --cached --check
git commit -m "fix(ps2): route intersecting slices to VU clipping"
```

### Task 6: Build and Validate the Isolated Tilt Render Test

**Files:**
- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Build input only: `C:\dev\helworks\builds\demodisc\ps2\build-inputs\B315-full-clip`
- Build output only: `C:\dev\helworks\builds\demodisc\ps2\B315-full-clip`

- [ ] **Step 1: Advance the profiling identifier**

Set the profiling overlay build number to `B315`. Do not enable the profiling overlay in ordinary full-disc debug or release builds.

- [ ] **Step 2: Prepare an isolated scene-only build input**

Copy the DemoDisc project into the visible B315 build-input directory while excluding `.git`, `.worktrees`, caches, outputs, generated code, and prior package artifacts. In the copied `user_settings/build_config.json`, select only `test_scene_tilt_trial_level_01_render` at order 1. Confirm the source project still selects 21 PS2 scenes.

- [ ] **Step 3: Build through the deterministic waiter**

Require all three artifacts:

```powershell
dotnet run --project C:\dev\helworks\helengine\tools\build-waiter\helengine.buildwaiter.csproj -c Release --no-restore -- `
  --output C:\dev\helworks\builds\demodisc\ps2\B315-full-clip `
  --require game.iso `
  --require disc/SYSTEM.CNF `
  --require disc/HELENGIN.ELF `
  -- powershell -NoProfile -ExecutionPolicy Bypass -File C:\dev\helworks\helengine\scripts\build-platform.ps1 `
  -Project C:\dev\helworks\builds\demodisc\ps2\build-inputs\B315-full-clip\project.heproj `
  -Platform ps2 `
  -Output C:\dev\helworks\builds\demodisc\ps2\B315-full-clip `
  -Configuration Debug `
  -BuildProfile ps2-default `
  -WorkspaceRoot C:\dev\helworks\b
```

Expected: waiter reports all artifacts fresh and non-empty.

- [ ] **Step 4: Launch the exact B315 ISO**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\launch_in_emulator.ps1 -ArtifactPath C:\dev\helworks\builds\demodisc\ps2\B315-full-clip\game.iso
```

- [ ] **Step 5: Record telemetry with HelenUI**

OCR the PCSX2 window through the established HelenUI navigation service. Record build ID, 3D time, fast/clipped/rejected slice counts, triangles, batches, and frame time. Do not inspect screenshots manually.

- [ ] **Step 6: Request visual clipping feedback**

Ask the user to move the camera slowly into `ClipProbeCube`, through oblique faces, below the ground, and behind the cube.

Expected: crossing faces remain as clipped polygons; no source triangle disappears solely because one vertex is outside; no explosion, flashing, or unrelated-face clipping occurs.

- [ ] **Step 7: Stop on a failed visual result**

If geometry is wrong, preserve B315, identify whether failure is classification, interpolation, fan ordering, winding, or GIF count, and form one new hypothesis. Do not combine fixes.

- [ ] **Step 8: Commit the validated build identifier**

```powershell
git add -- src/platform/ps2/Ps2BootHost.cpp
git diff --cached --check
git commit -m "chore(ps2): identify full-frustum clipping build"
```

### Task 7: Verify Performance and Full DemoDisc Integration

**Files:**
- Build output only: `C:\dev\helworks\builds\demodisc\ps2\B316-full-demodisc-clipping`

- [ ] **Step 1: Measure safe and intersecting views**

Use HelenUI OCR on B315. A safe view must remain within 0.2 ms of the established fast-path baseline. An intersecting view should add no more than approximately 0.5 ms in the Tilt render test.

- [ ] **Step 2: Confirm visual invariants**

Verify perspective-correct textures at oblique angles, correct lighting, single-sided opaque geometry, and explicit double-sided materials. Confirm camera and all four screen-side crossings.

- [ ] **Step 3: Build the unchanged full 21-scene configuration**

Run the deterministic PS2 build waiter against `C:\dev\helprojs\demodisc\project.heproj` with profile `ps2-default`, requiring ISO, `SYSTEM.CNF`, and ELF. Do not alter the source build configuration.

- [ ] **Step 4: Launch through the repository script**

Launch B316 once. Let the user navigate; do not automate menu input.

- [ ] **Step 5: Validate affected scenes**

Exercise Stacked Boxes, Stacked Spheres, Tilt render test, Tilt Play Scene 01, colored cubes, and textured cubes. Confirm scene transitions do not crash and ground/course meshes remain visible.

- [ ] **Step 6: Run final automated verification**

Run the complete builder test project and `git diff --check` on every task-owned source file.

Expected: zero failed tests and no whitespace errors.

- [ ] **Step 7: Commit any final test-only adjustments**

Only if validation required contract corrections, stage those exact test files and commit them separately. Do not fold unrelated dirty files into the clipping work.
