# PS2 Rendering Performance Benchmark

## Standard Capture

1. Build the newest DemoDisc PS2 ISO with the render test scene selected.
2. Launch that exact ISO in PCSX2.
3. Wait for the timing sample window to complete.
4. Capture the `frame timing avg` boot-log line.
5. Repeat one free-camera path: scene start, close to a large box, then the coin-behind-camera angle.

## Required Result Row

`commit | scene | camera path | FPS | Drw | Set | Enc | Vif | Sub | Gif | Leg | Tri | Pkt | Bytes | Grp | visual result`

Do not compare captures made from different ISOs, scene selections, timing sample windows, or camera paths.

## Baseline

| Commit | Scene | Camera path | FPS | Drw | Set | Enc | Vif | Sub | Gif | Leg | Tri | Pkt | Bytes | Grp | Visual result |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Pre-foundation user observation | DemoDisc render test | Standard path | 12.0 | 72.1 | 33.1 | Not captured | Not captured | Not captured | Not captured | Not captured | Not captured | Not captured | Not captured | Not captured | No known clipping regression |
