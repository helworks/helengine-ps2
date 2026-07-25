# PS2 Persistent CPU Textured Packet Cache

## Goal

Reduce the Colored Cubes direct-GIF CPU submission cost without changing the
established perspective-correct STQ, full-frustum clipping, backface, or
lighting behavior. The primary metric is the `Drw` overlay timing, excluding
v-sync/present, with a target of 2.0 ms or lower.

## Context

The stable Colored Cubes path is the CPU textured direct-GIF fallback. It is
correct because it performs full-frustum clipping before projection and emits
perspective-correct STQ values. The VU textured route was measured at 10.3 ms
because it dispatches VU work for every 12-triangle cube. A transient
per-frame indexed-projection cache measured 7.5 ms, worse than the 6.8 ms
baseline, because its allocations and bookkeeping outweighed its reuse.

## Options Considered

1. Restore the VU textured path. This retains a CPU-light setup but repeats
   VIF upload and MSCAL dispatch for each cube, which is already measured as a
   regression.
2. Cache transformed/projected vertices. This is unsafe as a generic cache:
   world, camera, projection, and clipping state change every frame. The prior
   version also regressed.
3. Cache immutable triangle source data and reuse packet storage. This is the
   selected design. It removes repeated packed-buffer decoding, runtime-index
   lookup, transient packet-vector allocation, and packet-vector copying while
   retaining every dynamic transform and safety check.

## Architecture

`Ps2VuVifPacketBuilder` owns a bounded persistent cache of immutable textured
triangle sources. An entry is identified by the packed-model pointer, runtime
model pointer, and packed-model triangle-vertex count. It stores one source
record per triangle: three local-space positions, the pre-summed face normal,
and three texture coordinates. It does not retain a material, texture, world,
camera, projection, light, or GS state.

The cache is populated only after validating packed source ranges. A lookup
with a changed identity or vertex count rebuilds that entry. Entries use a
fixed capacity with least-recently-used replacement so loading many assets
cannot cause unbounded persistent memory growth. The cache holds only cooked,
immutable model data; dynamic render state is deliberately recomputed every
frame.

The builder also retains reusable CPU scratch buffers for direct-GIF words,
VIF triangle packets, and clipping vertices. `Reset` clears their contents but
keeps capacity. Direct-GIF submission writes a batch header once and appends
each triangle payload as it is produced, removing the intermediate full packet
array and its later copy from this route.

## Per-Frame Data Flow

1. Resolve the cached immutable source record for the batch triangle.
2. Apply the current world-view transform and world-space normal transform.
3. Classify against the full screen frustum before projection.
4. Emit the existing perspective-correct STQ and RGBAQ payload for fully
   visible triangles, or use the existing clipping routine for partial ones.
5. Append the direct-GIF batch state once and each valid triangle payload to
   the reusable output buffer.

No cache result is used when a triangle is clipped. Clipped vertices remain
generated from the current transformed positions, preserving the fixes for
behind-camera vertex explosions and affine texture warping.

## Failure Handling and Observability

Invalid model data or out-of-range slices continue to skip exactly as the
current renderer does. Cache construction must not synthesize positions,
normals, or texture coordinates. The existing triangle timing and submitted
triangle diagnostics remain intact. A build-number bump and OCR of the
script-launched PCSX2 window will establish which ISO was measured.

## Tests and Validation

Source-contract tests will first require persistent cache ownership, immutable
source records, bounded eviction, and direct-GIF append behavior; they must
fail before the implementation is added. The focused test project will then
be run. Finally a `colored-cube-grid` PS2 ISO will be built with the build
waiter, launched only through `scripts/launch_in_emulator.ps1`, and read using
HelenUI's OCR path. Success requires correct multi-cube output reported by the
user and an OCR-visible finite frame metric; the numeric target is met only if
the `Drw` metric is at or below 2.0 ms.
