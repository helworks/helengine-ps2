# PS2 Colored Cubes Profiling Overlay

## Goal

Expose the existing PS2 renderer timing data needed to identify the CPU-side source of the Colored Cubes scene's 6.5 ms `3D` cost. The scene currently reports 5.6 ms of `Enc` time with zero VIF and GIF wait time, so this work is diagnostic only and must not change rendering behavior.

## Scope

The PS2 performance overlay will replace the showcase Light label and swatch while profiling is active. It will display a stable, fixed set of renderer-owned values:

- `Set`: triangle setup, including transforms, near-plane clipping, lighting, and source-payload construction.
- `Prep`: per-triangle preparation measured by the packet builder.
- `Emit`: GIF register-word emission measured by the packet builder.
- `Asm`: packet assembly and final packet-copy work.
- `Enc`: full packet encoding, retained as an end-to-end check.
- `Sub`: DMA submission time.
- `Gif`: GIF/GS wait time.
- `Tri`: submitted triangle count.
- `Bat`: VU/direct-GIF batch-dispatch count.
- `Bytes`: submitted GIF and VIF packet byte counts.

The first profiling build may add direct-GIF allocation and final-copy timing only when the existing `Set`, `Prep`, `Emit`, and `Asm` values cannot distinguish the dominant phase. New counters must be attributed to exactly one phase and must not overlap `Enc` without being labeled as a sub-phase.

## Data Flow

`Ps2VuVifPacketBuilder` already records setup, preparation, emission, lighting, payload-fill, and assembly timings. `Ps2RenderManager3D` aggregates these values per frame and the PS2 boot host publishes four platform-owned text rows through `Core::SetPerformanceOverlayTextRows`.

The shared `FPSComponent` already creates and lays out additional text rows, but its compatibility merge currently resolves platform-owned detail and additional text to empty strings. The change removes that suppression and merges the platform-owned detail/additional rows into the existing dynamic-row path. PS2 remains the sole producer of PS2 renderer values.

The overlay consumes only renderer-owned measurements. It does not infer GPU time from CPU encode time, and it preserves zero-valued VIF/GIF timing when the direct-GIF path does not wait on either subsystem.

## UI Behavior

The profiling overlay uses concise fixed labels and numeric milliseconds with one decimal place. It hides the Light label and color swatch for the profiling session so all available space belongs to diagnostics. The build identifier remains visible so measurements can be tied to the tested ISO.

## Validation

Source-contract tests verify that every published renderer metric is passed through the PS2 overlay contract and that the profiling UI no longer emits the Light label or swatch. A PS2 build verifies native compilation. Runtime validation records the full profiling line from the Colored Cubes scene; only then will a renderer optimization be selected.
