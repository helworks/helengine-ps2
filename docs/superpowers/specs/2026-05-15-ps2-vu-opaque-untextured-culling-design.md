# PS2 VU Opaque Untextured Culling Design

## Summary

This design adds a real VU-side culling path for opaque untextured geometry on PS2. The goal is to stop doing CPU-side per-triangle projection and culling for this path and instead use VU1 clip-flag culling in the standard PS2 style.

The first implementation is intentionally narrow:

- opaque only
- untextured only
- rigid meshes only
- no near-plane polygon clipping in the first pass

Textured, alpha, and mixed-material paths remain unchanged.

## Problem

The current opaque untextured VU path is not a true VU culling path.

- CPU-side setup still performs most of the expensive per-triangle work.
- The current `Ps2OpaqueDraw3D.vsm` program is still diagnostic in shape and does not perform the full transform and clip-flag workflow.
- Prior timing work showed the dominant frame cost is triangle prep and emit work on the EE, not packet assembly.

This means the current "VU path" still pays too much CPU geometry cost before VU1 sees the batch.

## Goals

- Move opaque untextured triangle transform and clip-style rejection onto VU1.
- Keep the current PS2 runtime architecture intact outside this path.
- Preserve the current working startup, lighting, and presentation baseline.
- Reuse host-debug for pre-emission validation and diagnostics.

## Non-Goals

- Do not change textured rendering in this pass.
- Do not change alpha rendering in this pass.
- Do not implement full polygon clipping against the near plane in this pass.
- Do not redesign the whole renderer or packet system.

## Reference Model

Tyra's dynamic VU path is the closest useful reference in the local PS2 references. In particular:

- `dynpip_d_vu1.vclpp` performs matrix multiply on VU1
- `PerformClipCheck` uses `clipw.xyz`
- clip flags are converted into ADC state for the outgoing GIF payload
- final screen conversion happens on VU1 before `xgkick`

This design follows that model at a smaller scope for opaque untextured batches.

## Target Behavior

For opaque untextured VU batches:

1. The CPU decodes packed model geometry and builds raw triangle payloads.
2. The CPU does not project triangles into screen space for this path.
3. The CPU does not do per-triangle front-face rejection for this path.
4. VU1 consumes raw vertex positions plus the required transform constants.
5. VU1 performs transform, clip-flag evaluation, GS conversion, and final kick.
6. Triangles outside the frustum are rejected using the PS2 clip-flag path.

Unsupported geometry should not partially use this path. Unsupported cases should be rejected at path selection and continue through the existing fallback path.

## Ownership

### `Ps2RenderManager3D`

`Ps2RenderManager3D` remains responsible for:

- scene traversal
- proxy sync
- frame planning
- batch selection
- choosing whether a batch is eligible for the new opaque untextured VU culling path

It should not own the culling implementation itself.

### `Ps2VuOpaqueUntexturedSetupBuilder`

`Ps2VuOpaqueUntexturedSetupBuilder` should become the shared opaque-untextured geometry setup layer.

It should:

- decode packed model triangle data
- prepare raw triangle payloads for VU consumption
- provide reusable diagnostics for host-debug and PS2

It should not:

- emit final screen-space XYZ2 values
- perform CPU-side front-face rejection for the new path

### `Ps2VuVifPacketBuilder`

`Ps2VuVifPacketBuilder` should:

- consume the shared raw opaque-untextured setup data
- emit the VIF/GIF packet layout expected by the new VU program
- keep PS2-only packet emission concerns isolated from shared setup logic

### `Ps2OpaqueDraw3D.vsm`

`Ps2OpaqueDraw3D.vsm` becomes the real opaque-untextured VU culling microprogram.

It must:

- load the needed transform constants
- transform vertices on VU1
- perform clip checks with VU clip flags
- write ADC-compatible results into the outgoing packet payload
- convert surviving vertices to GS format
- kick the packet

## Payload Contract

The new path should preserve the existing design principle that CPU and VU agree on a stable payload shape.

The payload must include:

- source triangle positions
- lighting inputs already needed by the path
- the transform constants required by VU1
- GS scale and offset data
- the GIF packet template region that receives final VU output

The payload should be designed for opaque untextured batches only in this first pass. It should not attempt to anticipate every future textured or alpha variant.

## Rollout Strategy

The first rollout should be gated to the smallest useful surface:

- `cube_test`
- opaque untextured content in `colored_cube_grid`

The current fallback path must stay available until the new path is stable.

Success for the first rollout means:

- correct visible rendering
- no startup regressions
- stable non-flickering presentation
- lower CPU-side geometry work than the existing opaque untextured path

## Host-Debug Role

Host-debug remains a validation tool for everything up to the PS2-only emission boundary.

Host-debug should:

- reuse the same opaque-untextured setup builder
- report triangle counts and batch eligibility
- continue exposing prep-style diagnostics before hardware submission

Host-debug does not need to emulate VU execution in this phase. Its role is to confirm payload preparation and path selection before the PS2 executable is used for final VU validation.

## Risks

### Near-Plane Cases

Because the first pass does not implement full polygon clipping, triangles intersecting the near plane may still need fallback behavior. This is acceptable in the first version as long as it fails over cleanly.

### Payload Drift

The setup builder, packet builder, and VU program must stay in lockstep. Any silent layout drift will produce hard-to-debug rendering failures.

### Mixed Path Complexity

If unsupported batches are allowed to partially enter the new path, the behavior will become difficult to reason about. Eligibility must be explicit and narrow.

## Verification

The first verification pass should use:

- `cube_test`
- `colored_cube_grid`
- host-debug `draw-once`
- live PS2 runtime runs in PCSX2

The verification criteria are:

- the cube still renders correctly
- lighting remains correct for opaque untextured content
- visible geometry is stable
- CPU-side prep work drops compared to the current path
- no startup material/runtime regressions are introduced

## Decision

Implement a Tyra-style VU clip-flag culling path for opaque untextured rigid meshes only, with CPU fallback retained for unsupported cases during rollout.
