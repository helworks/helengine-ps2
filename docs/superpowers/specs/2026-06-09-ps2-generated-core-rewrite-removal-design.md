# PS2 Generated Core Rewrite Removal Design

## Goal

Remove all PS2 post-generation C++ rewrites from `helengine-ps2` and make the generated runtime structurally correct before native compilation.

## Ownership

The ownership split for this work is:

- `csharpcodegen`
  - remains generic C# to C++ translation infrastructure
  - must not gain PS2-specific output adapters, flags, or rewrite rules
- `helengine.editor`
  - owns Helengine runtime-generation semantics
  - consumes typed platform metadata from `helengine.baseplatform`
  - computes platform-driven runtime-generation decisions from the existing `RuntimeGenerationContract` plus other editor-owned generation inputs where required
  - emits Helengine source/runtime shape that translates correctly without PS2 post-processing
- `helengine-ps2`
  - declares PS2 platform capabilities and builder metadata
  - provides native runtime implementation for PS2-specific runtime behavior
  - validates generated output when required
  - never rewrites generated C++

## Non-Goals

- Removing existing Wii or GameCube generated-output adapters from `csharpcodegen` as part of this change
- Re-architecting unrelated platform packaging flows
- Rewriting generated files in a different layer

## Current State

`helengine-ps2` currently mutates generated core output in `builder/Ps2NativeBuildExecutor.cs` before the Docker toolchain compiles the translated runtime. The normalization pass edits a wide set of generated files, including:

- material-loading and render-manager contract files
- runtime graphics manifest files
- file I/O runtime files
- runtime asset-id generation files
- multiple generated component/runtime files that reflect stale emitted source shape

This is architecturally wrong for two reasons:

1. The builder is patching text after generation instead of consuming a typed contract.
2. Generated-source correctness depends on fragile exact string matches against emitter output.

There is also active work in `csharpcodegen` around generic generated-output adapters. That makes the boundary more important, not less important: PS2 must not become another `GeneratedRuntimeAdapter` case.

## Rewrite Inventory Classification

The existing PS2 rewrite inventory should be split into three categories.

### 1. Editor-owned runtime-generation contract and editor generation inputs

These are generated-runtime semantics and belong in `helengine.editor` generation decisions. Some are already modeled in the existing `helengine.baseplatform.Definitions.RuntimeGenerationContract`, while others may need contract extensions or existing editor generation inputs such as graphics-profile settings:

- cooked platform material registration
- cooked platform material resolution flow
- generated `RenderManager3D` contract surface
- runtime graphics manifest defaults such as HDR and post-process tier
- packaged-path behavior if it affects generated runtime shape
- asset-id normalization behavior if it affects generated runtime shape

### 2. Editor-owned emitted-source bugs

These are cases where Helengine-side emitted source shape is wrong or stale before translation:

- stale API references
- malformed generated expressions
- stale generated helper usage
- generated component code that only compiles after PS2 string replacement

These must be fixed at the Helengine source-generation layer before translation.

### 3. Native runtime implementation gaps

These are true platform runtime concerns and should live in runtime/native code rather than generated-output rewrites:

- disc-backed file access behavior such as `cdrom0:` dispatch
- other platform file-system or packaged-runtime integration that does not belong in generated source mutation

## Target Architecture

`helengine.baseplatform` already defines `RuntimeGenerationContract`, and `helengine.editor` must fully consume it. Where the current contract is insufficient, it should be extended rather than replaced. The contract must describe semantics, not platform names.

Current contract surface already includes:

- `RuntimeMaterialResolutionMode`
- `SupportsRenderManager2DTextureReleaseFlush`
- `PackagedPathPolicy`

Likely missing or incompletely consumed dimensions:

- runtime graphics feature tier
- runtime file access policy
- runtime asset-id normalization policy

The contract can use enums plus focused booleans where appropriate, but it must not collapse into loosely named free-form flags.

The PS2 builder should expose the metadata required for the editor to select the correct contract values through `helengine.baseplatform` definitions. The editor then emits Helengine runtime/source shape that translates through `csharpcodegen` without any PS2-specific post-processing.

## Migration Strategy

### Phase 1. Freeze and classify the rewrite inventory

Use `builder/Ps2NativeBuildExecutor.cs` as the authoritative inventory. Every existing normalization helper must be assigned to one of the three ownership buckets above.

### Phase 2. Introduce editor-owned runtime-generation contract

Extend the existing `RuntimeGenerationContract` and consume it correctly in `helengine.editor` so platform builders can declare the runtime-generation semantics they require.

## Classified Rewrite Inventory

The current PS2 rewrite inventory maps to the ownership buckets as follows.

### Editor-owned runtime-generation contract and generation behavior

- `RuntimeContentManagerConfiguration.cpp`
  - cooked platform material registration
- `RuntimeSceneAssetReferenceResolver.cpp`
  - cooked platform material resolution
- `RenderManager3D.hpp`
  - cooked platform material seam declaration
- `RenderManager3D.cpp`
  - cooked platform material default implementation
- `runtime/runtime_graphics_renderer_manifest.cpp`
  - HDR/post-process defaults

### Editor-owned emitted-source bugs or translation-shape drift

- `ScrollComponent.cpp`
  - `SizeValue(new int2())`
- `FontAsset.cpp`
  - `entry.get_Value()` and `entry.get_Key()`
- `AmbientLightComponent.cpp`
- `DirectionalLightComponent.cpp`
- `LightComponent.cpp`
- `PointLightComponent.cpp`
- `SpotLightComponent.cpp`
  - shared `LightType::hpp` output drift
- `FPSComponent.hpp`
- `FPSComponent.cpp`
- `Core.cpp`
  - `SceneTrace` string conversion shape
- `SceneManager.cpp`
  - `SceneTrace` string conversion shape
  - `DeleteGeneratedArray(...)` naming drift
- `RuntimeAssetIdGenerator.cpp`
  - canonical-key normalization shape

### Native runtime implementation gaps

- `system/io/file-stream.hpp`
- `system/io/file-stream.cpp`
  - PS2 disc-backed file access behavior

### Phase 3. Migrate material and renderer contract generation

Move the material-registration, scene-resolution, and `RenderManager3D` contract rewrites into editor-owned generation behavior first. This is the most important slice because it changes generated runtime contracts, not just incidental syntax.

### Phase 4. Migrate graphics manifest generation

Move HDR and post-processing defaults into editor-owned generation behavior so PS2 no longer patches generated runtime manifest files.

### Phase 5. Resolve path, file-I/O, and asset-id ownership

For each remaining rewrite, decide whether it is:

- generated-runtime contract owned by `helengine.editor`, or
- runtime implementation owned by native/shared runtime code

No remaining case may stay as PS2 post-generation text rewriting.

### Phase 6. Replace rewrites with assertions

During the transition window, `helengine-ps2` may keep strict validations that fail the build when generated output does not match the expected contract. It must stop mutating generated files.

### Phase 7. Delete the normalization layer

Delete `NormalizeGeneratedCoreSources(...)` and all normalization helpers once the editor and runtime changes are in place.

## Validation Strategy

Validation must move upstream:

- `helengine.editor` tests should verify that platform-driven generation emits the correct runtime/source shape
- `helengine-ps2` tests should verify that the builder consumes generated output without any rewrite pass
- no validation should depend on silently fixing bad generated output

## Success Criteria

The migration is complete when all of the following are true:

1. `helengine-ps2` no longer rewrites generated C++ output.
2. PS2 native build tests pass using freshly generated output.
3. PS2 runtime-specific behavior is implemented either through editor-owned generation contract or native runtime code, not string replacement.
4. `csharpcodegen` does not gain any PS2-specific generated-output adapter or rewrite path.
