# PS2 On-Screen Boot Log Design

## Goal

Make PS2 startup diagnostics visible when the executable is launched through OPL, where the existing `host:` and `host0:` file paths are unavailable.

## Design

Reuse the existing `PresentBootLogHistoryToDebugConsole()` implementation and its retained 30-line history. During the boot phase, each boot-stage log message will refresh that console after graphics initialization, allowing the last completed checkpoint to remain visible if startup hangs while loading the packaged scene.

The on-screen refresh is diagnostic-only and will be disabled immediately before the normal frame loop begins. The existing fatal-halt refresh remains in place so runtime exceptions and explicit initialization failures continue to display their final messages.

## Scope

- Add one boot-diagnostic enable flag with a default enabled value for the OPL diagnostic build.
- Refresh the existing on-screen history only while boot diagnostics are enabled.
- Disable boot-log refresh before `PresentBootFrame()` starts.
- Add a source-contract test covering the enable/disable boundaries.
- Do not change video mode, asset loading, or rendering behavior.

## Verification

Run the focused PS2 builder test project and inspect the resulting source diff. Hardware verification consists of booting the rebuilt ISO through OPL and reading the last visible checkpoint line.
