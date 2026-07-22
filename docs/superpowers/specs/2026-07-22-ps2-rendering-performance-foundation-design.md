# PS2 Rendering Performance Foundation Design

## Goal

Make PS2 rendering performance measurable and scalable across engine projects, beginning with the DemoDisc render test scene. The initial target is a stable 30 FPS frame budget of 33.3 ms while preserving correct depth, texture, alpha, and camera-near clipping behavior.

## Current Evidence

The measured DemoDisc frame reports approximately 12 FPS, `Drw 72.1 ms`, and `Set 33.1 ms`. `Set` is composed of renderer-side proxy synchronization, frame planning, VU batch construction, triangle preparation, lighting, payload filling, packet assembly, and packet encoding. It is therefore part of `Drw`, not a separate GS cost.

The existing overlay does not distinguish all of the following costs:

- EE geometry preparation and VIF packet assembly;
- time waiting for VIF1/VU1 work to complete;
- GS completion and fill-rate cost;
- legacy CPU textured submission; and
- exceptional per-triangle clipping work.

The normal renderer initializes depth buffering on. When it is enabled, opaque work uses `RenderOpaqueWithVuPath`. That path waits for VIF1 before each opaque batch. Textured opaque batches may use `DrawOpaqueProxyLegacy`, which performs CPU-side triangle transforms, clipping, lighting, and per-triangle GS submission. The untextured path also performs CPU clipping for triangles that reach the camera frustum boundary.

The current numbers prove that renderer draw work is too expensive; they do not prove that the GS is the limiting processor. The implementation must measure EE, VIF/VU, and GS time independently before selecting a GS-specific optimization.

## Scope

This design applies to the PS2 3D renderer in `helengine-ps2`. It defines a reusable engine path for opaque world and dynamic geometry.

It includes:

- a consistent PS2 renderer timing and counter contract;
- opaque submission batching and DMA scheduling;
- a production VU/GIF route for textured opaque geometry;
- conservative proxy-level visibility classification;
- fast and exceptional clipping paths; and
- quality controls and benchmark scenes for later GS/fill-rate work.

It does not include:

- changing DemoDisc gameplay or authored scene content merely to improve a benchmark;
- replacing the existing UI/2D renderer;
- changing alpha blending semantics; or
- removing the existing legacy renderer until the corresponding VU path has passed visual and performance validation.

## Options Considered

### Option A: Tune GS effects and texture quality first

This could lower fill rate in a GS-bound scene, but it cannot establish whether the current 72 ms draw cost is EE, VIF/VU, or GS work. It also leaves CPU textured submission and per-batch synchronization intact.

### Option B: Rewrite the renderer as a single large VU pipeline

This promises the highest eventual throughput, but combines packet layout, clipping, texturing, state management, and DMA lifetime changes into one difficult-to-debug migration. It risks another long visual-regression cycle.

### Option C: Instrument, then migrate one opaque path at a time

This design selects Option C. It first establishes measured ownership of frame time, then makes opaque submission larger and asynchronous, moves textured opaque work onto the VU path, and adds visibility rejection. Each change has a narrowly defined fallback and benchmark gate.

## Architecture

The renderer uses the following ownership model:

```text
Frame planner -> visibility classifier -> opaque state sorter -> VU batch builder
             -> VIF packet chain -> VIF1/VU1 -> GIF stream -> GS framebuffer/Z buffer
```

The EE owns frame planning, conservative culling, material/state sorting, packet memory lifetime, and DMA scheduling. VU1 owns repeated per-vertex transform, projection, simple opaque lighting, and production GIF output for compatible opaque batches. The GS owns depth testing, rasterization, texture sampling, blending, and final display.

The renderer has three explicit routes:

1. **Opaque VU fast path:** fully visible opaque batches with supported material state. It uses compact static mesh input, shared batch constants, and one or more bounded VIF packets without a wait between every compatible proxy.
2. **Opaque VU clipping path:** opaque batches intersecting the near or screen frustum. It uses correct homogeneous clipping, but only for the boundary batch or triangles, never for every fully visible mesh.
3. **Legacy diagnostic path:** unsupported or explicitly diagnosed work. It remains available behind an explicit renderer diagnostic setting and reports its triangle count and elapsed time. It is not the normal textured opaque path after the textured VU milestone is complete.

Alpha geometry remains a separate sorted path. Its optimization is intentionally deferred because preserving blend order has higher correctness risk.

## Measurement Contract

`Ps2RenderManager3D` must publish one coherent per-frame metrics record. The boot overlay and boot log consume the same record.

### Timings

- `ProxySyncMs`: renderer proxy synchronization.
- `FramePlanMs`: frame plan construction and sorting.
- `BatchBuildMs`: visible opaque batch construction.
- `PacketEncodeMs`: EE VIF/GIF payload preparation.
- `VifWaitMs`: waiting before packet-memory reuse or for VIF1/VU1 completion.
- `VifSubmitMs`: DMA submission work excluding waits.
- `GsFinishMs`: explicit GS completion wait measured at a defined end-of-frame boundary.
- `OpaqueLegacyMs`: time spent in the legacy opaque route.
- `AlphaMs`, `GlowMs`, and `Draw3dMs`: separate later-stage pass and total timings.

### Counters

- visible, frustum-rejected, and clipping-path proxy counts;
- source, submitted, clipped, and legacy-rendered triangle counts;
- VIF packet/dispatch count and byte count;
- material and texture-state change count; and
- opaque VU, opaque clipping, and legacy batch counts.

All timing values are diagnostic values. They must not change render ordering or introduce a synchronous wait except for the deliberately measured GS completion sample.

## Milestones and Gates

### Milestone 1: Measurement and baseline

Add the metrics record, overlay/log display, and benchmark capture format. Use the DemoDisc render test scene as the primary baseline, with the existing clipping test camera positions as correctness checks.

Gate: the log identifies EE preparation, VIF/VU wait, submit, legacy, and GS completion costs for the same scene and camera.

### Milestone 2: Opaque submission throughput

Batch compatible opaque VU work by state and texture identity. Use reusable packet slots and wait only before a slot is reused. Preserve an explicit synchronous diagnostic mode for fault isolation.

Gate: the scene is visually unchanged; VIF dispatch count and `VifWaitMs` decrease without packet corruption or intermittent geometry loss.

### Milestone 3: Textured opaque VU route

Replace normal textured opaque legacy submission with compact textured VU input and VU-generated GIF output. Maintain the legacy route only for diagnostics or unsupported states.

Gate: textured models retain perspective-correct mapping, depth precision, and clipping correctness; `OpaqueLegacyMs` is zero for supported opaque textured materials.

### Milestone 4: Visibility and clipping tiers

Classify proxy bounds against the camera frustum. Reject outside proxies, route fully-inside proxies directly to VU, and reserve homogeneous clipping for intersecting geometry.

Gate: off-camera objects reduce submitted triangles and VIF bytes; the camera-near box and coin cases remain stable without fullscreen geometry.

### Milestone 5: GS/fill-rate quality policy

Only after the metrics show GS completion is material, add PS2 quality tiers for glow, alpha overdraw, texture filtering, texture format, resolution, and LOD. Opaque rendering must remain front-to-back with depth testing enabled.

Gate: each tier has a documented quality/performance tradeoff and does not hide an EE/VU regression.

## Performance Targets

The first acceptance target is a stable 30 FPS in the DemoDisc render test scene under the agreed camera path.

- Frame target: 33.3 ms average, with no recurring frame above 40 ms during the captured path.
- `Set`/EE preparation target: below 10 ms average after Milestones 2 and 3.
- Opaque legacy target: zero normal-path textured opaque triangles after Milestone 3.
- Submission target: compatible opaque meshes must not require a VIF1 wait per proxy.
- Correctness target: no mesh warp when geometry crosses behind the camera or any screen-frustum side.

These targets are acceptance gates, not assumptions about which processor will be dominant after each milestone.

## Validation

Every milestone requires:

1. Focused builder source tests for the renderer contract and metric publication.
2. A native PS2 build of the newest DemoDisc render-test ISO, not a previously completed output.
3. PCSX2 verification of the baseline camera path and the near-camera/clipping positions.
4. A recorded before/after metrics line with the same build configuration and scene.
5. A small commit containing only the milestone's engine, test, and documentation changes.

The validation matrix uses:

- DemoDisc render test: general opaque workload and performance target.
- Textured test geometry: textured VU route, perspective correction, and depth.
- Camera-near box: near plane and screen frustum clipping.
- Coin-behind-camera angle: behind-camera clipping regression.
- Alpha/glow test: deferred pass cost and blend correctness.

## Rollback and Failure Handling

Each renderer migration keeps its previous route behind an explicitly named diagnostic switch until the milestone gate passes. A black screen, missing geometry, unstable depth, invalid texture mapping, or metric regression stops the current milestone. Restore only that milestone's diagnostic route, capture the counters, and diagnose packet layout, VIF transfer size, VU memory offsets, or state ordering before proceeding.

No fallback may silently become the normal production path. Metrics must expose every legacy route invocation.

## Plan Decomposition

The work is intentionally split because the milestones have different correctness risks and can be shipped independently:

1. **Performance foundation plan:** Milestone 1 and the packet-lifetime portion of Milestone 2.
2. **Textured opaque VU plan:** Milestone 3.
3. **Visibility and clipping tier plan:** Milestone 4.
4. **GS quality policy plan:** Milestone 5, only when GS timing establishes the need.

The first implementation plan will cover the performance foundation. It creates the measurement contract and removes the known per-compatible-batch submission serialization without changing alpha behavior, post effects, or authored content.
