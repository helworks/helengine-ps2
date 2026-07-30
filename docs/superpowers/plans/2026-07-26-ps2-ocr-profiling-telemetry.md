# PS2 OCR Profiling Telemetry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make HelenUI return the raw OCR lines it already obtains from PCSX2 and remove the Light UI from the PS2 profiling build.

**Architecture:** Navigator stores a compact, immutable OCR diagnostic snapshot per loaded profile scope after the normal recognition pass. A read-only session route projects those snapshots without triggering capture, navigation, or input. The Demo Disc Colored Cubes scene stops attaching its Light presentation component while preserving its authored directional light and the PS2 performance overlay.

**Tech Stack:** C#/.NET 9 Navigator service and xUnit tests; C++ PS2 host and existing source-inspection tests; deterministic PS2 build/PCSX2 launch scripts.

---

### Task 1: Define retained OCR telemetry contracts

**Files:**
- Create: `C:\dev\helenui\plugins\navigator-service\src\NavigatorService\Contracts\NavigatorOcrTelemetryModels.cs`
- Modify: `C:\dev\helenui\plugins\navigator-service\src\NavigatorService\Sessions\SessionProfileScope.cs`
- Test: `C:\dev\helenui\plugins\navigator-service\tests\NavigatorService.Tests\SessionProfileScopeTests.cs`

- [ ] **Step 1: Write failing scope telemetry tests**

Create tests that construct a `RecognitionResult` with ordered OCR lines and assert that a scope retains only scope id, recognition time, engine status, and text lines. Assert an unrecognized scope returns an empty telemetry snapshot.

- [ ] **Step 2: Run the focused test and confirm it fails**

Run: `dotnet test plugins/navigator-service/tests/NavigatorService.Tests/NavigatorService.Tests.csproj --filter FullyQualifiedName~SessionProfileScope`

Expected: failure because no OCR telemetry model or storage exists.

- [ ] **Step 3: Add immutable telemetry models and scope storage**

Add public API models that do not expose image paths or image bytes. Add one setter on `SessionProfileScope` to replace the prior OCR snapshot after a completed recognition pass.

- [ ] **Step 4: Re-run the focused test**

Expected: PASS.

### Task 2: Retain OCR results during normal recognition

**Files:**
- Modify: `C:\dev\helenui\plugins\navigator-service\src\NavigatorService\Recognition\SessionRecognitionService.cs`
- Test: `C:\dev\helenui\plugins\navigator-service\tests\NavigatorService.Tests\SessionRecognitionServiceTests.cs`

- [ ] **Step 1: Write a failing recognition-service test**

Arrange a completed `RecognitionTickResult` with diagnostic OCR lines. Assert `RecognizeAsync` preserves those lines on the matching scope and does not create any additional capture or analyzer pass.

- [ ] **Step 2: Run the focused test and confirm it fails**

Run: `dotnet test plugins/navigator-service/tests/NavigatorService.Tests/NavigatorService.Tests.csproj --filter FullyQualifiedName~SessionRecognitionService`

Expected: telemetry remains empty.

- [ ] **Step 3: Project the existing recognition diagnostics**

After `ApplyRecognitionResult`, project `RecognitionTickResult.Result.Diagnostics.OcrResults` into the scope telemetry snapshot. Preserve line order and timestamp; skip null/empty text safely.

- [ ] **Step 4: Re-run the focused test**

Expected: PASS.

### Task 3: Add the read-only OCR route

**Files:**
- Modify: `C:\dev\helenui\plugins\navigator-service\src\NavigatorService\Http\NavigatorRouteHandlers.cs`
- Modify: `C:\dev\helenui\plugins\navigator-service\src\NavigatorService\Application\NavigatorSessionRouteGroup.cs`
- Modify: `C:\dev\helenui\plugins\navigator-service\README.md`
- Test: `C:\dev\helenui\plugins\navigator-service\tests\NavigatorService.Tests\SessionQueryServiceTests.cs`

- [ ] **Step 1: Write failing route/query tests**

Assert `GET /sessions/{id}/ocr` returns saved scope telemetry, returns empty lines before recognition, and never calls capture, recognition, input, or navigation services.

- [ ] **Step 2: Run the focused test and confirm it fails**

Run: `dotnet test plugins/navigator-service/tests/NavigatorService.Tests/NavigatorService.Tests.csproj --filter FullyQualifiedName~SessionQueryService`

Expected: route is absent.

- [ ] **Step 3: Implement route registration and query projection**

Register only `GET /sessions/{id}/ocr`; query the session registry and serialize snapshots. Document the endpoint as read-only and explicitly state it does not capture a frame.

- [ ] **Step 4: Re-run the focused route tests**

Expected: PASS.

### Task 4: Remove Light presentation controls from Colored Cubes

**Files:**
- Modify: `C:\dev\helprojs\demodisc\assets\codebase\rendering.tools\ColoredCubeGridSceneFactory.cs`
- Modify: `C:\dev\helprojs\demodisc\assets\codebase\rendering.tools.tests\ColoredCubeGridSceneFactoryTests.cs`

- [ ] **Step 1: Write a failing source-level test**

Assert the Colored Cubes UI root does not attach `DemoDiscLightToggleComponent`, while its directional-light entity is still created.

- [ ] **Step 2: Run the focused test and confirm it fails**

Run: `dotnet test assets/codebase/rendering.tools.tests/rendering.tools.tests.csproj --filter FullyQualifiedName~ColoredCubeGridSceneFactory`

Expected: existing source still attaches the Light presentation component.

- [ ] **Step 3: Disable only the Light presentation UI**

Remove the `DemoDiscLightToggleComponent` attachment from the Colored Cubes UI root. Do not alter the directional-light entity, light calculation, renderer material state, or performance overlay rows.

- [ ] **Step 4: Re-run the focused source test**

Expected: PASS.

### Task 5: Verify live OCR telemetry and PS2 artifact

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\src\platform\ps2\Ps2BootHost.cpp` only if incrementing the hardcoded build identifier for the new artifact.

- [ ] **Step 1: Run focused Navigator and PS2 tests**

Run each focused test suite from Tasks 1-4.

- [ ] **Step 2: Build a fresh deterministic PS2 artifact**

Use the existing build waiter and workspace-owned `C:\dev\helworks\builds\demodisc\ps2\<build-id>` output. Confirm a fresh `game.iso` exists and that native compilation included `Ps2BootHost.cpp`.

- [ ] **Step 3: Launch only through the project PCSX2 launcher**

Run `scripts\launch_in_emulator.ps1` with the new artifact. Do not create a second PCSX2 instance.

- [ ] **Step 4: Verify with HelenUI OCR only**

Attach a read-only session, invoke one recognition pass, call `GET /sessions/{id}/ocr`, and verify the build identifier plus live FPS/Drw lines are present. Treat `FPS: N/A` as failure. Delete the session after capturing text.
