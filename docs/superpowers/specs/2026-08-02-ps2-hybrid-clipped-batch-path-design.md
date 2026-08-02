# PS2 Hybrid Clipped Batch Path Design

## Purpose

Replace the branch-heavy per-triangle VU1 full-frustum clipping path with an exceptional clipping path that preserves the established fast Path 1 renderer. Fully visible textured geometry must remain on the existing high-throughput VU1 program. Only triangles that intersect the camera near plane or a screen-side plane are clipped on the EE, accumulated into material-coherent batches, and submitted to a small pretransformed VU1 program for perspective-correct GS packet generation.

This design follows the PS2 throughput guidance in `docs/PS2 Graphics Research.md` and the working Tyra clipping pattern under `C:\dev\helworks\reference\ps2\tyra`: clipping is exceptional EE work, while final packet generation remains batched Path 1 work with one `XGKICK` per VU-safe batch.

## Goals

- A triangle remains visible as its vertices cross the near or camera boundary; it must not explode or disappear as a whole.
- Perspective-correct textures remain perspective-correct after clipping. Affine texture warping is unacceptable.
- Fully visible geometry continues using the existing fast textured VU1 program without additional per-triangle clipping work.
- Fully invisible triangles are rejected before transport.
- Clipped geometry remains material- and texture-coherent and is emitted with one `XGKICK` per bounded batch.
- Single-sided materials retain the same winding behavior as the fast path. Explicit double-sided materials remain double-sided.
- The normal-view 3D regression remains at or below 0.2 ms in the isolated Tilt render test.
- Camera-intersection overhead should remain approximately 0.5 ms for the isolated Tilt render test and must not halve frame rate.
- Production builds contain no per-triangle timers, verbose clipping logs, or diagnostic bypasses.

## Non-goals

- The EE will not replace VU1 for normal transform, lighting, or packet generation.
- The clipped fallback will not use direct GIF Path 3 for ordinary textured geometry.
- The design does not add far-plane clipping; the existing far-plane behavior remains unchanged.
- The design does not change material cooking, texture cooking, tessellation settings, or scene authoring.
- The failed six-pass VU1 Sutherland-Hodgman program will not remain active as a second production clipping implementation.

## Architecture

The textured renderer keeps three routes:

1. **Fast**: the complete source triangle is safely inside the near and screen-side planes. It remains in the existing packed source stream and runs through `Ps2OpaqueTexturedDraw3D.vsm`.
2. **Rejected**: the complete source triangle is outside at least one plane. It is not submitted.
3. **Clipped**: the source triangle intersects one or more unsafe planes. The EE transforms and clips it into a bounded polygon, triangulates the polygon as a fan, appends the resulting pretransformed vertices to a material-coherent clipped batch, and submits that batch through a dedicated as-is VU1 program.

Outer source slices remain the amortized classification unit. If an outer slice intersects, its source triangles are refined individually so safe triangles return to the fast program and only genuinely intersecting triangles enter the exceptional path. The existing B317 single-triangle bounds API remains the refinement mechanism.

## Clipping Coordinate System

The clipped path follows the already working legacy PS2 CPU clipper and the engine's right-handed projection convention:

1. Transform local positions into view space.
2. Clip against the view-space near plane at `viewZ = -nearPlaneDistance`, retaining vertices where `viewZ <= -nearPlaneDistance`.
3. Transform retained positions into homogeneous clip space.
4. Clip against left, right, bottom, and top using distances `x + w`, `w - x`, `y + w`, and `w - y`, retaining distances greater than or equal to zero.

Clipping the actual view-space near plane makes a separate synthetic camera-W pass unnecessary. Every vertex retained by the near-plane pass has a valid positive projection W under the engine's projection matrix. Before reciprocal W, the batch builder still validates that W is finite and greater than the established minimum projection threshold. Invalid output is treated as a renderer invariant failure during development rather than silently projected.

Each plane pass uses Sutherland-Hodgman clipping with two fixed nine-vertex buffers. Crossing edges interpolate with `t = previousDistance / (previousDistance - currentDistance)`. The same clamped `t` is applied to view/clip position and raw normalized UV. Polygon sizes below three produce no triangles. Capacity overflow is an invariant failure because a triangle clipped against five planes cannot exceed the proven nine-vertex bound.

## Attribute Semantics

Each clipped vertex stores:

- Homogeneous clip position XYZW.
- Raw normalized texture coordinates UV.

Each clipped triangle retains the original source triangle's packed normal and material lighting inputs. The current renderer uses flat per-triangle lighting, so clipping does not need to invent or interpolate vertex normals. The as-is VU1 program computes the same diffuse lighting result as the fast program, then applies it to every generated fan vertex.

The as-is VU1 program computes `Q = 1 / W`, stores `S * Q`, `T * Q`, and `Q`, divides XYZ by W, applies the established GS scale and offset, converts only the final XYZ to GS fixed-point form, and writes the same `ST`, `RGBAQ`, `XYZ2` register order as the fast path.

## Batch and VU1 Memory Layout

Clipped fan triangles are accumulated by the same material, texture, GS context, and double-sided state. A clipped batch contains a shared state block followed by tightly packed triangle records. Each record contains three clip positions, three raw UVs, and one source normal/lighting record.

The source capacity is derived from the 16 KB VU1 data-memory contract, not hardcoded independently. Shared input, source records, GIF state, and maximum expanded output must fit without overlap in either VIF1 double-buffer window. When appending another triangle would exceed the proven capacity, the builder flushes the current clipped batch and begins another batch with the same state. Geometry is never truncated.

The program builds one dynamic GIF primitive tag for all accepted vertices in the clipped batch and issues one `XGKICK`. Rejected backfaces compact the output cursor and final GIF vertex count exactly as the fast program does. An empty accepted batch does not kick.

## Components

### Fixed clipping types

A dedicated clipped vertex type owns homogeneous position and raw UV. A dedicated fixed polygon buffer owns two arrays of nine vertices and exposes plane-pass operations without heap allocation. These types live in focused PS2 VU rendering files rather than adding more responsibilities to `Ps2VuVifPacketBuilder.cpp`.

### Exceptional clipped batch builder

A dedicated builder consumes one packed source triangle, world/view/projection transforms, near distance, and immutable material state. It returns zero or more fan triangles and appends them to a bounded clipped source batch. It owns capacity checks and flush boundaries but does not submit DMA itself.

### As-is VU1 program

A dedicated microprogram consumes pretransformed clipped triangles. It owns lighting, reciprocal-W projection, winding rejection, output compaction, dynamic GIF count, and `XGKICK`. It contains no polygon clipping loop.

### Packet routing integration

`Ps2VuVifPacketBuilder` keeps classification and packet orchestration. Fast and rejected routes remain unchanged. The clipped route delegates clipping and batch construction to the dedicated builder, then emits bounded VIF unpack and MSCAL commands for the as-is program.

## Failure Behavior

- Non-finite input positions, UVs, matrices, or interpolation results throw a descriptive renderer exception in debug validation.
- A source triangle index outside immutable packed model data throws as it does today.
- A clipped polygon exceeding nine vertices throws because it violates the mathematical and memory contract.
- An invalid reciprocal W is never sent to the GS.
- Packet capacity exhaustion flushes the current clipped batch; it is not an error and never drops geometry.
- Native build failures remain hard failures. Generated VU output is fixed through its source or build step, never rewritten after generation.

## Performance Model

Safe triangles pay only the existing outer classification and B317 refinement behavior; they do not execute the clipped microprogram or copy into clipped buffers. Intersecting triangles pay EE transform and fixed-buffer clipping cost, but the result is accumulated and transported in qword batches. The as-is VU1 program has a regular triangle loop and one kick per batch, avoiding the six plane loops, nested branches, and repeated scratch-buffer traffic that cut B317-B320 intersection performance approximately in half.

Metrics must distinguish fast source triangles, rejected source triangles, clipped source triangles, generated clipped triangles, and clipped batch count. These counters are aggregate integer increments only. They must not add clock reads inside per-triangle loops.

## Verification

### Automated contracts

- Fixed clipping tests cover fully inside, fully outside, one vertex outside, two vertices outside, side-plane crossings, near-plane crossings, UV interpolation, fan ordering, nine-vertex capacity, and degenerate edges.
- Packet layout tests prove VIF input and GIF output regions fit both VIF1 double-buffer windows.
- Routing tests prove safe triangles use only the fast microprogram, rejected triangles emit nothing, and intersecting triangles use only the clipped batch path.
- Microprogram source tests prove reciprocal W precedes STQ and XYZ projection, output is compacted, single/double-sided behavior matches the fast path, and one non-empty batch has one `XGKICK`.
- Diagnostic source tests prove all temporary B318-B320 validation and winding bypasses are absent.

### PCSX2 validation order

1. Build `test_scene_tilt_trial_level_01_render` only with a new build number.
2. Verify a safe view remains within 0.2 ms of the established fast baseline.
3. Move into the deterministic large cube and verify faces remain continuous at near and side crossings without explosion, full-triangle disappearance, flashing, or double-sided regression.
4. Record fast, clipped, rejected, generated triangle, clipped batch, 3D, and frame metrics through HelenUI OCR where available; the user remains the authority for visual clipping continuity.
5. Build the full Tilt render test and confirm tessellated course geometry remains stable.
6. Build the full DemoDisc and exercise Stacked Boxes, Stacked Spheres, colored cubes, textured cubes, Tilt render test, and Tilt Play Scene 01.

## Rollout

The first isolated build uses the hybrid path only for textured opaque geometry, matching the current failing scope. The old full-frustum clipping microprogram is removed from runtime upload and routing once the isolated build is visually accepted. Untextured and alpha geometry retain their existing paths until they receive separate measured designs; this change does not silently broaden into those renderers.
