# PS2 Rendering Metrics

## VU Overlay Counters

`Set` measures CPU-side construction of VU triangle setup data.

`Prep` measures CPU-side preparation of per-triangle VU payload data.

`Emit` measures CPU-side emission of triangle payload bytes into the VIF packet.

`Enc` measures the total VU packet encode window around `Ps2VuVifPacketBuilder.AddOpaqueBatch`.

`Sub` currently reports aggregate VIF submit time. It is backed by `LastVuSubmitMilliseconds` and now aggregates `LastVuSubmitOnlyMilliseconds`.

`Wt` currently reports aggregate VIF wait time. It is backed by `LastVuWaitMilliseconds` and now aggregates `LastVuInitialWaitMilliseconds` plus any future explicit completion wait.

`Tri` reports submitted triangle count from the VU packet builder.

`Disp` reports VU batch dispatch count from the opaque batch builder.

## Split DMA Timing

`LastVuInitialWaitMilliseconds` measures the pre-batch `DMA_CHANNEL_VIF1` wait before building and submitting the next packet.

`LastVuCompletionWaitMilliseconds` is reserved for an explicit completion barrier if a future pass needs to measure one separately from the initial overlap wait.

`LastVuSubmitOnlyMilliseconds` measures the direct `dma_channel_send_packet2` call for the normal VIF1 submit branch.

The FPS overlay continues to show the aggregate `Sub` and `Wt` counters so the runtime display remains stable while the renderer internals gain more precise timing.

## Current Cube Baseline

After the split-timing instrumentation pass, the editor-owned PS2 `cube_test` build launched in PCSX2 with a visible rotating cube.

```text
Set: 0.4
Prep: 0.2
Emit: 0.1
Enc: 1.6
Sub: 0.0
Wt: 0.0
Drw: 2.0
Tri: 12
Disp: 1
Visible result: rotating cube
```
