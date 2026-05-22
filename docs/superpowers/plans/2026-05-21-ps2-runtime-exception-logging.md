# PS2 Runtime Exception Logging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Clean up PS2 boot logging so runtime exceptions are consistently logged and normal startup no longer emits compact-disc probe noise.

**Architecture:** Keep the change local to `Ps2BootHost.cpp`. Add one shared exception logging helper for runtime phases, remove unconditional startup probe calls, and lock the behavior with one source-level regression test file.

**Tech Stack:** C++, xUnit, PS2 builder source tests

---

### Task 1: Lock the intended source behavior

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2BootHostSourceTests.cs`
- Test: `C:\dev\helworks\helengine-ps2\builder.tests\helengine.ps2.builder.tests.csproj`

- [ ] **Step 1: Write the failing source assertions**

Add assertions that check the boot host source no longer calls `BootLogDiscProbe("disc probe cube model"` from `InitializeRuntime()` and does contain one shared runtime exception logging helper entry point for startup-scene failures.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test C:\dev\helworks\helengine-ps2\builder.tests\helengine.ps2.builder.tests.csproj --filter FullyQualifiedName~Ps2BootHostSourceTests`
Expected: FAIL because the current boot host still emits disc probes and lacks the new helper usage.

- [ ] **Step 3: Commit the failing test state only if an isolated branch workflow is being used**

Do not commit a red state directly to `main`.

### Task 2: Centralize runtime exception logging and remove probe noise

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\src\platform\ps2\Ps2BootHost.cpp`
- Test: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2BootHostSourceTests.cs`

- [ ] **Step 1: Add a shared runtime exception logging helper**

Introduce a small helper near `BootLog(...)` that formats runtime exception messages by phase and appends scene-load diagnostics for startup-scene load failures when available.

- [ ] **Step 2: Replace startup-scene catch logging with the shared helper**

Route the `LoadPackagedStartupScene()` catch blocks through the helper instead of building ad-hoc strings inline.

- [ ] **Step 3: Remove unconditional startup disc-probe calls**

Delete the `BootLogDiscProbe(...)` calls from `InitializeRuntime()` after `cdvd ready` so they no longer pollute normal boot logs.

- [ ] **Step 4: Keep frame/update/draw/present runtime catches aligned**

Use the same helper for frame update, draw3d, draw2d, present, and outer frame exception cases where practical without changing halt behavior.

### Task 3: Verify the targeted behavior

**Files:**
- Test: `C:\dev\helworks\helengine-ps2\builder.tests\helengine.ps2.builder.tests.csproj`

- [ ] **Step 1: Run the focused PS2 boot-host source tests**

Run: `dotnet test C:\dev\helworks\helengine-ps2\builder.tests\helengine.ps2.builder.tests.csproj --filter FullyQualifiedName~Ps2BootHostSourceTests`
Expected: PASS

- [ ] **Step 2: Inspect git diff for unintended changes**

Run: `git -C C:\dev\helworks\helengine-ps2 diff -- builder.tests/Ps2BootHostSourceTests.cs src/platform/ps2/Ps2BootHost.cpp`
Expected: only the planned logging and probe cleanup changes appear.
