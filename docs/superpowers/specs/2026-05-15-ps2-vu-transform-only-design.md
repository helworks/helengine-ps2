## PS2 VU Transform Only Design

### Goal

Restore only VU-side vertex transform for the opaque untextured `cube_test` path while preserving the currently working packet and shading baseline.

### Current Stable Baseline

The current `cube_test` runtime path is stable with these properties:

- real editor-driven export succeeds
- real cube geometry reaches the PS2 runtime
- the helper-generated untextured GIF packet shape is valid
- VU submission through `xgkick` works
- triangle `XYZ2` values are generated on CPU
- flat lighting color is generated on CPU

This baseline renders a spinning lit-looking cube and must remain recoverable at all times.

### Problem Statement

The next renderer step must move work from CPU to VU1 without reintroducing the earlier multi-variable failures. Previous attempts mixed:

- packet layout changes
- VU transform changes
- VU lighting changes
- VU culling changes

That made failures ambiguous and expensive to debug.

### Scope

This step changes only one thing:

- VU1 computes the three `XYZ2` vertex outputs for one opaque-untextured triangle packet

Everything else stays as it is today:

- helper-generated packet format stays unchanged
- CPU flat color stays unchanged
- CPU path selection stays unchanged
- CPU culling stays unchanged
- CPU lighting stays unchanged

### Non-Goals

This step does not include:

- VU lighting
- VU clip/cull rejection
- textured opaque batches
- alpha batches
- packet format redesign
- scene/build-graph work

### Target Architecture

#### CPU Responsibilities

CPU remains responsible for:

- scene traversal
- proxy classification
- opaque-untextured batch selection
- helper-generated GIF packet template creation
- flat color resolution
- preparing one per-triangle payload containing raw triangle positions and transform constants

CPU must stop writing final `XYZ2` values for the active opaque-untextured VU path under this step.

#### VU Responsibilities

`Ps2OpaqueDraw3D.vsm` becomes responsible for:

- reading raw triangle positions
- reading the transform constants supplied by CPU
- transforming the three vertices into clip/screen space
- converting them into GS `XYZ2` values
- writing those `XYZ2` values into the already validated packet slots
- issuing `xgkick`

The VU program must not introduce lighting, culling, or packet-layout changes in this step.

### Data Contract

The helper-generated packet remains the source of truth for the untextured packet shape.

The payload contract for this step is:

- raw triangle positions are supplied for vertices A/B/C
- transform constants required to derive final `XYZ2` are supplied
- the packet already contains valid non-position register data
- the VU program patches only the three `XYZ2` slots before `xgkick`

The packet slot locations must remain identical to the currently working CPU-projected path.

### Implementation Boundary

The primary files for this step are:

- `src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.cpp`
- `src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.hpp`
- `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- `src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm`

`Ps2RenderManager3D` should not change behavior beyond consuming the updated setup/builder path.

### Safety Rules

- keep the current xgkick-only baseline recoverable
- do not change packet register order during this step
- do not reintroduce fixed diagnostic triangle mode
- do not bundle lighting or culling changes with transform restoration
- if VU transform fails, revert only to the last CPU-projected helper-packet baseline

### Verification

The verification target is `cube_test` only.

Success means:

- real editor export succeeds
- PCSX2 boots the fresh ISO
- `cube_test` still renders a spinning cube
- the packet remains visually stable
- the cube remains positioned and sized correctly
- CPU flat lighting still appears as before

Failure indicators for this step are:

- black screen
- giant triangle or square corruption
- missing cube with intact text overlay
- VIF/GIF/FIFO assertion regressions

### Next Step After Success

If this step succeeds, the next isolated step is VU-side cull/clip behavior while keeping:

- the helper-generated packet
- the VU transform
- the CPU flat color

Lighting remains a later step.
