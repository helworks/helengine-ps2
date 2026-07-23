# PS2 Render Timing Overlay

## Goal

Expose the PS2 renderer cost independently from v-sync and presentation pacing, so the Colored Cubes scene can be optimized against a real 2.0 ms render budget.

## Timing contract

The compact timing overlay will display these values on its renderer-detail row:

`Drw <ms> 3D <ms> Enc <ms> Vif <ms> Gif <ms>`

- `Drw` is the complete 3D render cost: CPU 3D draw submission plus the wait for submitted GIF work to drain. It excludes 2D overlay rendering, queue execution, flip, and v-sync/present pacing.
- `3D` is CPU time spent in `EngineCore->Draw()` before the GIF drain wait.
- `Enc` is VU packet encoding time reported by the PS2 3D render manager.
- `Vif` is time waiting for VIF1 packet-slot reuse.
- `Gif` is time spent waiting for the GIF channel to drain.

The render-performance target is `Drw <= 2.0 ms`. FPS and total frame milliseconds remain informational only because they can be capped by presentation timing.

## Implementation

`Ps2BootHost.cpp` already records separate draw-3D and GIF-wait samples. It will calculate the displayed `Drw` value from those two samples and format it on the existing detail row alongside the existing encoder, VIF, and GIF metrics. The existing FPS row remains unchanged.

No renderer work, packet data, synchronization, or presentation behavior changes. The change is limited to overlay formatting and its source-level coverage.

## Validation

Add/adjust the source test to assert that the detail row includes `Drw`, `3D`, `Enc`, `Vif`, and `Gif`, and run that focused test. A new PS2 build must show the values in the Colored Cubes scene; `Drw`, not FPS frame time, will be used for subsequent optimization decisions.
