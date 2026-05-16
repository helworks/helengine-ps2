# Generated Core Capability Design

## Goal

Replace PS2 post-generation C++ source rewriting with a cross-platform runtime-generation contract owned by `helengine`, so generated player code is structurally correct before native compilation and fails early when a platform contract is missing.

## Problem

`helengine-ps2` currently mutates generated C++ in `builder/Ps2NativeBuildExecutor.cs` after the editor has already emitted generated core sources. That layer is brittle for three reasons:

1. Engine source drift silently invalidates string matches and reintroduces regressions.
2. Platform behavior is encoded as text replacement instead of typed generator inputs.
3. The rewrite layer mixes obsolete fixes, temporary drift workarounds, and PS2 diagnostics into one place with no reliable ownership boundary.

This architecture is wrong even when the rewrites happen to work. The generator should own generated source shape. The platform builder should only declare runtime capabilities.

## Current Rewrite Inventory

The current normalization pass contains three categories of rewrites:

### Obsolete PS2 contract rewrites

- `RuntimeContentManagerConfiguration.cpp`
  - registers `Ps2MaterialAsset` instead of `MaterialAsset`
- `RuntimeSceneAssetReferenceResolver.cpp`
  - resolves materials through `Ps2MaterialAsset` and `BuildMaterialFromCooked`
- `RenderManager3D.hpp` / `RenderManager3D.cpp`
  - injects `BuildMaterialFromCooked`

These contracts are already owned natively in current `helengine` sources and should no longer be rewritten by `helengine-ps2`.

### Temporary engine-drift workarounds

- `ScrollComponent.cpp`
  - rewrites `SizeValue(new int2())` to `SizeValue(int2())`
- `Core.cpp` / `SceneManager.cpp`
  - rewrites `SceneTrace` console logging through `String::GetCString(...)`
- `SceneManager.cpp`
  - strips `FlushReleasedTextures()`
  - renames `DeleteGeneratedArray(...)`

These are compatibility gaps between the generated runtime contract and one or more platform runtimes.

### Diagnostics only

- `RuntimeSceneAssetReferenceResolver.cpp`
  - injects `Logger.hpp`, `printf`, and material-resolution tracing

These should never live in a long-term generated-core normalization layer.

## Target Architecture

Generated runtime code should be controlled by one cross-platform runtime-generation contract emitted from `helengine.editor`.

The ownership split should be:

- `helengine.core`
  - defines platform-facing runtime abstractions and compile-time generation branches
- `helengine.editor`
  - owns generated-core production and consumes the runtime-generation contract
- `helengine-ps2`
  - declares platform runtime capabilities through platform metadata or generator options
- platform runtimes
  - implement the contracts they claim

`helengine-ps2` should stop editing generated C++ and only build/package the generated result.

## Runtime-Generation Contract

The contract must be cross-platform, not PS2-specific. It should describe runtime behavior families that other platforms can also map onto.

Preferred modeling rules:

- use enums for behavior families
- use booleans only for independent toggles
- branch on semantics, not platform names

Initial contract surface:

- `RuntimeMaterialResolutionMode`
  - `RawShaderBacked`
  - `CookedPlatformOwned`
- `SupportsRenderManager2DTextureReleaseFlush`
- `PackagedPathPolicy`
  - includes whether rooted packaged paths are allowed
- `GeneratedSourceCompositionMode`
  - only if amalgamation-specific naming constraints remain relevant

If additional variation points are discovered during migration, they should be added only when they represent reusable runtime behavior across platforms.

## Migration Rules

### Deletion-first policy

When a rewrite corresponds to behavior already owned in `helengine`, delete the PS2 rewrite instead of preserving it as a fallback.

### Strict failure policy

During transition, any remaining normalization helper must fail the build when its expected source shape is absent. Silent no-op rewrites are not allowed.

### Centralized generator ownership

If a generated source needs to differ by platform contract, that branch belongs in `helengine.core` and `helengine.editor`, not in `helengine-ps2`.

## Rollout Order

1. Delete obsolete PS2 material-related rewrites and the diagnostics-only rewrites.
2. Introduce the shared runtime-generation contract in `helengine`.
3. Move remaining live behavior differences behind that contract.
4. Reduce `Ps2NativeBuildExecutor.NormalizeGeneratedCoreSources(...)` to strict transitional assertions only.
5. Delete the normalization layer once the last compatibility gap is owned by the generator/runtime contract.

## Verification

Each migration slice must be verified in three layers:

1. `helengine.editor.tests`
   - generated source tests for the new contract branches
2. `helengine-ps2` builder tests
   - confirm the PS2 builder passes the correct contract and no longer depends on removed rewrites
3. one real PS2 export smoke test
   - confirm generated core, native build, and runtime startup still work

## Non-Goals

- redesigning unrelated renderer behavior
- building a giant platform capability framework up front
- preserving post-generation rewrites as a permanent escape hatch

## End State

The desired steady state is:

- generated source shape is owned by `helengine`
- platform builders declare typed runtime-generation capabilities
- platform runtimes implement those capabilities
- generation or build fails immediately when the contract is incomplete
- `helengine-ps2` no longer patches generated C++ text after generation
