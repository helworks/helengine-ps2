# PS2 Colored-Face Clipping Probe Design

## Purpose

The B322 hybrid textured clipping path is substantially more stable after correcting VIF1 submission ordering, but close-camera intersections still produce displaced triangles. The current tessellated, uniformly textured cube makes it difficult to identify whether a bad triangle belongs to the wrong source face, carries corrupt UVs, or has corrupt clip-space positions.

This diagnostic replaces only the isolated Tilt Trial Level 01 render-test cube with a non-tessellated, six-color textured cube. It preserves the textured PS2 VIF/VU renderer under investigation.

## Diagnostic Geometry

The probe remains one 5-by-1-by-5 cube at the origin, with the existing camera, directional light, FPS overlay, scene id, and scene composition unchanged. The probe model contains the canonical cube's 24 face-local vertices and 12 triangles. No cook-time model-import or MeshComponent tessellation setting is enabled for this model on any platform.

The model is probe-specific rather than a modification of the engine-generated shared cube. This prevents the diagnostic from changing other scenes or platforms.

## Face Identification Texture

One small 3-by-2 texture atlas provides six padded, solid-color cells. The probe model maps each face's four UVs into one cell:

| Face | Color |
|---|---|
| Back (-Z) | Red |
| Front (+Z) | Green |
| Right (+X) | Blue |
| Left (-X) | Yellow |
| Top (+Y) | Magenta |
| Bottom (-Y) | Cyan |

The atlas uses opaque colors, nearest filtering, and interior UV coordinates so filtering cannot sample an adjacent cell. The cube uses one textured material and one MeshComponent, preserving one material state and the same textured batching route used by B322.

## Asset and Scene Ownership

DemoDisc's game-scene generation owns the diagnostic model, atlas, material, and scene references. Generation writes deterministic file-backed assets under a probe-specific Tilt render-test asset directory. The scene factory receives the generated probe model and material through the existing rendering/game generation asset preparation flow.

The normal Tilt Trial course material, shared engine cube, playable Level 01 scene, and platform tessellation feature remain unchanged.

## Validation Contract

Automated validation proves that:

- the isolated render scene references the probe-specific model and textured material;
- no component tessellation metadata is attached to the probe cube;
- the model remains exactly 12 source triangles with face-local vertices;
- each face maps to its assigned atlas cell;
- the material remains textured on PS2;
- the existing camera, cube transform, light, FPS overlay, and scene id are unchanged.

The next PS2 build receives a new hard-coded overlay number and boots directly into the isolated render scene. HelenUI records text metrics only. Visual acceptance comes from Helena moving the camera into the cube and reporting which face colors, if any, move, stretch, flash, disappear, or explode.

## Out of Scope

This diagnostic does not change clipping math, VU microcode, packet batching, the fast textured path, lighting, camera controls, shared generated primitives, playable Tilt Trial content, or full DemoDisc scene selection. It does not attempt to fix the remaining rendering defect; it produces deterministic evidence for the next renderer correction.
