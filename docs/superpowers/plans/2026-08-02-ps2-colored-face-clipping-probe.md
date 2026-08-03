# PS2 Colored-Face Clipping Probe Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the isolated Tilt render-test cube with one non-tessellated, six-color textured cube that preserves the PS2 textured VIF/VU path and makes remaining close-camera corruption attributable to a specific face.

**Architecture:** DemoDisc generation creates a probe-only model, BMP atlas, and textured material. The existing render-test scene receives those runtime assets through `RenderingSceneGenerationAssets`; the playable Tilt scene and shared engine cube remain unchanged. Helengine PS2 changes only the hard-coded overlay identity from B322 to B323 for stale-build detection.

**Tech Stack:** C# DemoDisc asset generation, Helen model/material/texture assets, PS2 VIF/VU textured renderer, xUnit source contracts, deterministic build-waiter, PCSX2, HelenUI OCR.

## Global Constraints

- Work directly on each repository's `main` checkout; do not create a worktree.
- Preserve the probe scene id, camera, light, 5-by-1-by-5 transform, FPS overlay, and one-mesh/one-material textured submission.
- Use exactly 24 face-local vertices, 12 source triangles, and six face atlas cells.
- Back is red, front green, right blue, left yellow, top magenta, and bottom cyan.
- Do not attach model-import or MeshComponent tessellation settings to the probe on any platform.
- Do not change the shared engine cube, playable Tilt Trial scene, clipping math, VU microcode, packet batching, or normal DemoDisc build selection.
- Add substantive XML comments to every new C# class and member; use one class per file and the existing brace/member-order conventions.
- Use `apply_patch` for source edits. Build outputs and logs belong under `C:\dev\helworks\builds`, never `%TEMP%`.
- Launch PCSX2 only through `scripts/launch_in_emulator.ps1`; use HelenUI only for text OCR and ask Helena for visual feedback.

---

### Task 1: Generate the probe model, atlas, and textured material

**Files:**

- Create: `C:\dev\helprojs\demodisc\assets\codebase\rendering.tools\TiltTrialClippingProbeModelFactory.cs`
- Create: `C:\dev\helprojs\demodisc\assets\codebase\rendering.tools\TiltTrialClippingProbeTextureFactory.cs`
- Create: `C:\dev\helprojs\demodisc\assets\codebase\rendering.tools\TiltTrialClippingProbeMaterialFactory.cs`
- Create: `C:\dev\helprojs\demodisc\assets\codebase\rendering.tools.tests\TiltTrialClippingProbeAssetSourceTests.cs`

**Interfaces:**

- Produces: `TiltTrialClippingProbeModelFactory.ModelRelativePath`, `ModelAssetId`, `CreateModelAsset()`, and `WriteModelAsset(string projectRootPath)`.
- Produces: `TiltTrialClippingProbeTextureFactory.TextureRelativePath` and `WriteTextureAsset(string projectRootPath)`.
- Produces: `TiltTrialClippingProbeMaterialFactory.MaterialRelativePath`, `MaterialAssetId`, and `WriteMaterialAsset(string projectRootPath)`.
- The model path is `models/rendering/tilt_trial/clipping_probe_face_colors.hasset`.
- The texture path is `textures/rendering/tilt_trial/clipping_probe_face_colors.bmp`.
- The material path is `materials/rendering/tilt_trial/ClippingProbeFaceColors.hasset`.

- [ ] **Step 1: Write the failing asset contract tests**

Add tests that require the three probe factories and assert these literal contracts:

```csharp
Assert.Contains("public const string ModelAssetId = \"Models.rendering.tilt_trial.ClippingProbeFaceColors\";", modelSource, StringComparison.Ordinal);
Assert.Contains("public ModelAsset CreateModelAsset()", modelSource, StringComparison.Ordinal);
Assert.Contains("new float3(-0.5f, -0.5f, -0.5f)", modelSource, StringComparison.Ordinal);
Assert.Contains("Indices16 =", modelSource, StringComparison.Ordinal);
Assert.Contains("BackFaceUv", modelSource, StringComparison.Ordinal);
Assert.Contains("FrontFaceUv", modelSource, StringComparison.Ordinal);
Assert.Contains("RightFaceUv", modelSource, StringComparison.Ordinal);
Assert.Contains("LeftFaceUv", modelSource, StringComparison.Ordinal);
Assert.Contains("TopFaceUv", modelSource, StringComparison.Ordinal);
Assert.Contains("BottomFaceUv", modelSource, StringComparison.Ordinal);
Assert.Contains("const int TextureWidth = 128;", textureSource, StringComparison.Ordinal);
Assert.Contains("const int TextureHeight = 64;", textureSource, StringComparison.Ordinal);
Assert.Contains("ResolveCellColor", textureSource, StringComparison.Ordinal);
Assert.Contains("ps2-simple-lit-textured", materialSource, StringComparison.Ordinal);
Assert.Contains("texture-relative-path", materialSource, StringComparison.Ordinal);
Assert.Contains("double-sided", materialSource, StringComparison.Ordinal);
```

Also assert that model indices contain exactly 36 entries and that the six UV constants map into six non-overlapping padded regions of the 128-by-64 atlas.

- [ ] **Step 2: Run the source contracts and verify RED**

Run:

```powershell
dotnet test C:\dev\helprojs\demodisc\user_settings\generated_code\projects\rendering.tools.tests\rendering.tools.tests.csproj `
  --filter FullyQualifiedName~TiltTrialClippingProbeAssetSourceTests `
  --verbosity minimal
```

Expected: FAIL because the three factory source files and contracts do not exist.

- [ ] **Step 3: Implement the 12-triangle model factory**

`CreateModelAsset()` must construct the canonical cube positions, normals, and indices without calling `ModelUtils.GenerateCubeMesh`, then replace each face's repeated 0-1 UVs with padded atlas coordinates. Use six named `float2[]` face UV arrays and concatenate in back/front/right/left/top/bottom order. Set explicit bounds to `(-0.5,-0.5,-0.5)` and `(0.5,0.5,0.5)`.

`WriteModelAsset()` must serialize that `ModelAsset` directly to `assets/models/rendering/tilt_trial/clipping_probe_face_colors.hasset` using `AssetSerializer.Serialize`.

- [ ] **Step 4: Implement the deterministic 128-by-64 atlas**

Write a 24-bit BMP with six 32-by-24 solid cells separated by unused border: 8 pixels at the left, between columns, and at the right; 4 pixels above and below; and 8 pixels between rows. The two rows and three columns correspond to the face order in the model factory. Keep every face UV one texel inside its solid cell so either point or linear filtering cannot sample the border. Emit exact channel values:

```csharp
static readonly byte4[] FaceColors = [
    new byte4(255, 0, 0, 255),
    new byte4(0, 255, 0, 255),
    new byte4(0, 0, 255, 255),
    new byte4(255, 255, 0, 255),
    new byte4(255, 0, 255, 255),
    new byte4(0, 255, 255, 255)
];
```

Load or create texture import settings with the editor's registered importers and return the persisted imported texture asset id. Do not add a platform resolution reduction because the source is already 128-by-64.

- [ ] **Step 5: Implement the probe-only textured material**

Follow `TiltTrialCourseMaterialFactory`'s platform definitions but use the probe asset id/path, white base color, opaque alpha, back-face culling, `double-sided=false`, and the imported atlas texture. PS2 must use `ps2-simple-lit-textured`, include both `texture-id` and `texture-relative-path`, and keep lighting enabled so the renderer follows the same path as B322.

- [ ] **Step 6: Re-run the focused contracts and compile the generated tools**

Run:

```powershell
dotnet test C:\dev\helprojs\demodisc\user_settings\generated_code\projects\rendering.tools.tests\rendering.tools.tests.csproj `
  --filter FullyQualifiedName~TiltTrialClippingProbeAssetSourceTests `
  --verbosity minimal
dotnet build C:\dev\helprojs\demodisc\user_settings\generated_code\projects\rendering.tools\rendering.tools.csproj `
  --verbosity minimal
```

Expected: the focused contracts pass and the DemoDisc tooling compiles without missing XML comments, invalid asset paths, or material schema errors.

- [ ] **Step 7: Commit Task 1 in DemoDisc**

```powershell
git -C C:\dev\helprojs\demodisc add -- `
  assets/codebase/rendering.tools/TiltTrialClippingProbeModelFactory.cs `
  assets/codebase/rendering.tools/TiltTrialClippingProbeTextureFactory.cs `
  assets/codebase/rendering.tools/TiltTrialClippingProbeMaterialFactory.cs `
  assets/codebase/rendering.tools.tests/TiltTrialClippingProbeAssetSourceTests.cs
git -C C:\dev\helprojs\demodisc commit -m "test(rendering): add colored-face clipping probe assets"
```

---

### Task 2: Wire only the isolated render scene to the probe assets

**Files:**

- Modify: `C:\dev\helprojs\demodisc\assets\codebase\rendering.tools\RenderingSceneGenerationAssets.cs`
- Modify: `C:\dev\helprojs\demodisc\assets\codebase\rendering.tools\RenderingSceneAssetPreparationService.cs`
- Modify: `C:\dev\helprojs\demodisc\assets\codebase\game.tools\GameSceneFactory.cs`
- Modify: `C:\dev\helprojs\demodisc\assets\codebase\game.tools.tests\TiltTrialSceneGenerationSourceTests.cs`

**Interfaces:**

- Consumes: Task 1's model/material paths and factory methods.
- Produces: `RenderingSceneGenerationAssets.TiltTrialClippingProbeModel` and `.TiltTrialClippingProbeMaterial`.
- Produces: `GameSceneFactory` fields with the same names and a render-only mesh reference using those assets.

- [ ] **Step 1: Change the scene tests first**

Replace the old tessellated probe assertion with exact expectations:

```csharp
Assert.Contains("TiltTrialClippingProbeModel = tiltTrialClippingProbeModel", preparationSource, StringComparison.Ordinal);
Assert.Contains("TiltTrialClippingProbeMaterial = tiltTrialClippingProbeMaterial", preparationSource, StringComparison.Ordinal);
Assert.Contains("Model = TiltTrialClippingProbeModel", factorySource, StringComparison.Ordinal);
Assert.Contains("Materials = new[] { TiltTrialClippingProbeMaterial }", factorySource, StringComparison.Ordinal);
Assert.Contains("CreateFileSystemModel(TiltTrialClippingProbeModelFactory.ModelRelativePath)", factorySource, StringComparison.Ordinal);
Assert.Contains("CreateFileSystemMaterial(TiltTrialClippingProbeMaterialFactory.MaterialRelativePath)", factorySource, StringComparison.Ordinal);
Assert.DoesNotContain("CreateLevel01RenderOnlyCourseBoxEntity(\"ClipProbeCube\", float3.Zero, new float3(5f, 1f, 5f), float4.Identity, true)", factorySource, StringComparison.Ordinal);
```

Keep the existing assertions for one cube, camera position/orientation, fixed manual yaw/pitch, light, FPS overlay, and excluded unrelated visual roots.

- [ ] **Step 2: Run the focused scene test and verify RED**

Run:

```powershell
dotnet test C:\dev\helprojs\demodisc\user_settings\generated_code\projects\game.tools.tests\game.tools.tests.csproj `
  --filter FullyQualifiedName~TiltTrialSceneGenerationSourceTests `
  --verbosity minimal
```

Expected: FAIL because the asset bundle and scene still use `GeneratedCubeModel`, `TiltTrialCourseMaterial`, and the tessellation-enabled call.

- [ ] **Step 3: Extend the runtime asset bundle and preparation flow**

Write model, texture, and material assets before loading runtime assets. Load the probe model through `LoadImportedModelRuntime` and the probe material through `LoadRuntimeMaterial`. Add both required non-null values to the returned `RenderingSceneGenerationAssets` object.

- [ ] **Step 4: Replace only the render-test cube dependencies**

Validate and store the two probe assets in `GameSceneFactory`'s constructor. `CreateLevel01RenderOnlyCourseBoxEntity` must use them and persist explicit file-system references for both `Model` and `Materials[0]`. Remove the render-only boolean parameter and its call to `ApplyConstrainedPlatformTessellation`; retain the method and its playable-scene use in `CreateKinematicCourseBoxEntity`.

- [ ] **Step 5: Re-run focused tests and regenerate the scene**

Run:

```powershell
dotnet test C:\dev\helprojs\demodisc\user_settings\generated_code\projects\game.tools.tests\game.tools.tests.csproj `
  --filter FullyQualifiedName~TiltTrialSceneGenerationSourceTests `
  --verbosity minimal
dotnet run --project C:\dev\helworks\helengine\helengine.ui\helengine.editor.app\helengine.editor.app.csproj -- `
  --project C:\dev\helprojs\demodisc\project.heproj `
  --editor-command menu.generate-game-scenes
```

Expected: scene contracts pass, generation writes `assets/scenes/physics/test_scene_tilt_trial_level_01_render.helen`, and `rg -n "tessellat|Tessellat" C:\dev\helprojs\demodisc\assets\scenes\physics\test_scene_tilt_trial_level_01_render.helen` returns no probe MeshComponent tessellation payload.

- [ ] **Step 6: Commit Task 2 in DemoDisc**

```powershell
git -C C:\dev\helprojs\demodisc add -- `
  assets/codebase/rendering.tools/RenderingSceneGenerationAssets.cs `
  assets/codebase/rendering.tools/RenderingSceneAssetPreparationService.cs `
  assets/codebase/game.tools/GameSceneFactory.cs `
  assets/codebase/game.tools.tests/TiltTrialSceneGenerationSourceTests.cs
git -C C:\dev\helprojs\demodisc commit -m "test(tilt): isolate colored-face clipping probe"
```

---

### Task 3: Build B323 and collect face-specific visual evidence

**Files:**

- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`
- Modify: `builder.tests/Ps2StartupManifestSourceTests.cs`
- Temporary modify and restore: `C:\dev\helprojs\demodisc\user_settings\build_config.json`
- Output: `C:\dev\helworks\builds\helengine-ps2\ps2\B323-colored-face-clip-probe`

**Interfaces:**

- Consumes: Task 2's generated isolated scene.
- Produces: hard-coded overlay identity `B323`, fresh ISO/ELF/SYSTEM.CNF, and `validation.md`.

- [ ] **Step 1: Change the build-identity tests first**

Require:

```csharp
Assert.Contains("constexpr const char* FrameTimingOverlayBuildNumber = \"B323\";", source, StringComparison.Ordinal);
```

Update every build-number assertion and test name in `Ps2RenderManager3DSourceTests.cs` and `Ps2StartupManifestSourceTests.cs`, then run:

```powershell
dotnet test builder.tests\builder.tests.csproj `
  --filter "FullyQualifiedName~Ps2BootHost_WhenPublishingFrameTiming_PrefixesTheFpsRowWithBuildNumberB323|FullyQualifiedName~Ps2RenderManager3D_PublishesAggregateHybridClippingMetricsThroughTheExistingProfilerRow|FullyQualifiedName~Ps2BootHost_WhenRemovingTheDiagnosticOverride_UsesBuildMarkerB323" `
  --verbosity minimal
```

Expected: FAIL against B322.

- [ ] **Step 2: Set the runtime build identity to B323**

Change only `FrameTimingOverlayBuildNumber` in `Ps2BootHost.cpp`, then rerun the command from Step 1 plus:

```powershell
dotnet test builder.tests\builder.tests.csproj `
  --filter FullyQualifiedName~Ps2VuHybridClippedBatchSourceTests `
  --verbosity minimal
```

Expected: PASS.

- [ ] **Step 3: Select only the isolated scene temporarily**

In DemoDisc's PS2 build entry, temporarily set `selectedSceneIds` and `sceneOrders` to only `test_scene_tilt_trial_level_01_render`. Capture the previous entry first and restore it with `apply_patch` immediately after packaging.

- [ ] **Step 4: Build through the deterministic waiter**

```powershell
dotnet run --project C:\dev\helworks\helengine\tools\build-waiter\helengine.buildwaiter.csproj -- `
  --output C:\dev\helworks\builds\helengine-ps2\ps2\B323-colored-face-clip-probe `
  --require game.iso `
  --require disc/SYSTEM.CNF `
  --require disc/HELENGIN.ELF `
  -- powershell -NoProfile -ExecutionPolicy Bypass `
  -File C:\dev\helworks\helengine\scripts\build-platform.ps1 `
  -Project C:\dev\helprojs\demodisc\project.heproj `
  -Platform ps2 `
  -Configuration Debug `
  -BuildProfile ps2-default `
  -Output C:\dev\helworks\builds\helengine-ps2\ps2\B323-colored-face-clip-probe
```

Do not impose an arbitrary timeout. Restore the DemoDisc build config and verify its diff is empty.

- [ ] **Step 5: Launch the exact B323 ISO**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File C:\dev\helworks\helengine-ps2\scripts\launch_in_emulator.ps1 `
  -ArtifactPath C:\dev\helworks\builds\helengine-ps2\ps2\B323-colored-face-clip-probe\game.iso
```

- [ ] **Step 6: Record safe OCR and request visual intersection feedback**

Use HelenUI to record B323, 3D, and F/C/R/G/CB in `validation.md`. Ask Helena to move through the cube and report the color of every displaced, stretched, flashing, disappearing, or exploding face. Do not infer geometry correctness from OCR.

- [ ] **Step 7: Commit the PS2 build identity and diagnostic test changes**

After the build is verified and while preserving unrelated dirty files:

```powershell
git add -- `
  src/platform/ps2/Ps2BootHost.cpp `
  builder.tests/Ps2RenderManager3DSourceTests.cs `
  builder.tests/Ps2StartupManifestSourceTests.cs
git commit -m "test(ps2): identify colored-face clipping probe build"
```

Do not mark the clipping defect fixed. Feed the color-specific visual result back into the active hybrid clipping Task 7 investigation.
