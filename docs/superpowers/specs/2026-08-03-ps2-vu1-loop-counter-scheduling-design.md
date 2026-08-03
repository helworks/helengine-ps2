# PS2 VU1 Textured Loop Counter Scheduling Design

## Problem

The isolated two-triangle clipping probe submits two fast textured source triangles, but synchronized B331 VU1 output readback reports three emitted triangles. At the normal camera position, triangle C is degenerate at screen center. After the camera intersects the face and backs away, triangle C becomes the visible malformed outside triangle while triangles A and B remain coherent.

Both active textured VU1 microprograms decrement `VI01` and branch on `VI01` in the immediately following instruction. Athena schedules an independent instruction or stall between equivalent integer-counter writes and dependent branches. The current ordering allows the branch to observe the previous counter value and execute one extra iteration, consuming an unwritten source record.

## Selected Design

Reorder the existing loop-tail instructions in both active textured VU1 microprograms:

1. Decrement `VI01`.
2. Advance the independent source pointer `VI05`.
3. Branch on `VI01`.

This schedules the dependency without adding instructions or increasing the per-triangle loop length. No CPU clipping, route classification, culling, packet layout, GIF state, or GS behavior changes are included.

The affected programs are:

- `Ps2OpaqueTexturedDraw3D.vsm`
- `Ps2OpaqueTexturedPretransformedDraw3D.vsm`

## Alternatives Considered

- Insert a `NOP` between decrement and branch. This is simple but adds one VU1 cycle per triangle.
- Replace the counter loop with an end-pointer comparison. This is more invasive and introduces unnecessary register and scheduling changes.

The independent-instruction reorder is preferred because it is the smallest zero-cost correction.

## Verification

Add a focused source contract that fails while either active textured program branches immediately after decrementing `VI01`. The contract must require the exact decrement, independent pointer advance, and branch order.

After the test fails for the existing ordering, apply the reorder and verify:

1. The focused source contract passes.
2. Existing focused textured VU1 contracts pass.
3. A fresh isolated B332 PS2 artifact builds and launches.
4. HelenUI reports `F2 C0 G0 N2` at the normal camera position.
5. Intersecting the face and backing away does not produce triangle C or the outside-triangle artifact.
6. The deep-inside traversal does not produce an extra triangle.
7. Draw performance shows no regression attributable to the loop-tail change.

## Success Criteria

The fix is accepted only when two source triangles produce exactly two VU1 output triangles at every tested camera position, with no outside or deep-inside extra triangle and no additional per-triangle VU1 instruction cost.
