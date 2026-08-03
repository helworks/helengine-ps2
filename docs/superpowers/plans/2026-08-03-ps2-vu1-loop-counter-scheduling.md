# PS2 VU1 Textured Loop Counter Scheduling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent the active textured VU1 programs from executing one source triangle beyond the submitted count without adding a per-triangle instruction.

**Architecture:** Preserve the existing counter loop and packet formats. Schedule the independent `VI05` source-pointer increment between the `VI01` decrement and its dependent branch in both active textured programs, then prove the output count and camera traversal on PS2 hardware emulation.

**Tech Stack:** PS2 VU1 assembly, C# xUnit source contracts, deterministic build-waiter, PCSX2, HelenUI OCR.

## Global Constraints

- Work directly on the existing `main` checkout; do not create a worktree.
- Do not change CPU clipping, route classification, culling, packet layout, GIF state, or GS behavior.
- Do not add a VU1 instruction or increase the loop length.
- Keep DemoDisc build configuration restored to SHA-256 `99993AE330D58FACF3819820BC761098C0D8C8705399BA1A5B52B6E8C51B85E7` outside isolated packaging.
- Keep build artifacts under `C:\dev\helworks\builds`; do not use `%TEMP%`.
- Use HelenUI OCR for screen telemetry; do not visually inspect screenshots.

---

### Task 1: Schedule the active textured VU1 loop counters

**Files:**
- Modify: `builder.tests/Ps2VuHybridClippedBatchSourceTests.cs`
- Modify: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm`
- Modify: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedPretransformedDraw3D.vsm`

**Interfaces:**
- Consumes: Existing `VI01` remaining-triangle counter and `VI05` seven-qword source-record pointer.
- Produces: The exact loop-tail order `isubiu VI01`, `iaddiu VI05`, `ibne VI01` in both active textured VU1 programs.

- [x] **Step 1: Write the failing source contract**

Add this xUnit test to `Ps2VuHybridClippedBatchSourceTests`:

```csharp
/// <summary>
/// Requires both active textured loops to separate the remaining-count write from its dependent branch with useful pointer work.
/// </summary>
[Fact]
public void Ps2TexturedVuPrograms_ScheduleTheLoopCounterBeforeItsDependentBranch() {
    string repositoryRootPath = GetRepositoryRootPath();
    string fastProgramSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedDraw3D.vsm"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);
    string clippedProgramSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedPretransformedDraw3D.vsm"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);
    string expectedFastLoopTail = string.Join("\n",
        "         NOP                                                        isubiu VI01, VI01, 0x00000001",
        "         NOP                                                        iaddiu VI05, VI05, 0x00000007",
        "         NOP                                                        ibne VI01, VI00, texturedTriangleLoop");
    string expectedClippedLoopTail = string.Join("\n",
        "         NOP                                                        isubiu VI01, VI01, 0x00000001",
        "         NOP                                                        iaddiu VI05, VI05, 0x00000007",
        "         NOP                                                        ibne VI01, VI00, texturedPretransformedTriangleLoop");

    Assert.Contains(expectedFastLoopTail, fastProgramSource, StringComparison.Ordinal);
    Assert.Contains(expectedClippedLoopTail, clippedProgramSource, StringComparison.Ordinal);
}
```

- [x] **Step 2: Run the new contract and verify RED**

Run:

```powershell
dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2TexturedVuPrograms_ScheduleTheLoopCounterBeforeItsDependentBranch"
```

Expected: FAIL because both current programs place `iaddiu VI05` before `isubiu VI01`, leaving the branch immediately dependent on the decrement.

- [x] **Step 3: Apply the zero-cost instruction reorder**

Change `Ps2OpaqueTexturedDraw3D.vsm` from:

```text
         NOP                                                        iaddiu VI05, VI05, 0x00000007
         NOP                                                        isubiu VI01, VI01, 0x00000001
         NOP                                                        ibne VI01, VI00, texturedTriangleLoop
```

to:

```text
         NOP                                                        isubiu VI01, VI01, 0x00000001
         NOP                                                        iaddiu VI05, VI05, 0x00000007
         NOP                                                        ibne VI01, VI00, texturedTriangleLoop
```

Apply the same reorder in `Ps2OpaqueTexturedPretransformedDraw3D.vsm`, retaining its branch target `texturedPretransformedTriangleLoop`.

- [x] **Step 4: Run focused contracts and verify GREEN**

Run:

```powershell
dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2VuHybridClippedBatchSourceTests|FullyQualifiedName~Ps2VuOutputReadbackSourceTests"
```

Expected: all selected tests pass.

### Task 2: Package and validate B332

**Files:**
- Temporarily modify and restore: `C:/dev/helprojs/demodisc/user_settings/build_config.json`
- Create through build tools: `C:/dev/helworks/builds/helengine-ps2/ps2/B332-vu-loop-counter-scheduling/game.iso`
- Update: `C:/dev/helworks/builds/helengine-ps2/ps2/B332-vu-loop-counter-scheduling/validation.md`
- Update: `.superpowers/sdd/2026-08-02-ps2-colored-face-clipping-probe/progress.md`

**Interfaces:**
- Consumes: Task 1 microprogram scheduling and isolated scene `test_scene_tilt_trial_level_01_render`.
- Produces: A fresh B332 ISO plus synchronized HelenUI evidence that two source triangles emit two VU1 triangles.

- [x] **Step 1: Set B332 markers and isolate the probe scene**

Update the diagnostic marker from `B331` to `B332` and its focused source contracts. Temporarily set only the PS2 platform's `selectedSceneIds` and `sceneOrders` to `test_scene_tilt_trial_level_01_render`, preserving every other platform and PS2 option.

- [x] **Step 2: Build through the deterministic waiter**

Run:

```powershell
dotnet run --project C:\dev\helworks\helengine\tools\build-waiter\helengine.buildwaiter.csproj -- --output C:\dev\helworks\builds\helengine-ps2\ps2\B332-vu-loop-counter-scheduling --require game.iso --require disc/SYSTEM.CNF --require disc/HELENGIN.ELF -- powershell -NoProfile -ExecutionPolicy Bypass -File C:\dev\helworks\helengine\scripts\build-platform.ps1 -Project C:\dev\helprojs\demodisc\project.heproj -Platform ps2 -Output C:\dev\helworks\builds\helengine-ps2\ps2\B332-vu-loop-counter-scheduling -Configuration Debug -BuildProfile ps2-default
```

Expected: native build, packaged output verification, platform build, and fresh/non-empty waiter checks complete.

- [x] **Step 3: Restore and verify DemoDisc configuration**

Restore the full PS2 scene list immediately after packaging, then run:

```powershell
(Get-FileHash C:\dev\helprojs\demodisc\user_settings\build_config.json -Algorithm SHA256).Hash
```

Expected: `99993AE330D58FACF3819820BC761098C0D8C8705399BA1A5B52B6E8C51B85E7`.

- [x] **Step 4: Launch the exact B332 artifact**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\launch_in_emulator.ps1 -ArtifactPath C:\dev\helworks\builds\helengine-ps2\ps2\B332-vu-loop-counter-scheduling\game.iso
```

Expected: the launcher replaces the prior PCSX2 instance and reports the exact artifact, process ID, and live window.

- [x] **Step 5: OCR the initial synchronized output**

Capture the PCSX2 window with HelenUI screenshot-cli and analyze it with recognition-cli using the live HWND context. Do not open or inspect the PNG.

Expected: `B332 F2 C0 G0 N2`; A and B form the face and no triangle C is declared.

- [x] **Step 6: Validate both artifact traversals**

Ask Helena to reproduce the outside-triangle traversal, OCR that exact frame, then reproduce the deep-inside traversal and OCR again.

Expected: both positions remain `N2`, no outside or deep-inside extra triangle appears, and draw performance does not regress.

- [x] **Step 7: Record evidence**

Record artifact hashes, focused test results, configuration restoration hash, initial OCR, traversal OCR, and Helena's visual result in B332 `validation.md` and the existing SDD ledger. Leave the implementation uncommitted until Helena accepts the visual result and requests a commit.
