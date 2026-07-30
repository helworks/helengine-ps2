# PS2 VU Hardware Clip-Flag Diagnostic Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the unreliable manual VU safety classification in the diagnostic clipping route with Athena-style hardware clip flags so unsafe triangles are discarded before reciprocal-W projection.

**Architecture:** The established fast textured microprogram remains unchanged. Intersecting slices continue to use the dedicated clipping microprogram, where `CLIPW` and `FCAND` classify each transformed vertex; any unsafe vertex rejects the whole triangle, while a fully safe triangle uses the already-proven compact emitter.

**Tech Stack:** C++20, PS2SDK, VIF1/VU1 micro assembly, xUnit source-contract tests, PowerShell build scripts, PCSX2, HelenUI OCR.

## Global Constraints

- Work directly on the main worktree; do not create a Git worktree.
- Prefix every shell command with `rtk`.
- Use `apply_patch` for file edits.
- Store build artifacts under `C:\dev\helworks\builds`, never `%TEMP%`.
- Launch PCSX2 only through `scripts\launch_in_emulator.ps1`.
- Use HelenUI OCR for emulator telemetry; do not inspect its screenshots manually.
- Preserve the existing fast textured microprogram byte-for-byte.
- Reject unsafe triangles before any `DIV Q, VF00w, clipPositionW` instruction.
- The diagnostic accepts localized triangle popping but never vertex explosion.

---

### Task 1: Use hardware clip flags for triangle rejection

**Files:**
- Modify: `builder.tests/Ps2VuNearPlaneClippingSourceTests.cs`
- Modify: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedClipDraw3D.vsm`
- Modify: `src/platform/ps2/Ps2BootHost.cpp`

**Interfaces:**
- Consumes: transformed clip-space positions in `VF18`, `VF19`, and `VF20`; proven emitter label `texturedClipEmitTriangle`; triangle-loop tail label `texturedClipTriangleLoopTail`.
- Produces: diagnostic label `texturedClipHardwareClipFlagDiagnostics`, hardware rejection label `texturedClipHardwareRejectTriangle`, and overlay build identifier `B296`.

- [ ] **Step 1: Write the failing source-contract test**

Update `Ps2OpaqueTexturedClipDraw3D_WhenTriangleCrossesTheCameraFrustum_ClipsBeforePerspectiveDivision` to require the hardware path and remove the B295 diagnostic label:

```csharp
Assert.Contains("texturedClipHardwareClipFlagDiagnostics:", source, StringComparison.Ordinal);
Assert.Contains("texturedClipHardwareRejectTriangle:", source, StringComparison.Ordinal);
Assert.Contains("clipw.xyz", source, StringComparison.Ordinal);
Assert.Contains("fcand", source, StringComparison.Ordinal);
Assert.DoesNotContain("texturedClipEmitNearInsideTriangleDiagnostics:", source, StringComparison.Ordinal);
```

Update `Ps2NearPlaneDiagnostics_WhenDisplayed_ReportSliceRoutesWithoutPerTriangleTimers`:

```csharp
Assert.Contains("FrameTimingOverlayBuildNumber = \"B296\"", bootSource, StringComparison.Ordinal);
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
rtk.exe dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter FullyQualifiedName~Ps2VuNearPlaneClippingSourceTests --no-restore
```

Expected: FAIL because the hardware diagnostic labels and B296 identifier are absent.

- [ ] **Step 3: Implement hardware rejection before projection**

In `Ps2OpaqueTexturedClipDraw3D.vsm`, replace the B295 near-mask diagnostic branch with `texturedClipHardwareClipFlagDiagnostics`. For each of `VF18`, `VF19`, and `VF20`:

1. Execute `clipw.xyz vertex, vertexw`, wait for the clip-flag pipeline, and use `fcand` with `0x3f` to reject standard homogeneous X/Y/Z/W violations.
2. Copy the vertex to `VF24` and calculate `VF24.z = (2 * (vertex.z - epsilon)) - vertex.w` while retaining the original `w`.
3. Execute `clipw.xyz VF24, VF24w`, wait for the clip-flag pipeline, and use `fcand` with `0x20` to reject `clipZ < epsilon` through the hardware negative-Z flag.
4. Branch to `texturedClipHardwareRejectTriangle` on any nonzero result.

After all three vertices pass, call the proven emitter and then branch to the triangle-loop tail:

```text
         NOP                                                        bal VI15, texturedClipEmitTriangle
         NOP                                                        NOP
         NOP                                                        b texturedClipTriangleLoopTail
         NOP                                                        NOP
texturedClipHardwareRejectTriangle:
         NOP                                                        b texturedClipTriangleLoopTail
         NOP                                                        NOP
```

Keep the old polygon clipper unreachable during this diagnostic; do not remove it yet. Change `FrameTimingOverlayBuildNumber` to `B296`.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Step 2 command again.

Expected: PASS, 7 tests passed.

- [ ] **Step 5: Build the deterministic PS2 diagnostic**

Create `C:\dev\helworks\builds\demodisc\ps2\B296-hardware-clip-flags` and invoke the existing build waiter executable directly, requiring `game.iso`, `disc/SYSTEM.CNF`, and `disc/HELENGIN.ELF`.

Expected: the waiter exits only after all three required artifacts exist.

- [ ] **Step 6: Launch and verify telemetry**

Launch the exact B296 ISO with:

```powershell
rtk.exe proxy powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\launch_in_emulator.ps1 -ArtifactPath C:\dev\helworks\builds\demodisc\ps2\B296-hardware-clip-flags\game.iso
```

Capture only the PCSX2 window through HelenUI and run recognition with `C:\dev\helenui\pcsx2.json`.

Expected OCR: `B296`, `Fast 0`, `Clip 2`, `Tri 12`, `Bat 1`, and no `FPS: N/A`.

- [ ] **Step 7: Request visual verification**

Ask the user to move the camera through the cube.

Expected: triangles disappear individually as they become unsafe; no triangle stretches, flashes to infinity, or explodes.

- [ ] **Step 8: Commit only after visual success**

After successful visual verification, stage only the clipping diagnostic files and commit:

```powershell
rtk git add -- builder.tests/Ps2VuNearPlaneClippingSourceTests.cs src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedClipDraw3D.vsm src/platform/ps2/Ps2BootHost.cpp
rtk git diff --cached --check
rtk git commit -m "fix(ps2): reject unsafe VU triangles with clip flags"
```
