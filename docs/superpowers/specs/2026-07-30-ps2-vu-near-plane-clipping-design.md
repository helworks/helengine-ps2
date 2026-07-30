# PS2 VU1 Near-Plane Clipping Design

## Objective

Prevent textured geometry from exploding, warping, or producing invalid screen-space triangles when a vertex reaches or crosses the camera near plane. The solution must retain perspective-correct STQ texture mapping, single-sided winding, and the performance of the B280 fast textured VU1 path.

## Acceptance Criteria

- No vertex is perspective-divided until it is known to be in front of the near plane or has been generated on that plane.
- A triangle crossing the near plane remains visually stable and is geometrically clipped instead of being discarded or projected with an invalid `W`.
- A triangle fully behind the near plane emits no GIF vertices.
- Textured output remains perspective-correct with `S = U / W`, `T = V / W`, `Q = 1 / W`, and `PRIM.FST = 0`.
- Opaque single-sided winding is evaluated after clipping and projection.
- The normal-view 3D time remains within 0.2 ms of the B280 baseline.
- Geometry intersecting the near plane adds no more than approximately 0.5 ms in the target test scene.
- The textured path does not fall back to per-triangle CPU clipping.

## Root Cause

The B280 textured microprogram transforms each source vertex to homogeneous clip space and unconditionally calculates `Q = 1 / W`. As a vertex approaches the camera plane, `W` approaches zero; after it crosses the camera, `W` becomes negative. Dividing before near-plane classification produces unbounded or inverted XY and STQ values, which the GS receives as giant or unstable triangles.

The engine uses a right-handed DirectX-style projection matrix. Its valid homogeneous near half-space is `clipZ >= 0`, and the exact near-plane boundary is `clipZ = 0`. Clipping can therefore be performed directly in homogeneous clip space before any reciprocal-W operation.

## Hardware Clip-Flag Triangle-Drop Diagnostic

The B295 diagnostic proved that the clipping microprogram's manual `FMAND` safety checks can accept an unsafe vertex and still allow reciprocal-W projection to explode. The next diagnostic therefore follows Athena's established VU1 pattern and uses the hardware `CLIPW` instruction with clip-flag reads instead of manually composing homogeneous side-plane signs from MAC flags.

The existing fast textured microprogram remains unchanged. Only slices already routed to the clipping microprogram use this diagnostic. For each triangle, the clipping program transforms all three vertices, evaluates each vertex with hardware clip flags, and discards the entire triangle before `DIV` when any vertex is outside X/Y/W safety or behind the DirectX-style near plane. Safe triangles continue through the proven compact emitter.

This diagnostic intentionally does not interpolate crossing edges or generate a triangle fan. Its accepted visual compromise is localized triangle popping at the camera plane. Vertex explosion, invalid reciprocal-W projection, whole-slice disappearance, and normal-view fast-path regression are not accepted. If this diagnostic is stable, hardware clip flags become the trusted classifier for the final geometric clipper; triangle dropping itself remains a fallback rather than replacing the design's final clipping requirement.

## Architecture

The renderer will use two textured VU1 routes:

1. The existing B280 fast microprogram remains unchanged for source slices proven to be fully in front of the near plane.
2. A dedicated clipping microprogram handles only slices whose conservative bounds intersect the near plane.

Each cached 32-triangle source slice stores immutable local-space center and extents bounds calculated when its packed triangle source is created. During packet assembly, the EE transforms the near-plane distance interval of those bounds using the current world-view-projection matrix.

- If the entire interval is in front of the near plane, the slice uses the fast microprogram.
- If the entire interval is behind the near plane, the slice is rejected before VIF submission.
- If the interval intersects the plane, the slice uses the clipping microprogram.

The bounds test must be conservative. Numerical uncertainty or contact with the classification epsilon selects the clipping route, ensuring the fast route never receives a vertex that may have an invalid near-plane `W`.

Slices should be grouped by route where practical so microprogram switches do not alternate unnecessarily inside one submission. Both routes consume the same packed source positions, UVs, normals, material state, and texture state.

## VU1 Clipping Data Flow

For each triangle in an intersecting slice, the clipping microprogram performs these operations:

1. Transform all three positions into homogeneous clip coordinates and retain `X`, `Y`, `Z`, and `W`.
2. Classify each vertex against `clipZ >= epsilon` before executing `DIV`.
3. Reject the triangle when all three vertices are outside.
4. Use the existing projection and emission sequence when all three vertices are inside.
5. Clip mixed triangles against the near plane with one-plane Sutherland-Hodgman clipping.
6. Interpolate each crossing edge with `t = (epsilon - Za) / (Zb - Za)`.
7. Interpolate homogeneous clip position and raw UV using the same `t`, clamp `t` to `[0,1]`, and place the generated position exactly on the safe plane.
8. Triangulate the resulting three- or four-vertex polygon as a fan, producing one or two triangles.
9. Perspective-divide only accepted or generated vertices, producing perspective-correct STQ.
10. Perform projected winding rejection, compact accepted output, and update the final GIF `NLOOP` from the emitted vertex count.

The edge denominator uses a small threshold. A numerically degenerate crossing produces no generated triangle rather than issuing an unstable reciprocal. Zero-area projected triangles are also discarded before GIF emission.

## Memory Safety

One 32-triangle input slice can expand to at most 64 triangles after clipping against one plane. At nine output qwords per triangle plus the existing eight-qword GIF state template, the worst-case output uses 584 qwords. Beginning at output address `0x100`, it ends at qword 840 and remains below the 1024-qword VU1 data-memory limit.

The implementation must add compile-time layout checks for:

- Cached slice bounds and packed source alignment.
- Maximum input/shared-state extent below the output region.
- Maximum clipped output extent below the VU1 data-memory ceiling.

## Scope

This design fixes the current exploding textured VU1 route. The existing untextured path already performs CPU full-frustum clipping and remains unchanged. Both paths must continue to satisfy the equivalent visual invariants in `GRAPHICS.md`.

Only near-plane clipping is added to the fast textured route in this work. Side-plane clipping remains outside this implementation unless validation reveals that the current GS behavior violates an existing invariant independently of the near-plane defect.

## Diagnostics

Temporary or retained renderer counters will expose:

- Fast textured slices.
- Near-clipped textured slices.
- Near-rejected textured slices.
- Source triangles processed by the clipping route.
- Triangles emitted after clipping and winding rejection.

The FPS overlay keeps the hardcoded build identifier. HelenUI OCR will record the build number, 3D time, packet-assembly time, VIF submission time, slice-route counters, and emitted triangle count. Diagnostics must not add per-triangle timing calls to the production path.

## Validation

Automated validation will cover source-layout contracts, worst-case VU memory bounds, route classification, clipping-program assembly, dynamic GIF loop-count generation, and preservation of the material `DoubleSided` flag.

PCSX2 validation will use the newest hardcoded build and these cases:

1. A fully visible cube: confirms the fast route, B280-equivalent rendering, and the normal-view performance budget.
2. A cube crossing the near plane: confirms stable clipped faces, perspective-correct textures, valid winding, and the intersecting-view performance budget.
3. A cube fully behind the camera: confirms complete rejection without flashing, giant triangles, or `FPS: N/A`.
4. Oblique textured geometry: confirms clipping does not reintroduce affine texture warping.

The fix is accepted only when the scene remains stable while repeatedly crossing the near plane and HelenUI reports no normal-view regression beyond 0.2 ms and no intersecting-view overhead beyond approximately 0.5 ms.
