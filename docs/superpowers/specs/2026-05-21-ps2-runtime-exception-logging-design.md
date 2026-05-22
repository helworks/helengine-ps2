# PS2 Runtime Exception Logging Design

## Goal

Make PS2 runtime exceptions easier to diagnose by routing caught runtime exceptions through one consistent boot-log path and by removing unconditional compact-disc probe noise from normal boot output.

## Current State

`src/platform/ps2/Ps2BootHost.cpp` already catches exceptions around startup scene load, frame update, draw, and present boundaries. Those catches log short messages with `BootLog(...)` and then halt. The same file also emits unconditional `BootLogDiscProbe(...)` output during runtime initialization, which adds low-signal startup noise unrelated to most failures.

## Requirements

1. Runtime exceptions in the PS2 ELF must continue to be caught and written to the boot log.
2. Exception logging should use one shared formatting path so phase labels stay consistent.
3. Startup scene load failures should include existing scene-load diagnostic fields when available.
4. Unconditional compact-disc probe logging must be removed from the normal startup path.
5. The low-level probe helper may remain available for future diagnostics, but it must not run during standard boot.

## Recommended Approach

Add a small logging helper in `Ps2BootHost.cpp` that accepts a phase label plus either `Exception*`, `std::exception`, or an unknown exception case. That helper should emit the phase-prefixed log entry and, where relevant, append runtime scene-load diagnostics already exposed by `SceneLoadService` and `SceneManager`.

During `InitializeRuntime()`, remove the unconditional `BootLogDiscProbe(...)` calls after `cdvd ready`. This keeps boot logs focused on meaningful lifecycle and failure output.

## Error Handling

- Caught runtime exceptions remain fatal on PS2 after logging.
- Unknown exceptions continue to log as `unknown`.
- If the engine core or diagnostic providers are not yet available, the helper should log only the phase and message without inventing fallback state.

## Testing

Update `builder.tests/Ps2BootHostSourceTests.cs` to assert:

- startup-scene boot flow still exists
- disc probes are no longer called during normal runtime initialization
- centralized runtime exception logging hooks are present for startup-scene failures
