# PS2 VU Opaque Path Refactor Design

## Goal

Refactor the PS2 opaque VU rendering path so rigid opaque meshes stop paying heavy EE-side per-triangle setup costs. The new path should preserve the current stable visual baseline for `cube_test` and `colored_cube_grid` while moving opaque clip-style rejection and transform-to-screen work closer to the VU program model used by Tyra's dynamic PS2 pipeline.

## Current Problem

The current "VU" path still spends most of its frame time on CPU triangle work before the VU program runs. Timing evidence from the existing diagnostics shows:

- `3D` time is dominated by `Pkt`
- `Pkt` time is dominated by triangle setup, not packet assembly
- the expensive substeps are split between:
  - `Prep`: unpack plus world/view preparation
  - `Emit`: clipping, projection, culling, and per-triangle payload generation
- packet memory writes are negligible

This means the performance ceiling is architectural. The PS2 EE is still doing too much geometry work for the opaque VU path.

## Reference Direction

Tyra documents two clipping approaches:

- VU1-side "fake" PS2 clipping
- EE Core software clipping

Tyra's dynamic VU1 programs also perform clip checks in VU1 program code. That is the relevant model for this refactor. The intent is not to copy Tyra literally, but to align the opaque path with the same division of work: coarse CPU setup, VU-side clip-style rejection.

## Scope

This design only covers:

- opaque rigid meshes
- the current PS2 VU opaque rendering path
- the scenes used for current validation:
  - `cube_test`
  - `colored_cube_grid`

This design explicitly does not cover in the first pass:

- alpha rendering
- mixed transparency edge cases
- generalized software clipping parity
- large-scale scene system rewrites
- new material systems

## Architecture

### Overview

The opaque PS2 VU path will be changed from a CPU-screen-space triangle builder into a raw opaque batch uploader. The CPU will continue to choose renderable opaque batches and provide per-batch state, but it will stop performing per-triangle near-plane clipping, perspective projection, and screen-space front-face rejection for the new opaque path.

The VU opaque microprogram will become responsible for the remaining transform-to-screen and clip-style rejection needed for opaque triangles.

### CPU Responsibilities

The CPU side remains responsible for:

- scene traversal
- proxy synchronization
- frame planning and opaque batch selection
- material and model resolution
- per-batch constants and state selection
- uploading batch geometry and transforms to the VU path
- fallback selection when the new path is not valid

The CPU side must stop doing per-triangle work for the new opaque path in these categories:

- near-plane triangle clipping
- perspective projection to screen coordinates
- screen-space front-face rejection
- XYZ2 register generation per triangle

### VU Responsibilities

The VU opaque path will take over:

- vertex transformation using uploaded matrices/constants
- clip-style rejection appropriate to the opaque VU pipeline
- final position generation needed for GIF/GS submission

The VU path should remain opaque-only in the first pass.

## Component Boundaries

### `Ps2RenderManager3D`

`Ps2RenderManager3D` remains the orchestration layer. It should:

- rebuild proxies
- build the opaque frame plan
- choose the new opaque VU path
- keep alpha on the existing CPU path
- keep the old opaque CPU fallback available behind a switch until validation completes

It should not regain per-triangle clipping or projection logic.

### `Ps2VuVifPacketBuilder`

`Ps2VuVifPacketBuilder` is the main refactor target. It should stop constructing fully screen-resolved opaque triangles for the new path.

Instead, it should:

- package raw triangle data in a VU-friendly layout
- upload the transforms/constants required by the VU program
- avoid EE-side triangle clipping and projection for opaque draws

The packet builder should still own packet formation, but packet assembly is not the main performance problem. The primary goal is to remove CPU geometry work, not to micro-optimize `packet2`.

### VU Program Sources

The opaque VU microprograms under `src/platform/ps2/rendering/vu/programs` must be updated to match the new packet contract.

The VU program changes should include:

- consuming the new opaque payload layout
- performing the clip-style rejection needed for opaque draws
- emitting the final vertex positions used for GS submission

### `Ps2VuGifStateEncoder`

`Ps2VuGifStateEncoder` may need limited changes if the new payload/register order changes. It is not expected to be the main source of work.

## File Impact

Primary files:

- `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- `src/platform/ps2/rendering/Ps2RenderManager3D.hpp`
- `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`
- `src/platform/ps2/rendering/vu/Ps2VuGifStateEncoder.cpp`
- `src/platform/ps2/rendering/vu/programs/*`

Secondary files:

- `src/platform/ps2/Ps2BootHost.cpp`
  Only for temporary timing and validation overlays during rollout.

## Rollout Strategy

### Phase 1: Opaque-Only Parallel Path

Introduce the new opaque VU contract behind a dedicated path selection branch while preserving:

- the current stable opaque CPU fallback
- the current alpha CPU path

This keeps the refactor isolated and comparable.

### Phase 2: Validation Baseline

The first validation target is correctness, not peak FPS.

Required visual checks:

- `cube_test` still rotates correctly
- size remains correct
- stable presentation remains intact
- `colored_cube_grid` still renders correctly
- no regression to the previous flicker state

### Phase 3: Performance Comparison

Once correctness holds, compare the same scenes against the current timed baseline using the existing onscreen timing overlay.

Success means:

- `Prep` drops materially
- `Emit` drops materially
- `Pkt` drops materially
- `3D` improves enough to move the overall frame time down

### Phase 4: Cleanup

Only after the new opaque VU path is validated should temporary deep timing instrumentation be reduced or removed.

## Error Handling and Fallback

If the new opaque VU path encounters unsupported geometry cases, fallback must happen at the opaque path selection level, not by reintroducing CPU triangle clipping into the new path.

Acceptable first-pass behavior:

- opaque batch uses new VU path when supported
- otherwise, fallback to the existing opaque CPU path

Unacceptable first-pass behavior:

- mixing old per-triangle CPU clipping logic back into the new opaque path
- silently rebuilding screen-space triangles on EE for "just a few cases"

## Testing Strategy

### Functional Validation

Use:

- `cube_test`
- `colored_cube_grid`

Validate:

- visibility
- rotation
- size
- lighting/color baseline
- stable non-flickering presentation

### Performance Validation

Keep the existing onscreen timing overlay until the refactor is complete.

Important metrics:

- `3D`
- `Pre`
- packet-builder sub-buckets
- any new opaque-path-specific markers needed to disambiguate runs

### Regression Boundary

Alpha rendering remains on the old path, so opaque refactor failures should not be debugged through alpha behavior in the first pass.

## Risks

### VU Contract Risk

The largest risk is changing the VU input contract without matching the microprogram semantics precisely. This is the main reason to keep the old opaque CPU path available during rollout.

### Visual Regression Risk

Moving clip-style rejection into VU1 may initially expose:

- missing triangles
- winding/cull mismatches
- near-plane artifacts
- incorrect screen-space size

These are expected validation risks, not reasons to restore CPU clipping into the new path.

### Scope Creep Risk

Trying to solve alpha, generalized clipping correctness, and opaque performance in one pass will slow the refactor and blur the performance signal. Opaque-only must remain the hard boundary for the first pass.

## Success Criteria

The design is successful when all of the following are true:

- the new opaque VU path renders `cube_test` correctly
- the new opaque VU path renders `colored_cube_grid` correctly
- stable presentation remains intact
- EE-side triangle setup work drops materially
- `Pkt` is no longer dominated by CPU triangle setup
- the renderer no longer relies on CPU near-plane clipping, CPU projection, and CPU screen-space culling for opaque VU batches

## Implementation Notes

The current timing evidence strongly suggests that further micro-optimizing packet assembly is the wrong path. The first implementation pass must attack the EE-side per-triangle geometry contract directly.

That means the implementation plan should focus first on:

- defining the new opaque VU payload contract
- updating the opaque VU program to consume it
- removing CPU clipping/projection/cull from the new opaque path
- preserving the old CPU fallback until the new path is visually validated
