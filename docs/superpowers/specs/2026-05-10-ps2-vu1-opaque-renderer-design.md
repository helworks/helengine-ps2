# PS2 VU1 Opaque Renderer Design

## Summary

This document defines the first real PlayStation 2 renderer milestone for HelEngine: a `VU1 + VIF` opaque renderer path that replaces the current CPU triangle submission architecture for the performance-critical 3D path.

The first milestone is intentionally narrow:

- opaque untextured
- opaque textured
- no alpha test
- no alpha blend
- no HDR glow

The current CPU renderer remains available as a fallback and debugging reference while the new path is brought up.

## Problem

The current PS2 3D renderer is functionally useful for bring-up, but it is architecturally wrong for real performance. Current measurements show:

- single cube scene: about `60 FPS`
- `4x4` textured cube grid: about `9.7 FPS`
- `4x4` untextured cube grid: about `12 FPS`

The timing split makes the bottleneck clear:

- update is cheap
- present is cheap enough
- draw is dominant

The dominant draw cost comes from the current CPU path:

- per-frame proxy rebuild
- per-triangle CPU transform
- per-triangle CPU clipping
- per-triangle CPU culling
- per-triangle CPU lighting
- one `gsKit` triangle call per triangle

This is not a PS2 renderer architecture that can scale to real scenes.

## Goals

- Replace the CPU per-triangle submission path for opaque geometry with a real `VU1/VIF` renderer path.
- Use a PS2-native cooked mesh layout that is already ready for `VIF` and `VU1`.
- Avoid runtime expansion of `vector3` data into `vector4` on PS2.
- Keep the current CPU renderer available as a fallback while the new path is being validated.
- Make the first VU milestone small enough to debug without dragging alpha, glow, and full feature parity into bring-up.

## Non-Goals

- No alpha test in the first VU milestone.
- No alpha blending in the first VU milestone.
- No HDR glow or expensive post paths in the first VU milestone.
- No attempt to optimize disc footprint before the VU-ready layout works.
- No attempt to build the final multi-family PS2 renderer in one step.

## Decision Summary

### Renderer scope

The first VU milestone targets:

- `VU1 + VIF`
- opaque untextured
- opaque textured

### Vertex layout

The first PS2-native cooked geometry format uses qword-aligned `vector4`-friendly layout from day one.

Shared engine data may remain `vector3` or generic, but PS2 cooked data must be emitted in the format the hardware wants. The PS2 runtime must not convert arrays of `vector3` into `vector4` before upload.

### Migration strategy

The new VU renderer path is added beside the current CPU renderer path. `Ps2RenderManager3D` remains the public renderer entry point, but internally it can route between:

- legacy CPU path
- new `VU1` opaque path

This lowers bring-up risk and preserves a correctness reference.

## Why Vector4

For the PS2 cooked format, `vector4` is the correct choice.

Reasons:

- `VU1` and `VIF` operate naturally on qwords.
- `16-byte` alignment simplifies upload layout and microprogram inputs.
- `vector3` would either require:
  - awkward stride and unpack logic in the upload path, or
  - runtime expansion to `vector4`
- runtime expansion is explicitly not acceptable for this design.

The cost is larger PS2 cooked mesh size on disc. That is acceptable for the first working VU renderer. If size becomes a problem later, the next step is a compressed PS2-native packed format, not a return to plain `vector3`.

## Current Architecture Boundary

The shared runtime still owns gameplay:

- entity hierarchy
- transforms
- component state
- scene loading
- input
- update logic

The PS2 renderer owns rendering:

- cooked PS2 mesh format
- packet layout
- batch assembly
- VIF upload
- VU1 execution
- GS state programming

That boundary remains unchanged. This design only replaces how PS2 executes 3D rendering.

## Target Runtime Architecture

The VU renderer path should be split into five PS2-owned pieces.

### 1. PS2 packed geometry representation

Add a dedicated PS2 runtime model representation for the VU path. It should expose:

- sectioned mesh data
- packed vertex streams
- packed index streams or topology blocks
- material section boundaries
- static metadata needed by the packet builder

The current `Ps2RuntimeModel` can remain as the compatibility container for the legacy CPU path, but the new path should not rely on raw `std::vector<float3>` position iteration.

### 2. PS2 batch planning layer

The frame planner still decides what is visible and which proxies are opaque. The new VU path adds a second-level batch builder that groups compatible opaque draws by:

- textured or untextured
- material state compatibility
- mesh section
- static versus dynamic category where needed

The first milestone does not need aggressive batch merging. It does need an explicit batch abstraction so the renderer is not dispatching one microprogram invocation in an ad hoc per-triangle loop.

### 3. VIF packet builder

Introduce a PS2-owned VIF packet builder responsible for:

- unpack commands
- qword-aligned payload emission
- matrix and lighting constant uploads
- vertex block uploads
- index or primitive block uploads
- microprogram launch commands

This builder owns packet correctness. The renderer should not assemble raw VIF command structure inline inside the scene traversal logic.

### 4. VU1 microprogram registry

Introduce an internal registry for microprogram selection. The first milestone needs only two program families:

- opaque untextured
- opaque textured

The registry should make later expansion straightforward for:

- alpha test
- alpha blend
- shadow variants
- stylized variants

The registry is responsible for mapping a draw batch to:

- the loaded microprogram
- expected input layout
- output mode assumptions

### 5. GIF and GS state encoder

The VU path still needs GS state management. That logic should be explicit and separate from mesh packet upload. It should own:

- texture binding state
- opaque depth state
- clamp/filter policy
- primitive state for textured and untextured opaque draws

This layer should not know about scene traversal. It only knows how to encode the required state for one draw batch.

## First-Milestone Mesh Layout

The first milestone mesh layout should be simple, explicit, and qword aligned.

Per-vertex data should use:

- position: `float4`
- normal: `float4`
- UV block: qword-aligned attribute block

For the first milestone, the UV block can be stored in a full qword even though only two components are semantically required. The wasted space is acceptable in exchange for:

- simpler `VIF` unpack
- simpler microprogram addressing
- no runtime repacking

The first-milestone packed vertex format should prioritize:

- correctness
- alignment
- simple packet generation

It should not attempt early compression tricks.

## Builder Changes

The builder must emit a PS2-native cooked mesh payload specifically for the VU path.

### Required builder responsibilities

- convert generic model geometry into qword-aligned PS2 packed streams
- convert positions into `float4`
- convert normals into `float4`
- emit UV attribute blocks in qword-friendly layout
- preserve section/material boundaries
- emit metadata required by the runtime batch and packet builders

### Explicit rule

The builder must do the shape conversion. The PS2 runtime must not convert generic arrays into the VU layout at load time.

## Runtime Model Changes

`Ps2RuntimeModel` should evolve into one of these shapes:

1. dual representation
   - legacy CPU-friendly vectors for fallback
   - packed VU streams for fast path
2. explicit split types
   - one legacy model type
   - one VU packed model type

For the first milestone, the cleaner design is an explicit split type or a clearly separated packed sub-structure, because the current raw vectors represent a different rendering architecture entirely.

The runtime fast path should not iterate CPU triangles from `std::vector<float3>` just because that was convenient for bring-up.

## Render Flow

Per frame, the VU opaque path should look like this:

1. resolve active camera and matrices
2. rebuild or synchronize render proxies
3. build the opaque frame plan
4. build compatible draw batches
5. for each batch:
   - emit GS state
   - upload constants
   - upload vertex/index payload or reference resident packed payload
   - dispatch the correct VU1 program
   - execute GIF chain

The fast path should avoid any CPU per-triangle clipping or per-triangle gsKit draw calls.

## Handling Transforms

Dynamic object transforms remain sourced from the gameplay world. The first milestone should treat transforms as per-draw constants uploaded through the VIF/VU path.

That means:

- cooked vertex streams remain in object-local space
- each draw uploads the current object transform
- VU1 performs transform work

Static and dynamic objects can still share the same basic packed layout. Later milestones may specialize static geometry further.

## Feature Scope for the First Milestone

### Supported

- opaque untextured materials
- opaque textured materials
- per-object transform
- camera transform
- directional-light shading at the level already needed for the current opaque path

### Deferred

- alpha test
- alpha blend
- additive
- glow
- software fallback parity for every branch
- advanced shadow execution

This is deliberate. The milestone must validate the real architecture first.

## Compatibility and Cutover

The existing CPU renderer path remains in the codebase temporarily.

The cutover strategy should be:

- add the new VU path under an explicit internal mode or branch
- prove correctness on:
  - cube test
  - colored cube grid
  - textured cube grid
- only then decide whether to delete or demote the CPU path

This prevents regressions from leaving the PS2 target without any working renderer during bring-up.

## Testing Strategy

The first milestone needs three kinds of verification.

### 1. Builder tests

Verify that PS2 cooked mesh payloads:

- use the VU-ready packed format
- contain qword-aligned attribute blocks
- preserve section boundaries
- do not require runtime `vector3 -> vector4` conversion

### 2. Source-level native tests

Verify that:

- new VIF packet builder exists and is wired
- microprogram registry exists and is wired
- `Ps2RenderManager3D` routes opaque work through the VU path
- legacy per-triangle `gsKit_prim_triangle_*` loops are not used by the new opaque path

### 3. Runtime scene tests

Use the committed city scenes:

- `Cube Test`
- `Colored Cube Grid`
- `Textured Cube Grid`

Success means these scenes render through the VU opaque path with visible correctness and improved frame time.

## Success Criteria

The milestone is successful when:

- PS2 cooked meshes are VU-ready and qword-aligned
- opaque untextured rendering uses the VU path
- opaque textured rendering uses the VU path
- the three diagnostic city scenes render correctly
- measured draw time is materially lower than the current CPU path

## Rejected Alternatives

### Keep vector3 and expand in memory

Rejected because it adds runtime conversion cost and defeats the main point of a PS2-native cooked format.

### VU0-only transform assist

Rejected because it still leaves the submission architecture centered on CPU triangle emission and does not solve the actual bottleneck.

### Full feature parity from day one

Rejected because it would bury the bring-up under alpha, glow, and material complexity before the core packet path is proven.

## Open Follow-Up Work

This design intentionally leaves later work for later milestones:

- alpha test VU path
- alpha blend VU path
- glow and post-path integration
- packed/compressed PS2 geometry formats
- static-world specialization
- texture residency optimization
- deeper batching and packet reuse

Those should come after the first opaque VU path is working and measured.
