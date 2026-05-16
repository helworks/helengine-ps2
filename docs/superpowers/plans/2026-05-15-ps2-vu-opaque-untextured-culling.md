# PS2 VU Opaque Untextured Culling Implementation Plan

## Objective

Implement a Tyra-style VU clip-flag culling path for opaque untextured rigid meshes on PS2, while keeping the current fallback path available during rollout.

## Constraints

- Scope is limited to opaque untextured batches.
- Textured and alpha paths are unchanged.
- The first pass does not implement full near-plane polygon clipping.
- Host-debug remains a validation aid, not a VU emulator.

## Task 1: Lock The Current Baseline

### Goal

Freeze the current working behavior before changing the opaque untextured VU path.

### Changes

- Verify the current `cube_test` and `colored_cube_grid` export still load and render.
- Verify the current host-debug `load-only` and `draw-once` modes still pass.
- Keep the current fallback path reachable by an explicit switch during rollout.

### Verification

- `cube_test` renders in PCSX2.
- `colored_cube_grid` still boots and renders.
- `ps2-host-debugger --mode load-only` exits `0`.
- `ps2-host-debugger --mode draw-once` exits `0`.

## Task 2: Define The New Opaque Untextured Payload Contract

### Goal

Replace the diagnostic screen-space payload assumption with a stable raw-geometry payload for the opaque untextured VU path.

### Changes

- Audit the existing opaque untextured payload layout used by `Ps2VuOpaqueUntexturedSetupBuilder`, `Ps2VuVifPacketBuilder`, and `Ps2OpaqueDraw3D.vsm`.
- Define a single payload contract that includes:
  - source triangle positions
  - normals or lighting inputs already required by the path
  - transform constants
  - GS scale and offset
  - outgoing GIF template region
- Document the payload offsets in the relevant shared header or builder source comments.

### Verification

- One source of truth exists for the payload layout.
- Host-debug still reports the same batch/triangle counts after the contract change.

## Task 3: Move Shared Geometry Setup Fully Into The Setup Builder

### Goal

Ensure the shared setup builder produces raw VU-ready triangle payload data without CPU-side projection or cull decisions for the new path.

### Changes

- Refine `Ps2VuOpaqueUntexturedSetupBuilder` so it:
  - decodes packed model triangles
  - emits raw source positions
  - emits lighting inputs needed later by VU
  - tracks diagnostics for prepared and emitted triangles
- Remove any remaining CPU-side screen-space output assumptions from the new-path flow.
- Keep unsupported-case detection explicit and narrow.

### Verification

- Host-debug `draw-once` still completes.
- Host-debug reports stable `submittedTriangles` and batch counts for `cube_test`.

## Task 4: Refactor Packet Emission Around The New Payload

### Goal

Teach `Ps2VuVifPacketBuilder` to emit the new raw opaque-untextured payload without reintroducing CPU-side projection/cull work.

### Changes

- Update the untextured opaque branch in `Ps2VuVifPacketBuilder`.
- Ensure VIF/GIF template patching matches the new payload layout.
- Preserve the fallback path for unsupported batches.
- Keep the PS2-only emission logic isolated from shared geometry setup.

### Verification

- PS2 native build succeeds.
- Host-debug still builds successfully.
- Existing packet diagnostics stay coherent.

## Task 5: Replace The Diagnostic VU Program With Real Transform And Clip-Flag Culling

### Goal

Turn `Ps2OpaqueDraw3D.vsm` into a real opaque untextured VU culling microprogram.

### Changes

- Load the required transform constants on VU1.
- Transform vertices on VU1.
- Use VU clip flags in the Tyra-style pattern:
  - reset clip flags
  - run `clipw.xyz`
  - convert clip state into ADC-compatible output
- Perform GS conversion on VU1.
- Preserve the current lighting behavior for opaque untextured output.
- Keep the microprogram scoped to the opaque untextured contract only.

### Verification

- A diagnostic single-triangle path renders correctly through the new VU program.
- `cube_test` renders correctly through the new VU path.

## Task 6: Add Narrow Eligibility And Fallback Rules

### Goal

Avoid mixed-path ambiguity while the new path is rolling out.

### Changes

- Add explicit eligibility checks for:
  - opaque
  - untextured
  - rigid
  - supported packed model format
- Route unsupported content to the existing fallback path.
- Add clear diagnostics for why batches were rejected from the new path.

### Verification

- Unsupported content still renders through fallback.
- New-path rejection counts are visible in host-debug or PS2 diagnostics.

## Task 7: Validate On Host Before Live PS2 Runs

### Goal

Use host-debug to catch payload and setup regressions before PCSX2.

### Changes

- Keep `draw-once` reporting:
  - batch counts
  - triangle counts
  - prep/setup counters
  - rejection counts
- Add any new opaque-untextured counters required by the new path.

### Verification

- `ps2-host-debugger --mode draw-once` exits `0`.
- Reported triangle and rejection counts remain sane for `cube_test`.

## Task 8: Validate In PCSX2

### Goal

Confirm the new VU culling path works on the actual PS2 executable path.

### Changes

- Export fresh PS2 builds from the real editor path.
- Validate `cube_test` first.
- Validate the opaque untextured subset of `colored_cube_grid` next.

### Verification

- `cube_test` renders with correct rotation and lighting.
- `colored_cube_grid` opaque untextured cubes still render correctly.
- No startup/runtime contract regression appears.

## Task 9: Re-Measure The CPU Cost Boundary

### Goal

Prove the refactor moved real work off the CPU-side setup path.

### Changes

- Re-enable or retain the timing diagnostics needed for comparison.
- Compare the new opaque untextured path against the previous baseline.
- Focus on the old hot buckets:
  - prep/setup work
  - emit work
  - overall 3D time

### Verification

- CPU-side prep/setup work drops materially for the opaque untextured path.
- Overall 3D cost improves or at minimum shifts away from the old CPU bottleneck.

## Task 10: Remove Temporary Diagnostics And Stabilize The Path

### Goal

Leave the new path in a maintainable state after validation.

### Changes

- Remove one-off diagnostic toggles that are no longer needed.
- Keep only durable counters or markers that remain useful for future debugging.
- Make sure the fallback path is still available but not accidentally selected for the validated opaque untextured case.

### Verification

- Final PCSX2 run remains stable.
- Host-debug still builds and runs.
- Builder and export still succeed from the mainline workflow.
