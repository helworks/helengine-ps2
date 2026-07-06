# PS2 Textured VU Performance Design

## Goal

Raise PS2 textured opaque rendering performance toward 60 FPS in the `textured_cube_grid` scene without changing the `city` project scene content or authored materials.

## Scope

- Engine-only changes in `helengine-ps2`
- Keep the PS2 startup scene pinned to `textured_cube_grid` during this optimization pass
- Generalize the optimization for textured opaque PS2 meshes, while using `textured_cube_grid` as the verification scene

## Current Problem

The current textured opaque path still performs expensive CPU-side per-triangle work:

- near-plane clipping
- projection
- GS `XYZ2` packing
- lighting evaluation
- GIF packet construction

The current batch builder also emits one VU batch per proxy. In the textured cube scene this means repeated VIF submission and repeated CPU setup across 16 cubes.

## Constraints

- Work in small steps
- Rebuild and relaunch the emulator between steps so visual regressions can be checked live
- Avoid scene-side or project-side optimizations
- Preserve current visual quality unless a temporary diagnostic mode is explicitly enabled

## Recommended Strategy

Use a phased renderer change with a clear end state.

### End State

The textured opaque path should mirror the intent of the untextured VU path:

- CPU builds compact source data and shared state only
- VU1 performs transform, projection, lighting, culling, and packet patching
- CPU submits fewer, larger batches

### Phase 1

Remove CPU-side textured per-triangle work that does not need to live on the EE.

Target:

- move textured lighting off the CPU first
- preserve existing visible behavior
- keep diagnostics active

### Phase 2

Move textured transform and projection responsibilities onto VU1.

Target:

- CPU no longer computes per-triangle screen-space positions for textured opaque meshes
- VU1 computes GS-ready positions

### Phase 3

Reduce submission overhead by grouping compatible textured proxies into fewer batches.

Target:

- batch compatible textured opaque proxies by shared render state and texture identity
- reduce per-frame VIF submission count

## First Small Step

The first implementation step should be narrow and reversible:

- keep the current textured packet structure
- keep `textured_cube_grid` as the startup scene
- replace CPU textured lighting computation with a cheaper shared-material path or VU-owned lighting input path
- rebuild and relaunch PCSX2 for visual verification before continuing

This step is intentionally limited so that regressions can be isolated quickly.

## Diagnostics To Keep

- textured batch count
- submitted triangle count
- VIF bytes per submission
- CPU packet build time
- submit time
- startup scene override confirmation

## Success Criteria

- textured opaque PS2 meshes use a materially cheaper CPU path
- `textured_cube_grid` remains visually correct during each step
- the optimization is generalized for PS2 textured opaque meshes, not hardcoded to one scene
- the workflow remains incremental with emulator feedback after every step
