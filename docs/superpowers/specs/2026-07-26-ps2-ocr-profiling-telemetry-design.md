# PS2 OCR Profiling Telemetry Design

## Goal

Read the active PCSX2 PS2 profiling overlay through HelenUI OCR without inspecting screenshots or sending emulator input. Reduce visual noise in the profiling build by removing the Light toggle label and swatch.

## Scope

### Navigator service

The Navigator service will retain the raw OCR diagnostics returned by its existing recognition pass for each profile scope. It will expose a read-only session endpoint that returns the most recent OCR text lines and recognition timestamp.

The endpoint will not capture a new frame, send input, navigate, expose image bytes, or modify the attached target. It reports only the OCR data already produced by `POST /sessions/{id}/recognize`.

### Colored Cubes scene UI

The Demo Disc Colored Cubes scene will no longer attach `DemoDiscLightToggleComponent`, which owns the Light label and visual swatch. The directional light remains authored and active. The PS2 performance overlay remains visible and continues to show the hardcoded build identifier and timing rows, including the render metrics required for the 2.0 ms target.

## API contract

`GET /sessions/{sessionId}/ocr` returns the latest OCR diagnostic for every loaded scope. Each scope includes its identifier, capture/recognition timestamp, OCR engine status, and ordered text lines. A session that has not yet completed recognition returns no text lines rather than triggering capture.

## Verification

1. Navigator unit tests verify that a completed recognition result is retained and returned without invoking capture or input.
2. The Demo Disc source test verifies that Colored Cubes omits the Light-toggle component while retaining the directional-light entity.
3. Build and launch a new PS2 artifact through the existing deterministic build flow.
4. Attach HelenUI read-only, call `POST /recognize`, then `GET /ocr`; confirm the result contains the new build number and a non-`N/A` FPS/timing row.

## Non-goals

- No navigation changes.
- No controller or keyboard input changes.
- No screenshot inspection or screenshot endpoint use.
- No renderer optimization is included in this telemetry/UI change.
