# PS2 Large-Model VU Packet Slicing

## Purpose

Allow cooked meshes with more source triangles than one PS2 VU packet can represent to render safely. This preserves cook-time tessellation at a 0.5 maximum edge length for the Tilt Trial Level 01 textured course geometry.

## Problem

The renderer currently treats each `Ps2VuOpaqueBatch` as indivisible. Textured aggregate packets allow at most 2,048 source triangles, while untextured aggregate packets allow at most 64. A single tessellated Level 01 course model contains 3,360 to 6,064 triangles, so it cannot be represented by the existing bounded aggregate route.

## Design

Introduce an internal opaque submission-slice descriptor. A slice contains one existing opaque batch, a first source-triangle index, and a source-triangle count. It never copies model data or creates scene-visible objects.

The render manager converts each opaque batch into consecutive, contiguous slices:

- Textured slices contain at most 2,048 source triangles.
- Untextured slices contain at most 64 source triangles.
- Existing small batches remain one slice and retain aggregation with compatible adjacent batches.

The VIF packet builder accepts slices and reads only each slice's range from the packed triangle stream. Textured slices retain the existing resolved texture, perspective-correct STQ coordinates, complete screen-frustum clipping, backface culling, and material-derived lighting. Untextured slices retain their existing VU setup and clipping behavior.

## Error Handling

The slicing helper rejects null models, zero-length invalid slices, source-triangle starts beyond the model, and ranges that exceed the model triangle count. The renderer must never raise a packet-capacity error merely because an otherwise valid model has more triangles than one packet.

## Tests

Add source-contract tests proving:

- A 6,064-triangle textured model is represented as three bounded slices: 2,048, 2,048, and 1,968 triangles.
- A large untextured model is represented by 64-triangle slices.
- The packet builder consumes a slice range rather than the whole packed model.
- The textured path continues to pass the resolved texture and uses the existing STQ, frustum-clip, and backface-cull logic.

Validate with the focused builder tests, a PS2 native build, and a full DemoDisc PS2 ISO containing the Level 01 0.5 tessellation configuration.

## Acceptance Criteria

The full Tilt Trial Level 01 PS2 build boots and enters the textured course scene without freezing. Its six tessellated course pieces remain visible with correct texture mapping and no vertex explosion or affine texture warping.
