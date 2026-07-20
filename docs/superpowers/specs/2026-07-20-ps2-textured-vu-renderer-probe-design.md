# PS2 Textured VU Renderer Probe Design

## Goal

Create a standalone DemoDisc PS2 renderer probe containing four boxes and one coin. All five objects use one cooked material and one shared texture so the scene isolates textured VU submission, cooked asset loading, UV interpretation, depth, and culling without Tilt Trial gameplay dependencies.

The probe must render through VU by default. The existing CPU textured path remains available as an explicit diagnostic reference, but it must not silently replace malformed VU submission.

## Context

The current PS2 renderer routes textured batches through the legacy CPU/GIF path because the aggregate textured VU path was considered unsafe. This makes textured rendering slow and leaves the underlying vertex-explosion and texture-mapping faults unresolved.

The probe is therefore a controlled test boundary rather than a replacement for Tilt Trial. It exercises the normal cooked model/material pipeline while keeping the scene small enough to diagnose one packet at a time.

## Architecture

DemoDisc will generate a distinct PS2 renderer-probe scene with:

- exactly four box entities and one coin entity;
- one fixed camera and one directional light;
- one shared cooked material and one shared cooked texture;
- fixed transforms and no gameplay, physics, or input dependencies;
- an explicit PS2 probe scene identifier that does not alter Tilt Trial scene selection.

The coin may use a separate cooked mesh, but it must use the same material and texture as the boxes. The probe scene generator owns authored scene structure; the PS2 renderer owns packet submission and diagnostics.

The PS2 renderer will add a minimal textured VU mode that submits one object/material batch at a time. Textured aggregate batching stays disabled until the per-object packet is proven. The CPU textured path remains a comparison mode only.

Each textured VU packet will have one authoritative layout covering cooked position, normal, and UV blocks; transform constants; GIF register templates; and GS texture state. The packet builder must validate packed-mesh header offsets and qword alignment before dispatching.

## Data flow

1. DemoDisc authoring writes the probe scene, shared material, shared texture, and box/coin cooked model inputs.
2. The platform cooker emits PS2 model and material assets through the existing project build pipeline.
3. The PS2 runtime loads the cooked models and shared material/texture records.
4. The render manager builds one textured VU submission per probe object.
5. The VU packet consumes the validated cooked streams, transforms vertices, emits GIF triangle data, and samples the shared GS texture.
6. Runtime diagnostics publish object count, triangle count, VU dispatch count, packet bytes, texture dimensions, and CPU/VU timing.

## Failure behavior

Missing UV data, invalid packed-mesh offsets, unsupported texture dimensions, malformed packet sizes, missing material records, and missing texture records fail explicitly during loading or packet preparation. The system must not synthesize default geometry, UVs, materials, or hidden CPU fallbacks.

## Validation

Validation proceeds from smallest to largest:

1. Authoring tests verify the exact five-object scene shape and shared material/texture references.
2. Cooker tests verify packed-mesh headers, UV blocks, and 16-bit triangle streams for both meshes.
3. Renderer contract tests verify VU register lists, qword offsets, texture dimensions, and dispatch mode.
4. A CPU reference run establishes expected visibility, triangle counts, screen bounds, and UV orientation.
5. VU is enabled first for one box, then four boxes, then the coin. Each stage checks for vertex stability and texture orientation.
6. The final PS2 build uses the existing DemoDisc output convention and the smallest relevant validation commands.

## Success criteria

- All five objects render through VU by default.
- No vertex explosion occurs when moving from one object to five.
- The shared texture has the same orientation and stable mapping on boxes and coin.
- Depth testing and back-face behavior remain stable.
- VU frame time is materially better than the current CPU textured path.
- Tilt Trial scene generation and runtime selection remain unchanged.
