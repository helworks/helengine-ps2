# PS2 Flat-Color Diagnostic Renderer Design

## Goal

Add one temporary PS2-only diagnostic rendering path that strips 3D scene rendering down to pure geometry submission. The path must make it easy to tell whether the remaining corruption in the directional-shadow scene comes from geometry/rasterization or from material, texture, alpha, and lighting state.

## Problem

The current PS2 renderer now reaches the point where it loads the scene, submits a plausible number of triangles, and shows motion. However, the image is still visually corrupted. The renderer still combines several systems at once:

- world transforms
- clipping and projection
- front-face culling
- textured material sampling
- alpha mode setup
- per-vertex lighting
- HDR glow emission

That makes the current output ambiguous. A broken frame could still be caused by geometry submission, by state setup, or by material-dependent behavior. The next debugging step must remove as many variables as possible without changing the underlying scene content.

## Diagnostic Scope

This diagnostic step changes only the PS2 3D draw path. It does not change:

- scene assets
- packaged material assets
- model assets
- camera setup
- object transforms
- clipping
- projection
- triangle culling
- depth handling

It changes only how already-submitted 3D proxies are shaded and submitted to gsKit.

## Chosen Approach

Introduce one temporary flat-color diagnostic mode inside the PS2 renderer.

When enabled, every 3D proxy is rendered as:

- opaque
- untextured
- flat colored
- unlit
- without HDR glow
- without material alpha branching

Each proxy receives one stable debug color so scene pieces remain distinguishable. The color should be deterministic across frames and should not depend on material content. A stable per-object color is preferred over a single gray because it makes overlap, ordering, and object identity easier to see while still isolating geometry from material behavior.

## Why This Approach

This is the smallest useful step because it removes the highest-risk rendering variables without disturbing the geometry path we are trying to verify.

If the scene becomes structurally correct in flat-color mode, the remaining bug is in one of:

- texture usage
- alpha setup
- lighting
- glow
- material-specific draw state

If the scene remains distorted in flat-color mode, the remaining bug is still in:

- transform application
- clipping
- projection
- screen-space mapping
- triangle winding/culling
- depth behavior
- gsKit primitive submission

## Design

### Renderer Entry Point

The change lives in [Ps2RenderManager3D.cpp](/C:/dev/helworks/helengine-ps2/.worktrees/normalize-camera-viewport-core/src/platform/ps2/rendering/Ps2RenderManager3D.cpp).

The renderer will gain one temporary diagnostic switch that forces 3D rendering through a flat-color path. The switch may be implemented as a local compile-time constant or a tightly scoped internal helper decision, but it must remain PS2-only and must not affect shared engine code.

### Diagnostic Material Behavior

In diagnostic mode:

- `ResolveTexture(...)` is not used for 3D submission
- `ApplyMaterialAlphaState(...)` is bypassed for 3D submission
- `ShouldDrawAlphaTestTriangle(...)` is bypassed
- `ResolveVertexColor(...)` is bypassed
- `ShouldEmitHdrGlow(...)` is bypassed

Instead, each submitted triangle receives one constant color derived from its owning proxy.

### Per-Object Color

Each proxy gets one stable color chosen from a compact debug palette. The palette should contain clearly separated colors that remain readable on CRT-like output, such as:

- red
- green
- blue
- yellow
- cyan
- magenta
- orange
- white

The selected color must be stable for the same proxy across frames. The mapping can be based on draw order index, proxy index, or a stable entity identifier hash. The exact mapping mechanism is an implementation detail, but stability is required.

If stable per-object color proves more invasive than expected, a single neutral gray is an acceptable fallback for this step. However, the implementation should attempt per-object coloring first.

### Triangle Submission

Diagnostic mode still uses the same projected vertices and the same triangle submission path to gsKit. The only intended change is that triangles submit through the untextured gouraud path with identical per-vertex colors for all three corners.

That preserves:

- the same triangle count
- the same projected coordinates
- the same culling decisions
- the same depth path

while removing material-dependent behavior.

## Testing Strategy

Follow red-green before changing production code.

### Failing Test

Add one PS2 source-level regression in [Ps2NativeBuildInputsTests.cs](/C:/dev/helworks/helengine-ps2/.worktrees/normalize-camera-viewport-core/builder.tests/Ps2NativeBuildInputsTests.cs) that proves the diagnostic renderer path:

- bypasses textured 3D submission for the diagnostic mode
- bypasses material-driven vertex color resolution
- bypasses HDR glow emission
- submits through the untextured triangle primitive call

This test does not need to validate pixel output. It only needs to lock the intended source-level behavior of the temporary diagnostic mode.

### Verification

After the source-level regression passes:

1. Run the full PS2 builder test suite.
2. Rebuild the PS2 export for the city project.
3. Boot the ISO and inspect:
   - whether the main menu still behaves
   - whether the directional-shadow scene becomes structurally readable
   - whether geometry still stretches, flickers, or overlaps incorrectly

## Non-Goals

This step does not try to:

- restore final correct materials
- fix texture rendering
- fix alpha blending
- fix PS2 lighting quality
- preserve production-ready visuals

It is strictly a debugging isolation pass.

## Exit Criteria

This diagnostic step is successful when:

1. the PS2 scene renders with flat, stable, per-object colors
2. the user can visually determine whether the geometry itself is structurally correct
3. the next debugging step can focus either on material state or on remaining geometry/raster issues, based on the observed output
