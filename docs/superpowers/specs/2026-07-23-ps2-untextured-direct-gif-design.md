# PS2 Untextured Direct GIF Design

## Goal

Render every opaque untextured batch, including every Colored Cubes instance, without retaining render payload data in VU1 memory after its VIF submission ends.

## Problem

The existing untextured route emits one VIF packet per bounded aggregate. Each packet stores triangle payloads in VU1's two `xtop` buffers and invokes the VU microprogram once per triangle. A VIF DMA completion proves only that command data was consumed; it does not give later aggregates ownership of the VU buffers. When more than one outer VIF aggregate is submitted, later payloads overwrite data still needed by earlier VU work. Colored Cubes reliably displays only the final four-cube aggregate.

## Chosen Architecture

Opaque untextured rendering will use the same ownership model as the stable textured direct-GIF route. The CPU already transforms vertices into view space, clips triangles against the screen frustum, calculates flat lighting, and constructs the untextured GIF template. It will additionally project each emitted vertex and write final `RGBAQ` plus `XYZ2` GIF registers into an owned GIF DMA packet.

Each aggregate packet is therefore self-contained:

1. The packet builder owns the complete GS command stream for all accepted triangles.
2. The render manager submits that stream once through `DMA_CHANNEL_GIF` and waits before freeing its packet memory.
3. No untextured aggregate uses `UNPACK`, `xtop`, `MSCAL`, or VU packet slots.

The existing VU microprogram stays loaded for now because no unrelated route is removed by this change. The untextured render manager path simply no longer depends on it.

## Packet Format

The first triangle in a compatible untextured group writes the existing GIF state and primitive tag. Every triangle then writes its flat color followed by the three final `XYZ2` vertices. The builder preserves the existing triangle ordering, clip behavior, depth conversion, and clockwise/counter-clockwise behavior. It does not use affine texture registers because this route is untextured.

## Limits and Batching

The bounded untextured aggregate limit remains a CPU/GIF packet size limit. It must not be raised to compensate for VU buffer reuse. Splitting a compatible group into multiple packets is correct because each packet owns its final GS data.

## Validation

Automated source tests must prove that:

- the untextured builder can create a direct-GIF stream;
- the untextured render manager submits that stream over GIF DMA and does not allocate or advance VIF packet slots for it;
- the direct-GIF stream contains final color and position registers for every triangle;
- the VIF aggregate cap is restored to its measured safe size rather than being used as a correctness workaround.

Manual verification is successful only when a newly stamped ISO boots the Colored Cubes scene, displays all 16 cubes, and reports a numeric FPS value rather than `N/A`.

## Non-Goals

- No change to textured direct-GIF rendering.
- No new VU microprogram or VU loop protocol.
- No changes to asset cooking, camera behavior, materials, or UI layout.
