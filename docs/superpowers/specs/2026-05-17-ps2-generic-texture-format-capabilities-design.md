## Summary

PS2 should publish the same generic platform texture-format capability metadata shape that PSP already uses, while keeping PS2's runtime texture output format and builder-owned cook execution intact. The editor must discover supported formats, alpha precision options, valid combinations, and default serialized settings entirely from generic platform capability metadata. No PS2-specific editor UI should be added.

## Goals

- Publish generic builder-owned asset cook capabilities for `texture` and `font-atlas-texture` in the PS2 platform definition.
- Advertise supported `TextureAssetColorFormat` values, supported `TextureAssetAlphaPrecision` values, and valid color/alpha combinations through `PlatformTextureFormatCapabilityDefinition`.
- Provide default serialized platform settings for both texture cook kinds using the generic settings contract: `MaxResolution`, `ColorFormat`, `AlphaPrecision`.
- Keep the existing PS2 builder-owned cook work-item execution path on the same generic contract.
- Preserve PS2-native runtime output artifacts such as `Ps2TextureAsset`.

## Non-Goals

- No PS2-specific editor UI or editor-side platform-specific widget logic.
- No change to the PS2 runtime texture artifact schema.
- No expansion of PS2 supported formats beyond what the current cooker/runtime path actually supports.
- No new asset discovery logic in the PS2 builder.

## Current State

The PS2 feature worktree already has builder-owned cook work-item execution for `texture` and `font-atlas-texture`, but its settings contract is PS2-specific. `Ps2TextureCookSettings` currently carries `MaxResolution`, `Format`, and `AlphaMode`, and the default serializer emits `Ps2TextureFormat.Rgba32` plus `Ps2TextureAlphaMode.Full`. That contract does not match the generic platform texture capability system now used by PSP.

The existing decoded source texture flow is already generic enough to support the transition. The PS2 source texture decoder populates generic `TextureAssetColorFormat.Rgba32` and `TextureAssetAlphaPrecision.A8` values on `TextureAsset`. The current PS2 runtime path only materially supports one effective combination today: full-color RGBA with full alpha precision.

## Design

### Capability Metadata

`Ps2PlatformDefinitionFactory` will publish two builder-owned `PlatformAssetCookCapabilityDefinition` entries:

- `texture`
- `font-atlas-texture`

Both capabilities will:

- use the generic settings contract shape already consumed by the editor
- expose a `PlatformTextureFormatCapabilityDefinition`
- provide serialized default settings using generic texture enums

The supported PS2 metadata will initially be:

- color formats: `TextureAssetColorFormat.Rgba32`
- alpha precisions: `TextureAssetAlphaPrecision.A8`
- valid combinations:
  - `(TextureAssetColorFormat.Rgba32, TextureAssetAlphaPrecision.A8)`

This keeps the published metadata truthful to the current PS2 builder/runtime implementation instead of advertising unsupported paths.

### Settings Contract

PS2 texture work items will move to the same generic settings contract PSP uses:

- `MaxResolution`
- `ColorFormat`
- `AlphaPrecision`

Defaults for both `texture` and `font-atlas-texture` will be:

- `MaxResolution = 0`
- `ColorFormat = TextureAssetColorFormat.Rgba32`
- `AlphaPrecision = TextureAssetAlphaPrecision.A8`

The PS2-specific `Format` and `AlphaMode` fields will be removed from the work-item settings contract. The serializer will emit and consume the generic enum names directly.

### Builder Execution

The existing PS2 builder-owned work-item execution path remains in place. `Ps2PlatformCookWorkItemExecutor` will continue to:

- consume `SourceAssetPath`
- consume serialized platform settings from the manifest
- produce final `Ps2TextureAsset` outputs at `OutputRelativePath`

The change is only in how settings are represented and interpreted:

- deserialize generic `ColorFormat` and `AlphaPrecision`
- validate that the requested combination is supported by PS2
- map the supported generic combination onto the current PS2 runtime output behavior

Because PS2 currently supports only `Rgba32 + A8`, the runtime cooker can stay simple. It will not introduce multiple PS2 runtime encodings yet. Unsupported combinations should fail clearly rather than silently degrading.

### Font Atlas Textures

`font-atlas-texture` will use the same generic capability metadata and default settings as normal textures. Builder execution remains the same:

- the editor emits a `font-atlas-texture` work item
- the PS2 builder resolves the source atlas texture from the editor-provided source asset path
- the PS2 builder writes a `Ps2TextureAsset` at the declared output path

No separate PS2-only atlas capability contract will be introduced.

## Error Handling

- Deserialization must reject missing or malformed generic texture settings payloads.
- Builder execution must reject unsupported `ColorFormat` / `AlphaPrecision` combinations explicitly.
- Capability metadata and defaults must remain internally consistent so the editor never emits a default combination the PS2 builder cannot execute.

## Testing

Focused PS2 builder tests will verify:

- both asset cook capabilities exist
- `texture` defaults are correct
- `font-atlas-texture` defaults are correct
- both capabilities expose the expected supported color formats
- both capabilities expose the expected supported alpha precisions
- both capabilities expose the expected valid color/alpha combinations

Existing PS2 work-item execution tests should continue to pass on the same generic contract. Validation for this slice will be:

- focused PS2 builder test slice
- confirmation that the PS2 platform definition advertises the generic texture-format metadata correctly for both texture cook kinds

## Implementation Notes

- Reuse PSP's generic capability structure as the reference shape, but do not copy PSP's supported formats or defaults.
- Keep the change scoped to metadata plus the already-existing PS2 work-item settings/execution path.
- Do not add new editor-side PS2 branches.
