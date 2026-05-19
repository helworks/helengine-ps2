# PS2 Cube Test Visible VU Baseline Recovery Design

## Summary

Restore the last known visible rotating `cube_test` baseline where the PS2 VU opaque path was active and rendering on screen, without rolling back the later generic PS2 texture capability and builder-owned cook work. This is a targeted renderer/runtime recovery slice, not a new optimization pass.

## Goals

- Recover the last visible rotating `cube_test` PS2 VU baseline.
- Restore the VU-active runtime path rather than falling back to a CPU-only render path.
- Keep later builder/platform-cook work intact, especially the generic PS2 texture capability work.
- Re-lock the recovered baseline with focused PS2 source-contract tests before any new VU optimization work resumes.

## Non-Goals

- No new VU optimization work in this slice.
- No new VU feature work beyond what is required to restore the last visible baseline.
- No rollback of generic PS2 texture capability metadata or platform-owned texture cook work.
- No broad repo reset to an older commit.

## Current Problem

The current PS2 tree still carries a compact untextured VU path, but the active `cube_test` path has drifted into a diagnostic/broken visual state. Earlier work established a visible rotating cube with active VU counters, but later compact-path changes moved the source away from that stable baseline.

The user wants the last visible rotating VU `cube_test` state back first, before continuing new VU passes.

## Recommended Recovery Strategy

Use a targeted rollback of the VU renderer/runtime slice only.

That means restoring the last known-good visible-cube behavior in the small set of files that define the PS2 opaque VU path:

- `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- `src/platform/ps2/rendering/Ps2RenderManager3D.hpp` if needed
- `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp` if needed
- `src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm`
- matching focused PS2 source-contract tests under `builder.tests`

This is preferable to reconstructing the baseline by trial and error because:

- it has a smaller blast radius
- it is faster to verify
- it preserves the unrelated newer builder/platform work
- it gets the project back to a known-good visual checkpoint before further optimization resumes

## Recovery Target

The target state is the previously visible rotating `cube_test` baseline with these observable properties:

- the cube is visible again
- it rotates correctly
- the active path is still the VU opaque path
- the runtime VU counters are non-zero again for the scene
- later diagnostic-only compact-path behavior that produced black output is not part of the restored baseline

This slice does not need to preserve every intermediate compact-path experiment. It only needs to restore the last stable visual baseline that the user confirmed.

## Files and Ownership

### Runtime/VU Files

These files own the recovery:

- `Ps2RenderManager3D.cpp`
  - path selection, batch submission, VU counter publication, and opaque VU orchestration
- `Ps2VuVifPacketBuilder.cpp`
  - host-side packet layout and VIF/VU upload contract
- `Ps2OpaqueDraw3D.vsm`
  - VU microprogram behavior for the opaque path

### Test Files

Focused source-contract tests should be aligned with the recovered baseline instead of the later diagnostic state. These are the main guardrails:

- `builder.tests/Ps2NativeBuildExecutorTests.cs`
- any other small PS2 renderer/VU source-contract test files that now encode the black-screen diagnostic path instead of the last visible baseline

## Validation

Recovery is complete only when all of the following are true:

1. Focused PS2 source-contract tests for the recovered VU path pass.
2. The PS2 export rebuilds successfully.
3. PCSX2 boots the rebuilt export successfully.
4. The user confirms the cube is visible and rotating again.
5. The VU path is active again rather than silently replaced with a CPU fallback.

## Risks

### Contract Drift

The main risk is restoring one side of the VU contract without restoring the matching host or microprogram side. The host packet layout and `Ps2OpaqueDraw3D.vsm` semantics must move together.

### Mixed Baseline

Another risk is keeping part of the later diagnostic path while restoring only part of the older visible path. That would create a hybrid state that passes some source tests but still does not render correctly. The recovery should target one coherent known-good baseline.

### Over-Rollback

Rolling back too broadly could discard newer unrelated work such as texture-format capability changes. The recovery must stay limited to the VU renderer/runtime slice and its direct tests.

## Success Criteria

This recovery is successful when:

- `cube_test` is visibly rendering again in PCSX2
- the cube rotates
- the active path is still the PS2 VU opaque path
- the renderer is back on a stable baseline suitable for the next deliberate VU pass
