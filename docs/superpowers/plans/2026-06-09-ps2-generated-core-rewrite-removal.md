# PS2 Generated Core Rewrite Removal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the PS2 generated-core rewrite pass by moving Helengine runtime-shape ownership into `helengine.editor`, leaving `csharpcodegen` generic and `helengine-ps2` rewrite-free.

**Architecture:** `helengine.editor` will compute typed runtime-generation semantics from platform metadata and emit Helengine source/runtime shape that translates correctly through `csharpcodegen`. `helengine-ps2` will only declare capabilities, provide native runtime implementation where required, and fail fast on contract mismatches during transition.

**Tech Stack:** C#, .NET test projects, Helengine editor/build graph, PS2 platform builder, native PS2 runtime C++

---

### Task 1: Freeze The Rewrite Inventory And Ownership Map

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\docs\superpowers\specs\2026-06-09-ps2-generated-core-rewrite-removal-design.md`
- Inspect: `C:\dev\helworks\helengine-ps2\builder\Ps2NativeBuildExecutor.cs`

- [ ] **Step 1: Enumerate the current rewrite entry points**

Read `C:\dev\helworks\helengine-ps2\builder\Ps2NativeBuildExecutor.cs` and list every file passed to `NormalizeGeneratedCoreFile(...)` plus every `Normalize*Source` helper.

- [ ] **Step 2: Classify each rewrite**

For each rewrite, assign exactly one owner:
- `helengine.editor` runtime-generation contract
- `helengine.editor` emitted-source bug
- native runtime implementation gap

- [ ] **Step 3: Update the design spec with the classified inventory**

Expand `C:\dev\helworks\helengine-ps2\docs\superpowers\specs\2026-06-09-ps2-generated-core-rewrite-removal-design.md` so the inventory is explicit and the owner of each rewrite family is unambiguous.

- [ ] **Step 4: Review the inventory for "hidden rewrites"**

Confirm there are no additional generated-output edits outside `Ps2NativeBuildExecutor.cs`, including deleted legacy helpers such as `Ps2CookedAssetPathRewriter.cs`.

- [ ] **Step 5: Verify no code changed in this task**

Run: `rtk git diff --stat`

Expected: only documentation changes at this stage.

### Task 2: Extend The Existing RuntimeGenerationContract And Editor Consumption

**Files:**
- Modify: `C:\dev\helworks\helengine\engine\helengine.baseplatform\Definitions\RuntimeGenerationContract.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.baseplatform\Definitions\PlatformDefinition.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorLoadedPlatformBuilder.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorPlatformCookWorkItemFactory.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\EditorSession.cs`
- Test: `C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj`

- [ ] **Step 1: Extend `RuntimeGenerationContract` only where the existing surface is insufficient**

Keep the existing contract for decisions it already models:
- material resolution mode
- packaged path policy
- render-manager-2D texture-release-flush support

Add new contract dimensions only for the remaining decisions that cannot be represented with the existing contract and existing editor profile settings:
- runtime file access policy
- runtime asset-id normalization policy
- runtime graphics feature tier, if existing graphics-profile settings are not sufficient

- [ ] **Step 2: Thread the metadata through editor platform loading**

Update the editor-side builder loading flow so `EditorLoadedPlatformBuilder` and the cook/build pipeline can read the runtime-generation contract from platform definitions.

- [ ] **Step 3: Have `helengine.editor` compute and expose the effective runtime-generation behavior**

Update the relevant editor build/session code so the contract from `helengine.baseplatform`, together with any existing editor-owned generation inputs such as graphics-profile settings, reaches the code preparing generated runtime/source output.

- [ ] **Step 4: Add focused editor tests for metadata flow**

Add or update tests in `helengine.editor.tests` that prove platform metadata survives loading and is available to the generation path.

- [ ] **Step 5: Run the smallest editor test slice**

Run: `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj --filter EditorLoadedPlatformBuilder`

Expected: the targeted metadata-loading tests pass.

### Task 3: Move Material And Render-Manager Contract Generation Into Helengine Editor

**Files:**
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\serialization\scene\EditorSceneAssetReferenceResolver.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorWindowsBuildScenePackager.cs`
- Modify: any Helengine editor/runtime generation files that emit:
  - `RuntimeContentManagerConfiguration.cpp`
  - `RuntimeSceneAssetReferenceResolver.cpp`
  - `RenderManager3D.hpp`
  - `RenderManager3D.cpp`
- Test: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2RenderManager3DSourceTests.cs`
- Test: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildExecutorTests.cs`
- Test: `C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj`

- [ ] **Step 1: Identify the Helengine editor emitters for the four material/renderer outputs**

Map the exact editor-side generation code paths that produce the generated runtime content-manager, scene resolver, and render-manager files that PS2 currently patches.

- [ ] **Step 2: Make the emitters branch on typed runtime-generation contract**

Update the editor generation code so PS2-compatible cooked-material flow is emitted directly instead of via post-generation replacement.

- [ ] **Step 3: Add editor-side tests for generated material/renderer shape**

Add assertions that the generated output contains the cooked-platform contract when the PS2 contract is selected and does not require post-processing.

- [ ] **Step 4: Convert PS2 material/renderer rewrites into assertions**

In `C:\dev\helworks\helengine-ps2\builder\Ps2NativeBuildExecutor.cs`, stop mutating those four generated files and replace the old rewrite code with strict validation that the expected generated shape is already present.

- [ ] **Step 5: Run targeted tests**

Run:
- `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj`
- `rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter Ps2RenderManager3DSourceTests`

Expected: editor generation tests and PS2 render-manager validation tests pass without source mutation.

### Task 4: Move Runtime Graphics Manifest Generation Into Helengine Editor

**Files:**
- Modify: Helengine editor/runtime generation code that emits `runtime_graphics_renderer_manifest.cpp`
- Modify: `C:\dev\helworks\helengine-ps2\builder\Ps2NativeBuildExecutor.cs`
- Test: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2BootHostSourceTests.cs`
- Test: `C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj`

- [ ] **Step 1: Identify the Helengine emitter for runtime graphics manifest output**

Find the editor-side generator that controls HDR enablement and post-process tier in the generated runtime manifest.

- [ ] **Step 2: Emit PS2-compatible graphics defaults from the editor**

Make the emitter select PS2-compatible manifest values from the runtime-generation contract rather than emitting generic defaults.

- [ ] **Step 3: Replace the PS2 graphics manifest rewrite with validation**

Remove the mutation logic from `Ps2NativeBuildExecutor.cs` and fail the build if the generated manifest does not already carry the expected values.

- [ ] **Step 4: Add editor-side manifest assertions**

Add tests that verify the PS2 contract emits the expected manifest values while other platforms keep their existing behavior.

- [ ] **Step 5: Run targeted tests**

Run:
- `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj`
- `rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter Ps2BootHostSourceTests`

Expected: the manifest is generated correctly and PS2 no longer patches it.

### Task 5: Resolve File-Io, Path, And Asset-Id Ownership

**Files:**
- Modify: Helengine editor generation files that control packaged paths or generated runtime helpers
- Modify: native/runtime files in `C:\dev\helworks\helengine-ps2\src\platform\ps2\...` if the behavior belongs in runtime implementation
- Modify: `C:\dev\helworks\helengine-ps2\builder\Ps2NativeBuildExecutor.cs`
- Test: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildInputsTests.cs`
- Test: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2PlatformAssetBuilderTests.cs`
- Test: `C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj`

- [ ] **Step 1: Decide ownership for each remaining rewrite family**

For:
- file-stream source/header rewrites
- runtime asset-id generator rewrites
- `Core.cpp` and `SceneManager.cpp` rewrites
- component-source rewrites such as `ScrollComponent.cpp`, `FontAsset.cpp`, and `FPSComponent.*`

decide whether the fix is editor-owned generation or native runtime implementation.

- [ ] **Step 2: Implement editor-side fixes for generated-source bugs**

Fix any stale or malformed emitted source in Helengine generation so translation produces valid C++ without PS2 patching.

- [ ] **Step 3: Implement runtime-side fixes for true PS2 runtime behavior**

If packaged-disc access or similar behavior is runtime-specific, implement it in PS2 runtime/native code instead of generated-file mutation.

- [ ] **Step 4: Replace the corresponding PS2 rewrites with assertions**

For each migrated family, remove mutation logic from `Ps2NativeBuildExecutor.cs` and leave only strict checks during the transition.

- [ ] **Step 5: Run targeted verification**

Run:
- `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj`
- `rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter Ps2NativeBuildInputsTests|Ps2PlatformAssetBuilderTests`

Expected: remaining generated-runtime ownership is explicit and the rewrite families are no longer mutating files.

### Task 6: Delete The PS2 Normalization Layer

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\builder\Ps2NativeBuildExecutor.cs`
- Modify: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildExecutorTests.cs`
- Modify: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildInputsTests.cs`

- [ ] **Step 1: Remove `NormalizeGeneratedCoreSources(...)` from the build flow**

Delete the build-step call that mutates generated output before Docker `make`.

- [ ] **Step 2: Delete dead normalization helpers**

Remove `NormalizeGeneratedCoreFile(...)`, `NormalizeGeneratedCoreSource(...)`, and the specialized normalization helpers that are no longer needed.

- [ ] **Step 3: Keep only contract validation that checks upstream correctness**

Retain or tighten validation only where it proves generated output already matches the required contract. No helper may rewrite file contents.

- [ ] **Step 4: Update PS2 tests to reflect rewrite-free behavior**

Change PS2 builder tests so they fail if generated-output mutation returns in the future.

- [ ] **Step 5: Run the PS2 builder suite**

Run: `rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj`

Expected: the entire PS2 builder suite passes with zero generated-output rewrites.

### Task 7: Full Cross-Repo Verification

**Files:**
- Modify: none unless a failing test reveals a missing migration step

- [ ] **Step 1: Verify `csharpcodegen` stayed out of the PS2 migration**

Run: `rtk git diff --stat`

Workdir: `C:\dev\helworks\csharpcodegen`

Expected: no PS2-specific adapter or rewrite work was introduced as part of this migration.

- [ ] **Step 2: Run the relevant Helengine editor suite**

Run: `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj`

Workdir: `C:\dev\helworks\helengine`

Expected: editor generation contract changes pass their test coverage.

- [ ] **Step 3: Run the PS2 builder suite again**

Run: `rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj`

Workdir: `C:\dev\helworks\helengine-ps2`

Expected: PS2 consumes generated output without rewriting it.

- [ ] **Step 4: Review git status in all three repos**

Run:
- `rtk git status --short`

Workdir:
- `C:\dev\helworks\helengine`
- `C:\dev\helworks\helengine-ps2`
- `C:\dev\helworks\csharpcodegen`

Expected: only intended Helengine and PS2 changes remain; `csharpcodegen` has no new PS2 work.

- [ ] **Step 5: Prepare the integration summary**

Document:
- which rewrite families moved into `helengine.editor`
- which behaviors moved into native runtime code
- which PS2 validations remain
- confirmation that `csharpcodegen` remained generic
