# Graphics Rendering Invariants

This document defines the visual-correctness rules for the engine renderer. Performance work is only valid when it preserves every invariant below.

## Non-Negotiable Correctness

- Perspective-correct texture mapping is required for textured geometry. Affine texture warping is unacceptable on every platform, including the PS2.
- On the PS2 STQ path, texture coordinates must be emitted as `S = U / W`, `T = V / W`, and `Q = 1 / W` with `PRIM.FST = 0`. Sending pixel UV coordinates, using `FST = 1`, or emitting an invalid/zero `Q` is a rendering regression.
- Triangles must be clipped before perspective division whenever they cross the camera near plane or any screen-frustum plane. Never project a vertex with an invalid or non-positive clip-space `W`.
- A vertex entering or moving behind the camera must not warp, explode, or reposition a mesh face. Giant or nearly full-screen triangles caused by a cube, coin, or other mesh crossing the camera are unacceptable.
- Textured and untextured opaque paths must apply equivalent full-frustum clipping: near, left, right, top, and bottom planes. Clipped textured vertices must retain perspective-correct texture interpolation.
- Textures must remain present and correctly bound. White/untextured geometry caused by an incorrect GIF register layout, texture state, or STQ setup is a rendering regression.

## Face Culling

- Opaque meshes are single-sided by default. Cull backfaces after clipping and projection using the engine's canonical winding test.
- Transparent or alpha-blended geometry remains double-sided unless its material explicitly defines different culling behavior.
- Do not silently make every mesh double-sided. It increases PS2 rasterization and texture-fill work and can conceal winding or clipping defects.
- Culling must not remove front-facing geometry or reintroduce camera-boundary artifacts.

## Performance Work

- Do not trade visual correctness for frame rate. A faster build with affine textures, missing textures, vertex explosions, or invalid culling is not an acceptable optimization.
- Prefer reductions in packet construction, redundant state setup, draw submission, and fill work while maintaining the invariants above.
- Measure on the newest PS2 build only. Use the hardcoded `B01`, `B02`, and subsequent build identifiers in the FPS overlay so stale ISOs are immediately apparent.

## Required Validation

- Test textured objects at oblique angles to confirm perspective-correct mapping.
- Move the orbit camera through and behind large geometry, especially cubes, and verify that no face explodes, stretches across the screen, or changes position unexpectedly.
- Check opaque backfaces are culled while expected alpha geometry remains visible from both sides.
- Confirm textured meshes are not rendered white or without their assigned textures.
- On PS2, capture the performance overlay with HelenUI OCR when measuring performance; do not manually inspect screenshots for OCR data.
