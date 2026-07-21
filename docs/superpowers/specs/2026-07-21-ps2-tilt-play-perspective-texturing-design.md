# PS2 Tilt Play perspective-correct texturing

## Goal

Correct affine texture warping on the textured cubes in the Tilt Play 01 render scene without changing the VU renderer, HDR glow path, scene content, clipping behavior, or the existing render-state setup.

## Scope

The active opaque CPU textured-triangle submission in `Ps2RenderManager3D.cpp` will use the GS perspective texture-coordinate path. Each vertex will emit `RGBAQ`, `ST`, and `XYZ2` in GIF-native order with `PRIM.FST` disabled. `Q` is derived from the projection-space `clipW` used for that vertex, and the submitted texture coordinates are normalized `S * Q` and `T * Q`.

The change deliberately retains the current CPU near-plane clipping, screen projection, z behavior, texture binding, and opaque batch ordering. HDR glow remains on its existing known-working path. The experimental VU packet builder remains unchanged.

## Rationale

The present opaque path calls the gsKit UV primitive helper, which submits affine texel-space UV coordinates. The GS supports perspective correction through STQ, so subdivision is neither required nor appropriate for cube faces. The previous broad STQ attempt made geometry invisible; this design avoids that risk by changing only the active textured primitive submission and preserving its surrounding working state.

## Validation

1. Add a source contract test covering the STQ register order, normalized texture-coordinate convention, and `clipW`-derived Q calculation.
2. Build the native PS2 target.
3. Package the existing Tilt Play 01 render scene through the editor CLI.
4. Launch that ISO in PCSX2 for visual confirmation that cube faces remain visible and no longer warp affinely.

## Failure handling

If the native build fails or the textured cubes disappear, revert only this packet-path change immediately. Do not alter the VU renderer or stack further packet experiments on top of a broken submission.
