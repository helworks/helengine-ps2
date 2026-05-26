## Summary

The current PS2 texture pipeline accepts generic per-platform texture settings from the editor, but the PS2 builder/runtime only support one cooked runtime format: 32-bit RGBA uploaded as `GS_PSM_CT32`. That is sufficient for correctness, but it wastes scarce GS eDRAM and causes practical failures for larger UI textures such as the `DemoDiscMainMenu` logo.

This design adds PS2-native support for the generic shared-engine formats that map cleanly onto the GS texture model:

- `Rgba32`
- `Indexed8`
- `Indexed4`

It does not add support for generic `Rgba4444` in this pass. The PS2 has 16-bit direct-color formats, but generic shared-engine `Rgba4444` is not treated here as a guaranteed 1:1 PS2 runtime payload contract.

The editor remains generic. It continues to publish per-platform texture settings through existing texture capability metadata. `helengine-ps2` narrows the PS2-advertised format list to the formats it truly supports and maps those generic settings into PS2-owned cooked payload metadata and native GS upload state.

## Goals

- Reduce PS2 texture VRAM pressure for UI and 3D assets by supporting indexed PS2-native texture payloads.
- Keep the shared editor contract generic and driven by per-platform asset settings.
- Ensure PS2 runtime texture upload uses payload metadata instead of hardcoded `GS_PSM_CT32`.
- Keep failure semantics strict: no runtime fallback, no builder-side guessing, no post-cook rewriting.

## Non-Goals

- Support generic `Rgba4444` in the PS2 path.
- Add mipmapping.
- Add PS2-specific texture settings UI in the editor.
- Add builder-side recovery for invalid or unpublished texture formats.

## Current Problem

Today the PS2 builder advertises generic texture settings, but the cooked runtime payload and native loaders only accept one format:

- `Ps2RuntimeTextureCooker` requires processed textures to remain `Rgba32 + A8`
- `Ps2TextureAsset` stores only a coarse `Ps2TextureFormat` enum plus raw pixel/palette blobs
- `Ps2BootHost.cpp` and `Ps2RenderManager3D.cpp` reject anything except `Ps2TextureFormat::Rgba32`
- both native loaders hardcode `GSTEXTURE.PSM = GS_PSM_CT32`
- CLUT data is not represented as first-class runtime upload state

This means the shared texture processor can already produce indexed textures, but the PS2 path cannot consume them.

## Architecture

### 1. Published PS2 texture capability formats

`Ps2PlatformDefinitionFactory` will advertise only these generic texture color formats for PS2 texture cook capabilities:

- `Rgba32`
- `Indexed8`
- `Indexed4`

It will no longer advertise generic `Rgba4444` for PS2.

This keeps the contract aligned with the editor capability model. The editor should only offer and serialize settings that the selected platform publishes.

### 2. PS2 cooked texture payload metadata

`Ps2TextureAsset` will be extended so the cooked runtime payload carries the native metadata required by the GS runtime loaders. The payload must describe:

- width
- height
- texture pixel storage mode
- CLUT pixel storage mode when applicable
- pixel payload bytes
- CLUT payload bytes when applicable

The payload is authoritative. Runtime loaders must not infer PS2 upload state from shared generic format names after cook time.

### 3. Builder mapping from generic format settings to PS2 payloads

`Ps2RuntimeTextureCooker` will continue to use the shared `TextureAssetProcessor` first. That preserves the current editor-owned per-platform settings flow for:

- max resolution
- generic color format selection
- alpha precision quantization

After shared processing, the PS2 cooker will map processed textures into PS2-owned cooked payloads:

- `Rgba32 -> GS_PSM_CT32`, no CLUT
- `Indexed8 -> PSMT8`, CLUT required
- `Indexed4 -> PSMT4`, CLUT required

If a malformed manifest reaches the PS2 builder with an unpublished or unsupported format, the builder throws immediately. That is treated as a contract violation, not a supported path.

### 4. Runtime texture upload

Both PS2 native loaders must consume the cooked payload metadata directly:

- `src/platform/ps2/Ps2BootHost.cpp`
- `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`

They will:

- stop requiring `Ps2TextureFormat::Rgba32` only
- populate `GSTEXTURE.PSM` from the cooked payload
- populate CLUT fields from the cooked payload when present
- allocate VRAM for both texel data and CLUT data when required
- upload textures through the existing gsKit upload path without silently falling back to `CT32`

This design assumes the runtime will also enforce GS texture-cache discipline after new texture or CLUT upload if required by the chosen upload path.

## Data Contract

### Supported generic editor-side settings for PS2

PS2 texture cook capabilities will expose:

- color formats:
  - `Rgba32`
  - `Indexed8`
  - `Indexed4`
- alpha precision:
  - existing shared precision values remain available through the generic settings contract

### Supported cooked PS2 runtime forms

First pass cooked runtime mappings:

- direct-color texture:
  - texel PSM = `GS_PSM_CT32`
  - no CLUT
- indexed 8-bit texture:
  - texel PSM = `PSMT8`
  - CLUT present
- indexed 4-bit texture:
  - texel PSM = `PSMT4`
  - CLUT present

The exact managed/native enum names should reflect PS2 runtime intent rather than shared editor nomenclature.

## Error Handling

- If the editor provides a texture work item with a format PS2 did not publish, the builder fails immediately.
- If the cooked texture payload is malformed or missing required CLUT data for indexed formats, runtime loading fails immediately.
- Runtime must not silently reinterpret indexed payloads as `CT32`.
- Runtime must not invent default CLUT data.

## Testing

Implementation will follow red/green/refactor.

Required tests:

1. Builder capability tests
- PS2 texture capability metadata publishes only `Rgba32`, `Indexed8`, `Indexed4`
- `Rgba4444` is absent from PS2-advertised texture formats

2. Cooker tests
- `Indexed8` source settings produce a cooked PS2 texture asset with indexed texel metadata plus CLUT payload
- `Indexed4` source settings produce a cooked PS2 texture asset with indexed texel metadata plus CLUT payload
- `Rgba32` still produces the expected direct-color payload
- malformed unsupported format input fails hard

3. Serialization tests
- PS2 texture assets round-trip the new payload metadata correctly

4. Native source-contract tests
- 2D loader no longer hardcodes `GS_PSM_CT32` for all PS2-owned textures
- 3D loader no longer hardcodes `GS_PSM_CT32` for all PS2-owned textures
- both loaders use payload-provided CLUT state when present

5. End-to-end verification
- Rebuild the city PS2 export with a PS2 asset setting that uses one indexed logo texture
- Verify `DemoDiscMainMenu` still boots
- Verify the menu logo renders
- Verify text and font atlas rendering still work

## Implementation Notes

- This work stays inside `helengine-ps2`.
- No engine-side PS2-specific path branching is required.
- The shared engine/editor side remains responsible only for publishing generic per-platform texture settings and emitting those chosen settings into platform cook work items.
- The PS2 builder/runtime remain responsible for translating supported generic settings into PS2-native cooked assets and GS upload state.

## Risks

- CLUT upload details may require careful gsKit/GS cache handling beyond the current direct-color path.
- Indexed palette ordering must match what the runtime sampler expects.
- Existing tests that assume `Rgba32` is the only PS2 cooked texture format will need to be narrowed or updated to assert per-format behavior instead.

## Acceptance Criteria

- PS2 publishes only supported texture formats to the editor.
- PS2 cooked texture payloads can represent direct-color and indexed runtime forms.
- Native 2D and 3D loaders consume cooked payload PSM/CLUT metadata directly.
- No PS2 runtime fallback to `GS_PSM_CT32` occurs for indexed payloads.
- `DemoDiscMainMenu` can use a smaller indexed logo texture through ordinary per-platform asset settings instead of PS2-specific engine code.
