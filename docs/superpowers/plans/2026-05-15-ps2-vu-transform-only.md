# PS2 VU Transform Only Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move only opaque-untextured `cube_test` vertex transform from CPU to VU1 while preserving the currently working helper-packet and CPU flat-lighting baseline.

**Architecture:** Keep the validated helper-generated GIF packet and CPU flat color exactly as they are today. Change the CPU setup builder so it emits raw triangle positions and transform constants instead of final `XYZ2`, then update the VU microprogram to patch only the three `XYZ2` slots before `xgkick`.

**Tech Stack:** C++, PS2 VU assembly (`dvp-as`), packet2, gsKit, real editor-driven PS2 export, PCSX2.

---

### Task 1: Lock Down the Current CPU-Projected Baseline

**Files:**
- Modify: `builder.tests/Ps2NativeBuildExecutorTests.cs`
- Modify: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm`
- Test: `builder.tests/helengine.ps2.builder.tests.csproj`

- [ ] **Step 1: Add a failing test that asserts the current untextured VU program still contains the xgkick-only baseline before the transform work starts**

```csharp
[Fact]
public void Ps2OpaqueDraw3DProgram_ShouldStartAsKickOnlyBaseline() {
    string programPath = Path.Combine(
        TestContext.ResolveRepositoryRoot(),
        "src",
        "platform",
        "ps2",
        "rendering",
        "vu",
        "programs",
        "Ps2OpaqueDraw3D.vsm");

    string source = File.ReadAllText(programPath);

    Assert.Contains("xtop VI02", source);
    Assert.Contains("iaddiu VI03, VI02, 0x00000010", source);
    Assert.Contains("xgkick VI03", source);
}
```

- [ ] **Step 2: Run the focused test to verify the baseline is captured**

Run: `rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter FullyQualifiedName~Ps2OpaqueDraw3DProgram_ShouldStartAsKickOnlyBaseline --no-restore`

Expected: `PASS`

- [ ] **Step 3: If the test fails, restore the kick-only baseline before touching transform logic**

```asm
__ps2_opaque_draw_3d:
         NOP                                                        xtop VI02
         NOP                                                        iaddiu VI03, VI02, 0x00000010
         NOP                                                        xgkick VI03
         NOP[E]                                                     NOP
         NOP                                                        NOP
```

- [ ] **Step 4: Re-run the focused baseline test**

Run: `rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter FullyQualifiedName~Ps2OpaqueDraw3DProgram_ShouldStartAsKickOnlyBaseline --no-restore`

Expected: `PASS`

- [ ] **Step 5: Commit the baseline lock**

```bash
git add builder.tests/Ps2NativeBuildExecutorTests.cs src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm
git commit -m "test: lock PS2 opaque VU kick-only baseline"
```

### Task 2: Remove CPU XYZ2 Generation From the Opaque-Untextured Setup Path

**Files:**
- Modify: `src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.cpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Test: `tools/ps2-host-debugger` output via `draw-once`

- [ ] **Step 1: Add a failing host-visible invariant that the setup builder no longer requires precomputed position registers for untextured triangles**

```cpp
struct alignas(16) Ps2VuOpaqueUntexturedTriangleSetup final {
    float WorldMatrix[16];
    float ViewMatrix[16];
    float ProjectionMatrix[16];
    float Viewport[4];
    Ps2VuOpaqueSourceTriangle SourceTriangle;
    float FaceNormal[4];
    float LightDirection[4];
    float LightConstants[4];
    float WorldViewProjectionMatrix[16];
    float GsScale[4];
    float GsOffset[4];
};
```

- [ ] **Step 2: Remove CPU projection/cull helpers from `Ps2VuOpaqueUntexturedSetupBuilder.cpp` for the active untextured path**

Delete or stop calling:

```cpp
static bool TryBuildVertexPositionRegister(...);
static bool IsFrontFacingTriangle(...);
```

and stop filling:

```cpp
triangleSetup.PositionRegisterA = positionRegisterA;
triangleSetup.PositionRegisterB = positionRegisterB;
triangleSetup.PositionRegisterC = positionRegisterC;
```

- [ ] **Step 3: Keep the CPU work limited to raw payload preparation**

The active build loop should retain:

```cpp
triangleSetup.SourceTriangle.PositionA[0] = packedPositionA.X;
triangleSetup.SourceTriangle.PositionA[1] = packedPositionA.Y;
triangleSetup.SourceTriangle.PositionA[2] = packedPositionA.Z;
triangleSetup.SourceTriangle.PositionA[3] = 1.0f;

CopyMatrix(world, triangleSetup.WorldMatrix);
CopyMatrix(view, triangleSetup.ViewMatrix);
CopyMatrix(projection, triangleSetup.ProjectionMatrix);
CopyMatrix(worldViewProjectionMatrix, triangleSetup.WorldViewProjectionMatrix);
triangleSetup.GsScale[0] = viewport.Z * 8.0f;
triangleSetup.GsOffset[0] = (2048.0f + viewport.X + (viewport.Z * 0.5f)) * 16.0f;
```

but must no longer derive final `GS_SETREG_XYZ2(...)` values on CPU.

- [ ] **Step 4: Update the packet builder payload copy to match the slimmer setup structure**

In `Ps2VuVifPacketBuilder.cpp`, the setup-to-payload copy should become:

```cpp
std::memcpy(payload.WorldMatrix, triangleSetup.WorldMatrix, sizeof(triangleSetup.WorldMatrix));
std::memcpy(payload.ViewMatrix, triangleSetup.ViewMatrix, sizeof(triangleSetup.ViewMatrix));
std::memcpy(payload.ProjectionMatrix, triangleSetup.ProjectionMatrix, sizeof(triangleSetup.ProjectionMatrix));
std::memcpy(payload.Viewport, triangleSetup.Viewport, sizeof(triangleSetup.Viewport));
std::memcpy(&payload.SourceTriangle, &triangleSetup.SourceTriangle, sizeof(triangleSetup.SourceTriangle));
std::memcpy(payload.WorldViewProjectionMatrix, triangleSetup.WorldViewProjectionMatrix, sizeof(triangleSetup.WorldViewProjectionMatrix));
std::memcpy(payload.GsScale, triangleSetup.GsScale, sizeof(triangleSetup.GsScale));
std::memcpy(payload.GsOffset, triangleSetup.GsOffset, sizeof(triangleSetup.GsOffset));
```

- [ ] **Step 5: Rebuild the builder to catch payload/offset drift immediately**

Run: `rtk dotnet build builder\helengine.ps2.builder.csproj --no-restore`

Expected: `Build succeeded.`

- [ ] **Step 6: Run the host debugger in `draw-once` mode to confirm the untextured path still classifies one opaque dynamic batch without requiring CPU `XYZ2`**

Run: `rtk powershell -NoProfile -Command ".\\tools\\ps2-host-debugger\\bin\\ps2-host-debugger.exe --export-root C:\\dev\\helprojs\\output\\ps2-vu-colored-baseline --mode draw-once"`

Expected output contains:

```text
proxies=1
opaqueDynamic=1
vuBatches=1
```

- [ ] **Step 7: Commit the CPU-side transform removal**

```bash
git add src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.hpp src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.cpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp
git commit -m "refactor: move opaque untextured XYZ2 generation off CPU"
```

### Task 3: Restore VU XYZ2 Generation In The Microprogram

**Files:**
- Modify: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm`
- Modify: `Makefile`
- Test: real PS2 export via `helengine.editor.app`

- [ ] **Step 1: Write the transform-only VU program with explicit scope comments before changing behavior**

```asm
; Opaque untextured VU1 microprogram.
; Scope for this step:
; - read three source positions
; - transform them
; - convert to XYZ2
; - patch the helper-generated packet
; - xgkick
; Non-goals:
; - no VU lighting
; - no VU culling
; - no packet layout changes
```

- [ ] **Step 2: Load the current payload using the existing `xtop`/packet base pattern and compute the packet write address exactly as today**

```asm
         NOP                                                        xtop VI02
         NOP                                                        iaddiu VI03, VI02, 0x00000010
```

Keep this packet-base contract unchanged.

- [ ] **Step 3: Replace the xgkick-only body with transform-to-XYZ2 writes for the three helper packet slots**

The VU implementation must:

```asm
; load WorldViewProjectionMatrix rows
; load SourceTriangle.PositionA / B / C
; multiply each vertex by WVP
; perform perspective divide
; apply GS scale/offset
; store XYZ2 results into the three known packet slots
; xgkick the packet base
```

The packet slots must remain the same helper-generated `XYZ2` qwords that already rendered the CPU-projected cube.

- [ ] **Step 4: Rebuild the PS2 builder to force `dvp-as` validation**

Run: `rtk dotnet build builder\helengine.ps2.builder.csproj --no-restore`

Expected: `Build succeeded.`

If it fails, stop and fix only assembler syntax. Do not change packet shape or lighting in the same pass.

- [ ] **Step 5: Run the real editor-driven PS2 export for `cube_test`**

Run: `rtk dotnet run --project C:\dev\helworks\helengine\helengine.ui\helengine.editor.app\helengine.editor.app.csproj --no-restore -- --project C:\dev\helprojs\city\project.heproj --build ps2 --output C:\dev\helprojs\output\ps2-vu-colored-baseline`

Expected output contains:

```text
[helengine-ps2] native build completed
[helengine-ps2] iso packaged
Build completed for platform 'ps2'
```

- [ ] **Step 6: Launch PCSX2 only after confirming the fresh ISO timestamp**

Run:

```powershell
rtk powershell -NoProfile -Command "Get-Item C:\dev\helprojs\output\ps2-vu-colored-baseline\game.iso | Select-Object FullName, LastWriteTime, Length | Format-List"
rtk powershell -NoProfile -Command "Start-Process -FilePath 'C:\Program Files\PCSX2\pcsx2-qt.exe' -ArgumentList 'C:\dev\helprojs\output\ps2-vu-colored-baseline\game.iso' -WorkingDirectory 'C:\dev\helprojs\output\ps2-vu-colored-baseline'"
```

Expected: a spinning cube still appears in `cube_test`.

- [ ] **Step 7: Verify the visual success criteria and stop if any regression appears**

Expected visual result:

```text
- cube still visible
- helper packet remains stable
- no giant square / giant triangle corruption
- no FIFO/VIF asserts
- flat CPU lighting still present
```

If any of those fail, revert only the VU program body to the xgkick-only baseline and re-export before trying a new transform hypothesis.

- [ ] **Step 8: Commit the transform-only VU step**

```bash
git add src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm Makefile
git commit -m "feat: move opaque untextured transform into VU"
```

### Task 4: Final Verification And Regression Notes

**Files:**
- Modify: `docs/superpowers/specs/2026-05-15-ps2-vu-transform-only-design.md` (only if the implemented behavior differs)
- Test: `builder/helengine.ps2.builder.csproj`
- Test: `builder.tests/helengine.ps2.builder.tests.csproj`

- [ ] **Step 1: Re-run focused builder tests**

Run: `rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter FullyQualifiedName~Ps2NativeBuildExecutorTests --no-restore`

Expected: `PASS`

- [ ] **Step 2: Re-run the builder project build**

Run: `rtk dotnet build builder\helengine.ps2.builder.csproj --no-restore`

Expected: `Build succeeded.`

- [ ] **Step 3: Re-run the real PS2 export one more time from the editor path**

Run: `rtk dotnet run --project C:\dev\helworks\helengine\helengine.ui\helengine.editor.app\helengine.editor.app.csproj --no-restore -- --project C:\dev\helprojs\city\project.heproj --build ps2 --output C:\dev\helprojs\output\ps2-vu-colored-baseline`

Expected:

```text
[helengine-ps2] packaged outputs verified
Build completed for platform 'ps2'
```

- [ ] **Step 4: If the implementation changed the approved scope, update the design doc immediately**

```markdown
Update only if the landed behavior differs from:
- helper packet unchanged
- CPU flat color unchanged
- transform only moved to VU
```

- [ ] **Step 5: Commit any doc correction or verification-only follow-up**

```bash
git add docs/superpowers/specs/2026-05-15-ps2-vu-transform-only-design.md
git commit -m "docs: align PS2 VU transform-only spec with implementation"
```
