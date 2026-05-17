# PS2 Platform-Owned Asset Cook Work-Item Pipeline Design

## Summary

This change moves PS2 platform-specific asset cooking onto the generic builder-owned `PlatformCookWorkItem` pipeline that the editor now emits. The PS2 builder will stop discovering platform-specific texture work by walking scene, material, or font references. Instead, it will execute the explicit work items in the manifest and write final PS2 runtime artifacts at the output paths declared by the editor.

The first supported work-item kinds are:

- `texture`
- `font-atlas-texture`

This slice also changes PS2 texture ownership end to end. PS2 will no longer rely on generic cooked `TextureAsset` runtime payloads. The PS2 builder will produce a PS2-native runtime texture asset format, and the PS2 runtime will load that format directly.

## Goals

- Execute editor-emitted platform cook work items from `PlatformBuildManifest.PlatformCookWorkItems`.
- Support PS2-owned cooking for `texture` and `font-atlas-texture`.
- Consume `SourceAssetPath` and `SerializedPlatformSettings` directly from the work item.
- Write final PS2 runtime texture payloads to the work-item `OutputRelativePath`.
- Use PS2 capability defaults when a source asset has no explicit PS2 override.
- Keep generic cooked blobs unchanged after staging.
- Avoid any PS2-specific asset discovery in the builder.

## Non-Goals

- Reworking scene packaging beyond consuming the outputs already declared in the manifest.
- Replacing the generic font asset or font metrics payload in this slice.
- Reworking PS2 material authoring schemas beyond pointing them at the final runtime texture paths they already consume.
- Expanding work-item execution beyond `texture` and `font-atlas-texture`.

## Current State

The editor now emits builder-owned `PlatformCookWorkItem` entries with:

- resolved source asset path
- source asset kind
- declared output path
- logical artifact id
- source/settings hashes
- serialized platform settings

The PS2 builder does not currently execute those work items. It stages `Manifest.CookedArtifacts`, then rewrites cooked asset references and packages the export. PS2 also does not currently publish `AssetCookCapabilities`, so the editor has no PS2 capability contract to target for builder-owned texture cooking.

The PS2 runtime still expects generic `TextureAsset` payloads. That is incompatible with the intended ownership model for this task.

## Design

### 1. Publish PS2 Builder-Owned Asset Cook Capabilities

`Ps2PlatformDefinitionFactory` will publish `AssetCookCapabilities` for:

- `texture`
- `font-atlas-texture`

Each capability will declare:

- the source asset kind it handles
- the PS2 target artifact kind for the runtime texture payload
- the serialized default PS2 texture processor settings used when an asset has no explicit PS2 override

The default settings will be PS2-owned settings, not a disguised generic runtime payload choice.

### 2. Add a PS2 Runtime Texture Settings Contract

PS2 needs its own serialized texture settings contract for builder-owned texture cooking. That contract will be carried through `SerializedPlatformSettings` and exposed in the editor through the generic platform settings path.

The contract will describe PS2 runtime texture output choices, such as:

- PS2 runtime texture format
- alpha handling mode
- max resolution

The exact set should stay minimal for this slice and only include fields needed to build the first PS2-native texture formats.

### 3. Add a PS2 Runtime Texture Asset Format

PS2 will get a dedicated runtime texture asset/schema that the builder writes and the runtime loads directly. This asset format replaces generic `TextureAsset` as the PS2 runtime payload for:

- regular textures
- font atlas textures

The format must carry the runtime data the PS2 loaders need without requiring a post-stage rewrite step. The payload should be final at the moment the builder writes it to the work-item output path.

### 4. Execute Work Items in the Builder

`Ps2PlatformAssetBuilder.BuildAsync(...)` will gain a work-item execution phase that runs before packaging and after basic request validation.

That phase will:

- read `request.Manifest.PlatformCookWorkItems`
- select only PS2-targeted work items
- execute supported work-item kinds
- write output files into `request.WorkingRoot\\ps2-staging\\<OutputRelativePath>`

Texture work items will:

- load the source image from `SourceAssetPath`
- deserialize the PS2 texture settings from `SerializedPlatformSettings`
- build the final PS2 runtime texture asset
- write the result exactly to the declared `OutputRelativePath`

Font-atlas-texture work items will follow the same flow, using the editor-provided source atlas image path and PS2 texture settings.

Unsupported or malformed work items should produce diagnostics rather than being silently ignored.

### 5. Narrow Artifact Staging Responsibility

`StageCookedArtifacts(...)` will stop acting as the source of truth for PS2-owned texture payload generation. Its responsibility becomes:

- staging generic cooked artifacts that are still editor-owned
- leaving PS2-owned runtime texture outputs alone

The builder will not rewrite generic cooked texture blobs after staging to simulate PS2 ownership. The PS2 texture artifact must already be final when written by the work-item executor.

### 6. Runtime Loading

PS2 runtime loading code in the boot host and render manager will be updated to load the new PS2 runtime texture asset format instead of assuming generic `TextureAsset`.

This keeps ownership consistent:

- editor resolves the source asset and settings
- builder produces the PS2-native runtime texture payload
- runtime loads the PS2-native payload directly

### 7. Font Atlas Scope

For this slice, only the atlas texture becomes PS2-native. The generic font asset and metrics payload may remain as-is if they already reference the atlas by path and do not require PS2-specific translation.

## Validation Plan

### Focused Builder Tests

Add focused tests in `builder.tests/Ps2PlatformAssetBuilderTests.cs` for:

- executing a `texture` work item from source image path to declared output path
- executing a `font-atlas-texture` work item from source image path to declared output path
- using PS2 capability default settings when no explicit PS2 override is present
- keeping the builder free of asset-reference walking for these work-item kinds

### Focused Source/Contract Tests

Add small tests where useful for:

- PS2 platform definition publishes the new asset cook capabilities
- the capability defaults serialize the expected PS2 texture settings contract
- runtime/build code targets the PS2 runtime texture asset type rather than generic `TextureAsset` for builder-owned outputs

### End-to-End Verification

Add at least one end-to-end verification proving:

- the editor-owned build graph emits PS2 work items
- the PS2 builder consumes those work items
- the final packaged artifact at the declared output path is PS2-native
- runtime-facing packaged references point at the final PS2-owned payload

## Risks

### Runtime Contract Mismatch

If the builder writes a PS2-native texture asset but runtime loaders still assume generic `TextureAsset`, the export will package successfully but fail at runtime. This is the highest-risk boundary and must be tested directly.

### Capability and Settings Drift

If PS2 capability defaults and the builder’s settings deserializer drift apart, assets without explicit PS2 overrides will cook incorrectly. The default-settings path needs explicit coverage.

### Accidental Dual Ownership

If staged generic cooked textures remain in use anywhere in the packaging path, the runtime could silently keep loading the wrong format. Tests should assert the final packaged path resolves to the PS2-native artifact written by the work item.

## Recommended Implementation Order

1. Publish PS2 asset cook capabilities and default serialized settings.
2. Introduce the PS2 texture settings/runtime texture asset contracts.
3. Add failing builder tests for `texture` and `font-atlas-texture` work-item execution.
4. Implement work-item execution in `Ps2PlatformAssetBuilder`.
5. Update runtime loaders to consume the PS2-native texture asset.
6. Add one editor-owned end-to-end verification for packaged PS2 output.
