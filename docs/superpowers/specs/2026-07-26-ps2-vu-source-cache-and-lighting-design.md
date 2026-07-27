# PS2 VU Source Cache and Lighting

## Goal

Reduce the Colored Cubes steady-state render cost from the B120 measurement of
`Drw 10.5 ms` / `Enc approximately 9.4 ms` to `Drw <= 2.0 ms`. The optimized
path must preserve per-material cube colors, diffuse lighting,
perspective-correct STQ texturing, opaque back-face culling, and safe behavior
when geometry reaches a clip plane.

## Evidence

B120 was measured through HelenUI OCR from the script-launched PCSX2 window.
It reports `Tri 192`, `Vif 0.0`, and `Gif 0.0`, while `3D` and `Enc` account
for the full render cost. The textured VU1 program already transforms local
positions, calculates reciprocal Q, writes STQ/XYZ values, and kicks GIF
output. The EE hot path instead rebuilds each source-triangle payload and
resolves a lit RGBA color for every triangle on every frame.

## Design

### Immutable source records

`Ps2VuTexturedPacketCache` becomes the single source for immutable textured
triangle data on both the CPU clipping path and the conservative VU1 fast
path. Its cached records contain local positions, local face normals, and UVs.
It remains bounded and keyed only by cooked model identity. It never caches a
world transform, camera, projection, light direction, material, texture, or GS
state.

The VU source-packing path consumes these records directly. It retains one
reusable packet-side source buffer so a frame does not allocate a temporary
vector for every batch.

### VU-side lighting

The textured VU1 shared state gains the values needed for flat diffuse
lighting: the material base color, the current world normal-direction matrix,
the normalized world light direction, and the material lighting constants.
Each triangle uses its cached local face normal, transforms and normalizes it
with the same W = 0 direction convention used by the current EE path, then
calculates its own RGBA value on VU1 while it produces its vertices. This
avoids the EE's per-triangle normal transform, normalization, intensity
calculation, channel multiplication, and RGBA packing.

The VU route remains limited to batches proven wholly inside the frustum.
Lighting behavior is deliberately diffuse-only on that fast path. It matches
the stable colored-cube look; unsupported lighting modes or material features
route to the existing CPU textured encoder.

### Correctness boundary

The existing CPU clipping encoder remains the mandatory fallback for any batch
that may meet the near, left, right, top, or bottom clipping planes. No clipped
triangle uses the VU fast path. The CPU path continues to generate
perspective-correct STQ and performs canonical back-face handling.

The VU output continues to use `PRIM.FST = 0`, `S = U * Q`, `T = V * Q`, and
`Q = 1 / clipW`. There is no affine-texture fallback.

### Instrumentation

The renderer exposes non-overlapping coarse timings for VU source-cache
resolution, VU source-payload packing, VIF packet assembly, and submission.
They use one timestamp per phase, not timestamps inside the triangle loop.
The overlay continues to show B-number, `Drw`, `3D`, `Enc`, `Vif`, `Gif`,
triangle count, batch count, and bytes so HelenUI OCR can measure every build.

## Validation

Source-contract tests first require VU fast-path cache use, local-normal
payload fields, VU lighting instructions, and the retained CPU fallback.
Focused native tests then verify the assembly is included and builds. A fresh
Colored Cubes ISO is built in the workspace-owned build directory, launched
only through `scripts\\launch_in_emulator.ps1`, and measured through HelenUI
OCR. Success requires correct lit multi-color cubes, no near-camera explosion
or affine warping during orbit-camera movement, and a steady `Drw <= 2.0 ms`.
