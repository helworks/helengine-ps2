## PS2 Cube Test VU Transform Pass Design

### Goal

Implement the next isolated VU renderer pass for `cube_test` only by moving final untextured opaque triangle `XYZ2` generation from CPU to VU1 while preserving the currently working packet, color, and dispatch baseline.

### Scope

This pass changes one thing only:

- VU1 computes the final three `XYZ2` outputs for the active untextured opaque `cube_test` packet path

Everything else remains on the current working baseline:

- packet register order stays unchanged
- CPU flat color stays unchanged
- CPU visibility and culling stay unchanged
- CPU path selection stays unchanged
- `xgkick` submission ownership stays unchanged
- no scene-path expansion beyond `cube_test`

### Non-Goals

This pass does not include:

- VU clip rejection
- VU front-face culling
- VU lighting
- textured opaque batches
- alpha batches
- packet header redesign
- renderer-family policy changes

### Current Stable Baseline

The recoverable baseline before this pass has these properties:

- real editor-driven PS2 export succeeds
- `cube_test` geometry reaches the PS2 runtime
- the current untextured VU packet path boots and dispatches over VIF1 successfully
- the helper-generated untextured GIF packet shape is valid
- final triangle `XYZ2` values are produced on CPU
- flat triangle color is produced on CPU
- the cube renders visibly in PCSX2 with the current spinning baseline

This baseline must remain easy to restore if transform-only bring-up fails.

### Problem Statement

The renderer needs a narrower next step than the earlier mixed VU experiments. Previous attempts combined transform, culling, lighting, and packet-shape changes in one move, which made failures ambiguous.

For `cube_test`, the next pass should isolate only the transform handoff:

- CPU stops owning final `XYZ2` generation for the active untextured VU path
- VU1 takes ownership of those three position outputs
- all non-position packet behavior stays fixed

That keeps failures attributable to either:

- incorrect transform math
- incorrect packet-slot writes

and avoids mixing them with visibility or shading regressions.

### Target Architecture

#### CPU Responsibilities

CPU remains responsible for:

- scene traversal
- choosing the active `cube_test` untextured opaque batch path
- preserving the current untextured packet template and non-position register contents
- resolving flat color
- preparing one per-triangle payload containing raw triangle positions plus the transform constants VU1 needs

CPU must stop treating final `XYZ2` values as authoritative for the active transform-only untextured VU path.

#### VU1 Responsibilities

`Ps2OpaqueDraw3D.vsm` becomes responsible for:

- loading the raw triangle positions
- loading the transform constants supplied by CPU
- transforming each vertex into the final GS-facing screen-space position form
- converting those results into final `XYZ2` register values
- writing only the three `XYZ2` packet slots already used by the working baseline
- issuing `xgkick`

VU1 must not add lighting, clip rejection, front-face rejection, or packet-layout changes in this pass.

### Data Contract

The untextured packet template remains the source of truth for packet shape.

The transform-only payload contract is:

- vertices A, B, and C are uploaded as raw positions
- transform constants required to derive final `XYZ2` are uploaded alongside the triangle data
- the packet already contains valid non-position register data before VU execution
- the VU program overwrites only the three `XYZ2` slots before `xgkick`

The packet slot locations must remain identical to the current working CPU-projected baseline so visual regressions can be attributed to transform logic rather than packet-layout churn.

### File-Level Design

Implementation stays inside the existing VU path seams:

- `src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.hpp`
- `src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.cpp`
- `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- `src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm`
- `src/platform/ps2/rendering/Ps2RenderManager3D.hpp`
- `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`

Expected responsibilities by file:

- `Ps2VuOpaqueUntexturedSetupBuilder`
  - define the transform-only payload layout for one untextured triangle
  - stop packaging CPU-authored final `XYZ2` as the authoritative position payload for this path
  - keep explicit slot addressing for the three `XYZ2` packet targets

- `Ps2VuVifPacketBuilder`
  - preserve the existing untextured packet template flow
  - upload transform-only payload data instead of final CPU-generated `XYZ2`
  - leave non-position GIF contents unchanged
  - keep the active untextured `mscal` dispatch pattern unchanged

- `Ps2OpaqueDraw3D.vsm`
  - read raw positions and transform constants
  - compute final screen-space `XYZ2`
  - write those values into the established packet slots
  - preserve the current `xgkick` tail behavior

- `Ps2RenderManager3D`
  - keep renderer behavior unchanged beyond passing through the same world, view-projection, and viewport inputs already used by the VU path
  - do not redesign fallback routing in this pass

### Safety Rules

- keep the current visible `cube_test` baseline recoverable
- do not change packet register order during this pass
- do not bundle culling or lighting work into this change
- do not redesign the untextured packet header in this step
- if bring-up fails, recover by reverting only to the last CPU-projected `XYZ2` baseline

### Verification

Verification remains intentionally narrow and `cube_test`-only.

Required checks:

- focused source and builder tests cover the transform-only payload contract and VU microprogram expectations
- PS2 rebuild succeeds from the current editor/export pipeline
- PCSX2 boots the fresh ISO
- `cube_test` still renders a spinning cube
- the cube remains visibly stable in size and position relative to the current baseline
- flat color still appears as before
- the FPS overlay remains visible so "missing cube with intact UI" remains a useful failure signal

Failure indicators for this pass are:

- black 3D output with UI still visible
- giant triangle or stretched quad corruption
- cube rendered far off-screen or at clearly wrong scale
- VIF or packet-dispatch regression in the current untextured VU path

### Success Criteria

This pass is complete when:

- VU1 owns final `XYZ2` generation for the active `cube_test` untextured opaque path
- CPU no longer acts as the final `XYZ2` author for that path
- no culling, lighting, or packet-shape work was introduced
- the rebuilt PS2 export still boots and visibly renders the expected spinning cube in PCSX2

### Next Step After Success

If this pass succeeds, the next isolated renderer step is VU-side clip and cull behavior for the same `cube_test` path while keeping:

- the same packet template
- the transform-only VU path
- the CPU flat-color baseline

Lighting remains a later step.
