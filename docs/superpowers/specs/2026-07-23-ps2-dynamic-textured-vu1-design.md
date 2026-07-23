# PS2 Dynamic Textured VU1 Rendering

## Goal

Reduce steady-state and moving-camera Colored Cubes rendering to `Drw <= 2.0 ms` while retaining the exact current perspective-correct texturing, per-material colors, and safe behavior for geometry crossing the near plane or camera.

## Current evidence

B62 records `Drw = 6.77 ms`, `Enc = 5.92 ms`, and `Gif = 0.001 ms` for 16 colored cubes / 192 triangles. The current textured VU1 microprogram only executes `xgkick` for a CPU-built GIF stream. The EE therefore performs world-view transforms, projection, reciprocal-W calculation, perspective texture coordinates, lighting, clipping classification, and GIF packet construction every frame.

## Architecture

The renderer gains a two-path textured opaque submission model.

1. A conservative bounds classifier places a whole opaque batch on the VU1 path only when every bound corner is strictly inside the near plane and screen frustum. This classifier is deliberately conservative: uncertain or intersecting bounds use the existing CPU direct-GIF encoder.
2. The VU1 fast path receives source positions, UVs, a world-view-projection matrix, per-batch color, and shared GS state. Its loop transforms all vertices, calculates reciprocal W, produces perspective-correct STQ/RGBAQ/XYZ2 payloads, and kicks bounded GIF output with `xgkick`.
3. The current CPU direct-GIF encoder remains the fallback for any batch that can touch a clip plane. This retains the established clipping behavior and prevents both vertex explosion and affine texture warping.

The renderer partitions batches before encoding; it never mixes CPU and VU1 vertices inside the same triangle. The result is visually identical to B62 for fully visible batches and uses the existing correct renderer for every risky batch.

## VU1 packet layout

Each bounded VU1 packet contains:

- a shared header: WVP matrix, GS scale/offset, TEX0/TEX1/TEST/PRIM state, texture dimensions, and output-buffer metadata;
- a sequence of batch headers: per-batch triangle count and flat material color;
- triangle source records: three local positions plus three UV pairs.

The microprogram consumes the records sequentially, emits a single packed GIF register stream per bounded packet, and uses a register list of `RGBAQ`, `ST`, and `XYZ2` after shared texture/primitive state is emitted once. Output is bounded by VU1 memory and packet limits; larger sets are split by whole triangle records.

## Correctness and fallback

The bounds classifier uses a small safety epsilon. It must not classify a batch as VU-safe when any corner can reach or cross a clip plane. If texture state, alpha mode, unsupported material behavior, a missing texture, or packet capacity cannot be represented by the VU1 layout, the batch uses the current CPU path.

Perspective correctness is mandatory: VU1 calculates `Q = 1 / clipW`, emits `S = U * Q`, `T = V * Q`, and writes the same Q into RGBAQ. No affine fallback is permitted.

## Instrumentation and validation

The performance record will distinguish VU1 fast-path batches/triangles from CPU fallback batches/triangles and report their packet bytes. The Colored Cubes run is successful only when the host log shows stable `Drw <= 2.0 ms`, correct multi-color cube output, and no GPU/GIF regression. Tests cover conservative fallback selection, VU packet capacity splitting, perspective-Q source requirements, and that the CPU clipping path remains available.
