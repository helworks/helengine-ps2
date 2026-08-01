# PS2 VU1 Full-Frustum Textured Clipping Design

## Goal

Render textured triangles continuously across camera and screen-frustum boundaries without explosions, whole-triangle popping, affine texture warping, winding regressions, or normal-view performance loss.

The optimized textured VU1 fast path remains unchanged. Only conservatively intersecting source slices use the clipping program.

## Root Cause

B314 is stable because `Ps2OpaqueTexturedClipDraw3D.vsm` rejects a whole triangle whenever any vertex receives an unsafe hardware clip flag. Its polygon clipper is deliberately unreachable: safe triangles jump directly to projection, unsafe triangles jump to the loop tail, and `texturedClipEmitPolygon` also exits immediately. The scratch polygon is never seeded.

The missing operation is geometric clipping between homogeneous transformation and reciprocal-W projection. The fast program is correct for fully safe slices.

## Invariants

- Never divide a vertex until positive finite `W` and frustum membership are proven.
- Clip against camera safety, near, left, right, bottom, and top boundaries.
- Interpolate homogeneous XYZW and raw UV before projection.
- Emit `S=U/W`, `T=V/W`, `Q=1/W` with `PRIM.FST=0`.
- Apply opaque winding rejection after clipping and projection.
- Preserve explicit double-sided materials.
- Uncertain bounds use the clipped route, never the fast route.

## Architecture

The EE retains one conservative bounds classification per source slice:

- `Fast` uses the unchanged compact textured microprogram.
- `Rejected` omits a slice wholly outside any plane.
- `Clipped` uses the full-frustum microprogram with bounded eight-triangle submissions.

The diagnostic switch forcing every slice through the clipping program is disabled. Cached source references, texture state, lighting state, and the fast submission capacity remain unchanged.

## VU1 Data Flow

For each clipped source triangle, VU1 transforms three positions to homogeneous XYZW, calculates lighting once, and seeds a three-vertex scratch polygon. Two fixed ping-pong buffers then run Sutherland-Hodgman clipping against camera-W safety, near Z, and four side planes.

Hardware `CLIPW` flags are authoritative for membership. Every flag result is captured before another `CLIPW`, and `FCAND`/`FCGET` registers never overwrite live loop counters.

Crossing edges use `t=dA/(dA-dB)`. Near-zero denominators are rejected; `t` is clamped to `[0,1]`; XYZW and raw UV use the same interpolation value; generated positions are snapped to the active boundary.

The surviving convex polygon is triangulated as a fan. Each output vertex is validated before `DIV`, then projected and encoded as perspective-correct STQ. Winding is tested after projection, accepted triangles are compacted, and GIF `NLOOP` is derived from the emitted vertex count.

## Memory and Failure Safety

Shared constants must prove the worst-case polygon and fan size for all six safety boundaries. Compile-time assertions cover source input, shared state, both scratch buffers, compact GIF output, and the 1024-qword VU1 limit. If capacity requires smaller clipped submissions, only that route shrinks.

Development builds treat polygon overflow as a hard failure. The production guard rejects the affected triangle without writing out of bounds and increments an overflow counter.

## Performance

Safe views execute the existing fast VU1 program byte-for-byte and perform no per-triangle CPU clipping. The EE classifies once per source slice. Only intersecting slices use smaller submissions and longer microcode.

Normal-view 3D time may regress by at most 0.2 ms. Camera-intersection overhead in the Tilt render test should remain near 0.5 ms. Production builds contain no per-triangle timers or verbose clipping logs.

## Validation

Automated contracts cover route classification, unchanged fast-program source, production diagnostic switches, all clipping planes, edge interpolation, pre-`DIV` validation, fan compaction, dynamic `NLOOP`, post-projection winding, and compile-time memory bounds. Native assembly is mandatory because source tests cannot prove VU execution.

PCSX2 validation starts with `test_scene_tilt_trial_level_01_render`, then the complete render test, then the full DemoDisc. HelenUI OCR records build identity, route counters, emitted triangles, and timing; the user judges visual clipping continuity.

## Acceptance Criteria

- Crossing triangles remain visible as their geometrically clipped portions.
- No invalid `W`, giant triangle, flashing, inversion, or unrelated-face disappearance occurs.
- Side-plane and camera-plane crossings are stable.
- Textures remain present and perspective-correct.
- Single- and double-sided behavior remains correct.
- Safe-view and intersecting-view timing meet the stated budgets.
- The full DemoDisc enters and leaves affected scenes without crashing.
