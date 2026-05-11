# PS2 VU1 LitSimple Colored Diffuse Design

## Goal

Restore material-colored untextured diffuse lighting on the PS2 `VU1` opaque path while preserving the currently verified:

- stable cube rendering
- correct light-motion behavior during rotation
- real `VIF1 -> MSCAL -> XGKICK` dispatch path

This step is intentionally narrower than a full material-system or program-registry expansion. It exists to replace the current grayscale lighting workaround with real material-colored diffuse output for the untextured flat-lit path.

## Current State

The current milestone already proves:

- host-side triangle generation is stable
- world-space face normals are uploaded correctly
- the `VU1` program computes flat diffuse intensity
- the `VU1` program selects one `RGBAQ` helper qword and kicks a valid GIF packet

The remaining parity gap is color:

- the current host payload builds a grayscale `RGBAQ` palette
- the `VU1` program selects a shade bucket from that grayscale palette
- the visible result is lit correctly, but it is not material-colored

## Scope

This design changes only the `LitSimple` untextured flat-diffuse color path.

In scope:

- replace the grayscale palette with a material-colored palette
- keep the current intensity computation path
- keep the current `VU1` palette-index selection path
- keep the current helper-packet writeback and `xgkick` flow
- verify stable color-correct lighting on the spinning cube

Out of scope:

- textured lighting
- specular lighting
- emissive behavior
- full CPU-material-equation parity
- `Ps2VuProgramKind` expansion
- `VU1` direct packed-color synthesis without the helper-palette workaround

## Recommended Approach

Use a host-built colored `RGBAQ` palette and keep `VU1` responsible only for selecting the final entry.

Why this approach:

- it preserves the currently verified geometry and lighting-motion path
- it avoids reopening the fragile low-level `RGBAQ` packing problem inside the `VU1` microprogram
- it restores visible material color with the smallest possible change surface

## Architecture

### Host Responsibilities

The host packet builder in [Ps2VuVifPacketBuilder.cpp](/mnt/c/dev/helworks/helengine-ps2/.worktrees/ps2-vu1-flat-diffuse-lighting/src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp) remains responsible for:

- reading packed positions and normals
- building world-space face normals
- world/view/projection transformation
- near-plane clipping
- backface culling
- building the helper GIF packet template
- uploading the per-triangle lighting payload

The host changes only one aspect of that payload:

- replace the grayscale palette with a per-triangle colored palette derived from the material base color

### VU1 Responsibilities

The `VU1` microprogram in [Ps2OpaqueDraw3D.vsm](/mnt/c/dev/helworks/helengine-ps2/.worktrees/ps2-vu1-flat-diffuse-lighting/src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm) remains responsible for:

- loading the face normal and light-direction qwords
- computing the flat diffuse intensity
- converting that intensity into a palette index
- loading the selected `RGBAQ` qword
- writing that `RGBAQ` qword into the helper packet
- kicking the packet with `xgkick`

No geometry, packet-addressing, or dispatch behavior changes in this step.

### Renderer Responsibilities

The renderer in [Ps2RenderManager3D.cpp](/mnt/c/dev/helworks/helengine-ps2/.worktrees/ps2-vu1-flat-diffuse-lighting/src/platform/ps2/rendering/Ps2RenderManager3D.cpp) already passes the resolved directional light vector into the packet builder.

No new renderer-side behavior is required for this step.

## Data Layout

### Palette Shape

Keep the existing `16`-entry palette structure.

Each palette entry remains a valid helper-built `RGBAQ` qword:

- low 64 bits: `GS_SETREG_RGBAQ(r, g, b, a, 0x00)`
- high 64 bits: `GIF_REG_RGBAQ`

This keeps the current `VU1` writeback strategy intact.

### Triangle Payload

The current payload layout remains:

1. colored `RGBAQ` palette entries
2. helper GIF packet template
3. face normal
4. light direction
5. light constants

Only the palette contents change in this step.

## Lighting And Color Equation

The diffuse-light response stays the same:

- `N = normalize(worldFaceNormal)`
- `L = normalize(lightDirection)`
- `ndotl = max(0, dot(N, L))`
- `intensity = ambientBias + (ndotl * diffuseScale)`

Current constants remain:

- `ambientBias = 0.25`
- `diffuseScale = 0.75`

For palette entry `i`:

- `shade = i / 15`
- `intensity = ambientBias + (shade * diffuseScale)`
- `entry.rgb = baseColor.rgb * intensity`
- `entry.a = baseColor.a`

The host precomputes these entries per triangle using the triangle material base color.

The `VU1` program still chooses which entry to use per triangle based on the computed diffuse intensity.

## Error Handling And Diagnostics

This step must preserve the current debugging discipline:

- do not change the real `VIF1` dispatch path
- do not remove the verified world-space normal path
- do not reintroduce the old helper start/end corruption path

If the colored-diffuse step regresses, isolate in this order:

1. confirm the cube still renders with stable geometry
2. confirm the light-motion behavior is still correct
3. inspect one palette entry on the host for the material base color
4. verify the selected `RGBAQ` qword still writes into the same helper slot before `xgkick`

## Verification Criteria

This step is complete only when all of these are true:

1. The cube still renders stably through the real packed `VU1` path.
2. The lighting motion remains correct during rotation.
3. The cube is no longer grayscale-only.
4. The visible face color reflects the material base color under the same diffuse response.
5. None of these regressions return:
- vertex-point artifacts
- white rectangle flashes
- pinned bright-face behavior

## Follow-On Work

After this step is stable, the next architectural step should be:

1. expand `Ps2VuProgramKind`
2. update `Ps2VuProgramRegistry`
3. split the current path into explicit program variants such as:
- `Unlit`
- `LitSimple`
- `LitTextured`

Specular should remain a later step rather than being bundled into this colored-diffuse cleanup.
