# PS2 VU Immutable Source Reference Design

## Goal

Reduce the EE CPU cost of textured VU1 rendering from the current approximately
20 ms frame cost toward 5 ms by removing the repeated per-frame construction and
copying of immutable triangle source records. The existing 32-triangle VU
double-buffer layout is the stability baseline and must remain unchanged.

## Scope

This work applies to the textured VU1 path. It does not change the VU program's
input or GIF-output memory regions, untextured rendering, scene content, or
visual material semantics.

## Data ownership

Each packed model slice will retain 16-byte-aligned immutable source records.
One record contains the local-space positions, texture coordinates, and face
normal for a triangle. The cache is rebuilt only when its packed model slice is
created or replaced. It is invalidated with the packed model and never carries
world-space or camera-dependent data.

## Per-frame submission

For every visible textured slice, the renderer will create only its dynamic VU
shared state: transforms, lighting, GS state, texture binding, and triangle
count. The VIF DMA chain will upload that small shared-state block and reference
the cached immutable source block before issuing the current VU1 microprogram.
The source records will still be delivered to the exact VU input range used by
the proven double-buffer path.

## Incremental rollout

The first implementation enables the reference path for exactly one textured
slice. The existing copied-payload route remains available as a diagnostic
fallback. After it renders stably on Tilt Play Level 01 and reduces `Enc` and
`Asm`, the reference path can be expanded to every textured slice.

## Safety and failure behavior

The submission code validates alignment, source-record size, triangle count,
and packet capacity before emitting a DMA reference. An invalid cache entry is a
programming error and throws; it must not silently fall back to malformed or
default geometry. The prior experimental fixed VU memory layout is explicitly
out of scope because it hung before the first draw.

## Validation

Source-contract tests will prove the immutable cache record layout and the
one-slice submission selection. A PS2 ISO build of Tilt Play Level 01 will be
launched through the standard launcher. Acceptance requires stable geometry and
lighting, no FPS N/A/hang, and a lower measured `Enc`/`Asm` time than B251's
approximately 17.9/16.6 ms baseline.
