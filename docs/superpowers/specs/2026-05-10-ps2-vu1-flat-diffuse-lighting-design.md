# PS2 VU1 Flat Diffuse Lighting Design

## Summary

This document defines the next PlayStation 2 renderer milestone after the recovered opaque VU1 cube path: match the spinning cube demo scene's directional face-brightness response while keeping the lighting computation on `VU1`.

The scope is intentionally narrow:

- real `VIF1 -> MSCAL -> XGKICK`
- real packed PS2 mesh positions and normals
- flat per-triangle diffuse lighting
- one directional light
- no CPU-side lighting

This milestone keeps the known-good CPU-side transform, clipping, and backface-culling path. The change is limited to how triangle color is produced and kicked to the `GS`.

## Problem

The current recovered `VU1` opaque path renders a stable cube again, but it is effectively unlit:

- the host projects geometry on CPU
- the VU path emits flat geometry with a constant base color
- the spinning cube does not match the existing demo scene's directional light response

The next useful milestone is not "general materials." It is narrower: as the cube rotates, each hard face must brighten and darken the same way the demo scene expects.

## Goals

- Keep the working packed `VU1` opaque renderer path intact.
- Move lighting evaluation onto `VU1`.
- Match the cube demo's directional face-brightness response closely enough that the rotating cube reads the same way as the existing scene.
- Use the already cooked packed normal stream instead of introducing a temporary CPU lighting fallback.
- Keep the packet layout deterministic and small enough to debug with direct captures.

## Non-Goals

- No specular term in this milestone.
- No emissive term in this milestone.
- No texture lighting in this milestone.
- No per-vertex Gouraud shading in this milestone.
- No migration of world/view/projection or clipping onto `VU1` in this milestone.
- No attempt to match every material mode yet.

## Decision Summary

### Lighting ownership

Lighting moves to `VU1`. The CPU remains responsible for:

- reading packed geometry
- world, view, and projection transforms
- near-plane clipping
- backface culling
- building screen-space `XYZ2`

The `VU1` program becomes responsible for:

- reading one triangle payload at a time
- computing one flat diffuse intensity per triangle
- modulating the material base color
- kicking `RGBAQ + XYZ2` to the `GS`

### Shading model

The first lit milestone uses flat per-triangle diffuse lighting only.

This is the correct trade-off for the spinning cube demo because:

- the cube is hard-edged
- the target visual is face brightness over rotation
- it keeps the packet and microprogram change small

### Match target

The explicit match target is:

- same directional-light response class as the demo scene
- same face-to-face brightness transitions as the cube rotates

The explicit non-target for this milestone is:

- exact full-material parity with the CPU path's roughness, specular, and emissive behavior

## Runtime Architecture

The lit `VU1` path continues to use the existing renderer entry point in [Ps2RenderManager3D.cpp](/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/Ps2RenderManager3D.cpp), but extends the current opaque path with directional-light data and flat normal data.

### Host-side responsibilities

[Ps2RenderManager3D.cpp](/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/Ps2RenderManager3D.cpp) is responsible for:

- resolving the active directional light direction from the scene using the existing light lookup
- normalizing that direction before dispatch
- passing the direction into the VU packet builder

[Ps2VuVifPacketBuilder.cpp](/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp) is responsible for:

- reading packed positions from the position block
- reading packed normals from the normal block
- continuing the existing CPU transform and clipping path
- producing one emitted triangle payload per surviving triangle
- uploading fixed-layout triangle payloads to `VU1`

### VU1 responsibilities

[Ps2OpaqueDraw3D.vsm](/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm) is responsible for:

- walking the triangle payload linearly from the unpack target
- reading one face normal and one light direction
- computing `max(0, dot(N, L))`
- applying ambient bias and diffuse scale
- multiplying the material base color by the final intensity
- emitting one flat `RGBAQ` followed by three `XYZ2` values

## Triangle Payload Layout

The lit milestone uses a fixed per-triangle unpack layout in `VU1` memory.

Each emitted triangle consumes six qwords:

1. `QW0`: vertex A screen-space `XYZ2`
2. `QW1`: vertex B screen-space `XYZ2`
3. `QW2`: vertex C screen-space `XYZ2`
4. `QW3`: face normal as `Nx, Ny, Nz, 0`
5. `QW4`: base color as `R, G, B, A`
6. `QW5`: light direction as `Lx, Ly, Lz, 0`

This layout is intentionally redundant:

- the light direction repeats per triangle
- the color repeats per triangle

That redundancy is acceptable in this milestone because it buys:

- a fixed-stride microprogram
- simple unpack logic
- low debugging ambiguity

If later optimization is needed, shared constants can move to dedicated constant qwords outside the per-triangle stream.

## Normal Source

The first lit milestone uses a flat face normal per emitted triangle.

Host-side normal selection is:

- read the three packed per-vertex normals from the normal block
- derive a single face normal for the emitted triangle
- the preferred derivation is the normalized average of the triangle's three packed normals

This preserves the packed normal source while producing a stable flat-shaded face result for the cube.

If the cube asset's vertex normals are already face-flat, the average and first-normal approaches will match. Averaging is still preferred because it is more robust for other hard-edged assets that are triangulated consistently.

## Lighting Equation

The first lit milestone uses a simple diffuse equation:

- `N = normalize(faceNormal)`
- `L = normalize(lightDirection)`
- `ndotl = max(0, dot(N, L))`
- `intensity = ambientBias + (ndotl * diffuseScale)`

Recommended constants:

- `ambientBias = 0.25`
- `diffuseScale = 0.75`

Final color:

- `litColor.rgb = baseColor.rgb * intensity`
- `litColor.a = baseColor.a`

These constants are chosen to:

- keep shadow-side faces visible
- preserve clear face-to-face contrast
- avoid a fully black back side unless later scene matching proves that necessary

## GIF Output Shape

The kicked GIF stream changes from the current colorless flat primitive path to a lit flat primitive path:

1. primitive start for flat-shaded triangle
2. one `RGBAQ`
3. three `XYZ2`

This is the smallest required packet change to make `VU1` own lighting while keeping the known-good screen-space geometry path intact.

## Error Handling And Diagnostics

The implementation should preserve the current bring-up discipline:

- keep packet-phase diagnostics available until lit capture is confirmed
- keep the post-present watch window until the lit cube is captured
- avoid reintroducing the helper-based start or end tag path that previously corrupted the packet layout

If the first lit build renders black or corrupt geometry, isolate in this order:

1. verify triangle count and packet bytes still match expected scale
2. verify the unlit `XYZ2` payload still renders when the `RGBAQ` write is forced to a constant
3. verify the computed face normal and light vector values on host for one known triangle
4. verify the `VU1` output packet shape with a fixed debug color derived inside the microprogram

## Testing Strategy

The milestone is complete when all of these are true:

1. The real packed `VU1` opaque path still renders the cube without geometry corruption.
2. The cube is visibly lit by direction, not by CPU-side baked color.
3. As the cube rotates, hard faces brighten and darken in the same pattern as the existing demo scene.
4. The renderer still survives through present and the capture path remains reliable.

## Implementation Order

1. Extend the host packet builder to read packed normals and upload the six-qword triangle payload.
2. Extend the renderer call path to pass the resolved directional light vector into the packet builder.
3. Update the `VU1` microprogram to compute flat diffuse intensity and emit `RGBAQ + XYZ2`.
4. Capture the spinning cube and compare the face-brightness pattern against the current demo target.
5. Only after the lit cube is stable, consider reducing payload redundancy or expanding to fuller material behavior.
