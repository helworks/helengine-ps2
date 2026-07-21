# PS2 On-Screen Boot Log Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Show retained PS2 boot checkpoints on screen during OPL startup so hangs before the first game frame remain diagnosable.

**Architecture:** Reuse `PresentBootLogHistoryToDebugConsole()` and the existing 30-line `BootLogHistory`. Add a boot-only flag; `BootLog` refreshes the debug console only after graphics initialization enables it, and `Run()` disables it immediately before the normal frame loop. Fatal-halt rendering remains unchanged.

**Tech Stack:** C++20 PS2 runtime, gsKit/debug screen API, .NET xUnit source-contract tests.

---

### Task 1: Add the failing source-contract test

**Files:**
- Modify: `builder.tests/Ps2BootHostSourceTests.cs`

- [x] Add a test that requires a boot-screen flag, its enable after graphics initialization, its use from `BootLog`, and its disable before `PresentBootFrame()`.
- [x] Run it; the test project is currently blocked by an unrelated existing compile error in `Ps2PlatformAssetBuilderTests.cs:2761`.

### Task 2: Implement boot-only on-screen refresh

**Files:**
- Modify: `src/platform/ps2/Ps2BootHost.cpp`

- [x] Add `EnableOnScreenBootLog` initialized to `false` beside `DebugConsoleReady`.
- [x] Refresh the existing on-screen history from `BootLog` only while the flag is enabled.
- [x] Enable the flag after graphics initialization and before startup-scene loading.
- [x] Disable the flag immediately before `PresentBootFrame()`.
- [x] Leave video mode, asset paths, frame rendering, and fatal-halt behavior unchanged.

### Task 3: Verify and review

- [x] Run `git diff --check` successfully.
- [x] Inspect the source/test diff for scope violations.
- [ ] Verify the rebuilt ISO on NTSC hardware through OPL.
