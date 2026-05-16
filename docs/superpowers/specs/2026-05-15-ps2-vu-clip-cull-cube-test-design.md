# PS2 VU Clip/Cull Cube Test Design

## Goal

Move clip/cull responsibility for the `cube_test` opaque-untextured VU path from CPU-side visibility decisions into the VU microprogram, while preserving the currently working transform-only VU baseline, packet/header contract, and CPU fallback code for debugging.

## Current Context

The current `cube_test` baseline is now stronger than when the first draft of this slice was outlined:

- real editor-driven PS2 export succeeds
- the packaged ISO boots in PCSX2
- the opaque-untextured VU path already owns final `XYZ2` generation
- the current packet/header layout and `xgkick` flow are valid
- repeated cold-start relaunches of `cube_test` stayed stable in PCSX2

That means the next isolated renderer step can now be narrower and more honest:

- keep transform ownership on VU1
- move only clip/cull responsibility to VU1
- keep all later VU work on `cube_test` until the full `cube_test` VU path is complete

## Scope

This design is intentionally narrow.

Included:

- `cube_test` only
- opaque-untextured path only
- current transform-only VU packet/header format
- VU-side transform plus VU-side clip/cull

Explicitly excluded:

- `colored_cube_grid`
- textured path changes
- alpha path changes
- VU lighting changes
- packet-layout redesign
- broad batch-system changes
- scene-path expansion before `cube_test` VU work is complete

## Problem Statement

`cube_test` is the VU proving ground. The transform-only pass removed CPU ownership of final `XYZ2`, but triangle visibility is still effectively authored on CPU for this path.

That leaves the `cube_test` VU path incomplete in one important way:

- VU1 owns transform output
- CPU still owns visibility decisions

The next step should move that single remaining triangle-decision boundary to VU1 without mixing in lighting or packet redesign. That keeps failure attribution tight:

- transform regressions stay separate from clip/cull regressions
- lighting stays a later problem
- scene expansion waits until `cube_test` proves the full VU path

## Design

### CPU Responsibilities

The CPU remains responsible for:

- scene traversal and proxy synchronization in `Ps2RenderManager3D`
- frame planning and batch selection
- packed model decode
- preparing per-triangle payloads
- assembling the VIF upload and VU-owned GIF packet header
- preserving the current CPU fallback implementation in source for debugging

The CPU must stop acting as the normal-path triangle visibility authority for the active `cube_test` opaque-untextured VU path.

### VU Responsibilities

The VU microprogram in `Ps2OpaqueDraw3D.vsm` becomes responsible for:

- transforming the three source vertices
- generating clip flags with `clipw.xyz`
- evaluating triangle rejection state from those flags
- encoding the resulting accept/reject state into the existing packet contract
- preserving the already working `xgkick` completion path

This pass should not add lighting, packet-layout changes, or a broader dispatch redesign.

### Fallback Strategy

The CPU fallback path remains useful and should stay in source.

That fallback is for:

- debugging unrelated renderer failures
- restoring a known-good CPU visibility path when investigating regressions
- comparing behavior during bring-up

But the normal `cube_test` execution path for this slice must not silently route back through CPU clip/cull. If VU clip/cull is enabled for the path, the path should actually exercise it.

## File Boundaries

### `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`

Keeps:

- scene traversal
- proxy sync
- frame planning
- VU path selection
- explicit CPU fallback code in source

Does not gain new normal-path triangle rejection logic.

### `src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.cpp`

Keeps preparing:

- source triangle positions
- normals
- face normal
- matrices
- GS scale/offset constants

Its job remains payload preparation, not authoritative visibility rejection for the normal VU path.

### `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`

Keeps:

- VIF packet assembly
- VU-owned GIF packet header creation
- per-triangle payload upload

For this pass, it should:

- preserve the transform-only payload contract unless clip/cull needs one explicit additional field
- avoid reintroducing CPU visibility filtering into the normal `cube_test` VU path
- keep packet slot ownership and dispatch shape stable

### `src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm`

This is the main implementation target. It should:

- keep the current transform-only packet layout
- add `clipw.xyz` handling
- derive reject/accept state from clip flags
- write the required ADC or equivalent visibility state into the existing vertex slots
- preserve the working packet write contract and `xgkick` tail

## Safety Rules

- keep the current visible `cube_test` baseline recoverable
- keep all next VU work on `cube_test` before moving to another scene
- do not change packet register order during this pass
- do not bundle lighting into this change
- do not redesign the untextured packet header in this step
- keep CPU fallback code available for debugging
- if bring-up fails, recover by reverting only this clip/cull responsibility back to the transform-only baseline

## Verification

Verification remains intentionally narrow and `cube_test`-only.

Required checks:

- focused source and builder tests cover VU clip/cull expectations and guard against reintroducing CPU normal-path visibility ownership
- PS2 rebuild succeeds from the current editor/export pipeline
- PCSX2 boots the fresh ISO
- `cube_test` still renders a spinning cube
- front/back visibility behaves correctly during rotation
- the FPS overlay remains visible so "missing cube with intact UI" remains a useful failure signal
- the CPU fallback remains available in source for debugging, even if it is not the active normal path

Failure indicators for this pass are:

- black 3D output with UI still visible
- cube faces disappearing incorrectly while rotating
- whole-cube disappearance from bad clip handling
- VIF or packet-dispatch regressions in the current untextured VU path
- hidden silent fallback that makes it unclear whether VU clip/cull actually ran

## Success Criteria

This pass is successful when all of the following are true for `cube_test`:

- VU1 owns triangle clip/cull decisions for the active opaque-untextured VU path
- the current transform-only VU packet path still renders visibly
- CPU fallback code remains available for debugging
- no lighting or packet-shape work was bundled into the slice
- the rebuilt PS2 export still boots and visibly renders the expected spinning cube in PCSX2

## Next Step After Success

After this clip/cull slice succeeds, the remaining VU implementations should still stay on `cube_test` before scene expansion.

That means the next work should continue to follow this rule:

- finish the `cube_test` VU path first
- only then move to the next scene

Lighting remains a later step.
