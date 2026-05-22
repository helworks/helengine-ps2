# PS2 Cube Test VU Transform Pass Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore VU-side `XYZ2` generation for the `cube_test` untextured opaque path without introducing VU culling, VU lighting, or packet-layout changes.

**Architecture:** Keep the current VIF/VU packet submission path and CPU-authored flat-color packet template intact, but remove clip/cull work from `Ps2OpaqueDraw3D.vsm` so VU1 only transforms vertices and overwrites the three existing `XYZ2` slots. Preserve the untextured setup-builder and packet-builder data flow, then rename the untextured payload contract in `Ps2VuVifPacketBuilder.cpp` so the code clearly describes a transform-only path instead of a lit/cull-oriented one.

**Tech Stack:** C#, xUnit, PS2 C++, VU assembly (`.vsm`), RTK, PCSX2

---

### Task 1: Remove VU Clip/Cull Behavior From The Transform-Only Microprogram

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildExecutorTests.cs`
- Modify: `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\programs\Ps2OpaqueDraw3D.vsm`
- Test: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildExecutorTests.cs`

- [ ] **Step 1: Write the failing source tests**

Replace the current positive clip/cull assertion with two transform-only guardrail tests:

```csharp
/// <summary>
/// Verifies that the transform-only opaque VU path does not perform clip or cull rejection in the microprogram.
/// </summary>
[Fact]
public void Ps2OpaqueDraw3DProgram_WhenUsingTransformOnlyPacketPath_ShouldNotUseVuClipCullInstructions() {
    string repositoryRootPath = ResolveRepositoryRoot();
    string programPath = Path.Combine(
        repositoryRootPath,
        "src",
        "platform",
        "ps2",
        "rendering",
        "vu",
        "programs",
        "Ps2OpaqueDraw3D.vsm");

    string source = File.ReadAllText(programPath);

    Assert.DoesNotContain("clipw.xyz", source, StringComparison.Ordinal);
    Assert.DoesNotContain("fcand", source, StringComparison.Ordinal);
}

/// <summary>
/// Verifies that the transform-only opaque VU path still writes one neutral ADC word into each XYZ2 slot before storing XYZ data.
/// </summary>
[Fact]
public void Ps2OpaqueDraw3DProgram_WhenUsingTransformOnlyPacketPath_ShouldWriteNeutralAdcWordsIntoXyz2Slots() {
    string repositoryRootPath = ResolveRepositoryRoot();
    string programPath = Path.Combine(
        repositoryRootPath,
        "src",
        "platform",
        "ps2",
        "rendering",
        "vu",
        "programs",
        "Ps2OpaqueDraw3D.vsm");

    string source = File.ReadAllText(programPath);

    Assert.Contains("iaddiu VI04, VI00, 0x00007FFF", source, StringComparison.Ordinal);
    Assert.Contains("isw.w VI04, 22(VI02)", source, StringComparison.Ordinal);
    Assert.Contains("isw.w VI04, 24(VI02)", source, StringComparison.Ordinal);
    Assert.Contains("isw.w VI04, 26(VI02)", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the focused tests to verify they fail**

Run:

```bash
rtk dotnet test C:\dev\helworks\helengine-ps2\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2OpaqueDraw3DProgram_WhenUsingTransformOnlyPacketPath_ShouldNotUseVuClipCullInstructions|FullyQualifiedName~Ps2OpaqueDraw3DProgram_WhenUsingTransformOnlyPacketPath_ShouldWriteNeutralAdcWordsIntoXyz2Slots" -v minimal
```

Expected: FAIL because `Ps2OpaqueDraw3D.vsm` still contains `clipw.xyz` / `fcand` and does not yet initialize `VI04` from `VI00` as a neutral ADC source.

- [ ] **Step 3: Write the minimal microprogram change**

Update the microprogram comment block and replace the clip/cull sequence with neutral ADC writes while keeping the existing transform, `sq.xyz`, winding order, and `xgkick` tail intact.

Use this shape:

```asm
; Scope for this step:
; - read three source positions
; - transform them with the precomputed WVP matrix
; - convert them to GS XYZ2 values
; - patch the existing VU-owned packet
; - xgkick
; Non-goals:
; - no VU clip rejection
; - no VU front-face culling
; - no VU lighting
; - no packet layout changes

         NOP                                                        iaddiu VI04, VI00, 0x00007FFF
         NOP                                                        isw.w VI04, 22(VI02)
         mulax         ACC,VF04,VF02x                               NOP
         madday        ACC,VF05,VF02y                               NOP
         maddaz        ACC,VF06,VF02z                               NOP
         maddw         VF02,VF07,VF02w                              NOP
         NOP                                                        isw.w VI04, 26(VI02)
         mulax         ACC,VF04,VF03x                               NOP
         madday        ACC,VF05,VF03y                               NOP
         maddaz        ACC,VF06,VF03z                               NOP
         maddw         VF03,VF07,VF03w                              NOP
         NOP                                                        isw.w VI04, 24(VI02)
```

The important constraint is: remove `clipw.xyz` / `fcand` entirely, but keep the three `isw.w` stores to the same packet slots and keep the later `sq.xyz VF01, 22(VI02)`, `sq.xyz VF03, 24(VI02)`, and `sq.xyz VF02, 26(VI02)` stores unchanged.

- [ ] **Step 4: Run the focused tests to verify they pass**

Run:

```bash
rtk dotnet test C:\dev\helworks\helengine-ps2\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2OpaqueDraw3DProgram_WhenUsingTransformOnlyPacketPath_ShouldNotUseVuClipCullInstructions|FullyQualifiedName~Ps2OpaqueDraw3DProgram_WhenUsingTransformOnlyPacketPath_ShouldWriteNeutralAdcWordsIntoXyz2Slots|FullyQualifiedName~Ps2OpaqueDraw3DProgram_WhenUsingTransformOnlyPacketPath_ShouldSwapSecondAndThirdVertexStores" -v minimal
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
rtk git -C C:\dev\helworks\helengine-ps2 add builder.tests/Ps2NativeBuildExecutorTests.cs src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm
rtk git -C C:\dev\helworks\helengine-ps2 commit -m "Remove VU clip-cull from cube_test transform pass"
```

### Task 2: Rename The Untextured VU Payload Contract To Match The Transform-Only Design

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildInputsTests.cs`
- Modify: `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\Ps2VuOpaqueUntexturedSetupBuilder.hpp`
- Modify: `C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.cpp`
- Test: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildInputsTests.cs`

- [ ] **Step 1: Write the failing source-contract test**

Add one focused test that forces the untextured packet-builder path to use a transform-only payload name instead of the current lit-oriented type name:

```csharp
/// <summary>
/// Ensures the untextured opaque VU path uses a transform-only payload contract name while keeping raw source positions and transform constants.
/// </summary>
[Fact]
public void Ps2_vu_vif_packet_builder_uses_transform_only_untextured_payload_contract() {
    string builderSource = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.cpp");
    string setupHeader = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\Ps2VuOpaqueUntexturedSetupBuilder.hpp");

    Assert.Contains("struct alignas(16) Ps2VuOpaqueUntexturedTrianglePayload final", builderSource, StringComparison.Ordinal);
    Assert.Contains("std::vector<Ps2VuOpaqueUntexturedTrianglePayload> trianglePayloads;", builderSource, StringComparison.Ordinal);
    Assert.Contains("Ps2VuOpaqueUntexturedTrianglePayload payload {}", builderSource, StringComparison.Ordinal);
    Assert.Contains("std::memcpy(packet.get()->next, &trianglePayload, sizeof(Ps2VuOpaqueUntexturedTrianglePayload));", builderSource, StringComparison.Ordinal);
    Assert.Contains("Ps2VuOpaqueSourceTriangle SourceTriangle;", setupHeader, StringComparison.Ordinal);
    Assert.Contains("float WorldViewProjectionMatrix[16];", setupHeader, StringComparison.Ordinal);
    Assert.Contains("float GsScale[4];", setupHeader, StringComparison.Ordinal);
    Assert.Contains("float GsOffset[4];", setupHeader, StringComparison.Ordinal);
    Assert.DoesNotContain("std::vector<Ps2VuLitTrianglePayload> trianglePayloads;", builderSource, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the focused test to verify it fails**

Run:

```bash
rtk dotnet test C:\dev\helworks\helengine-ps2\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2_vu_vif_packet_builder_uses_transform_only_untextured_payload_contract" -v minimal
```

Expected: FAIL because `Ps2VuVifPacketBuilder.cpp` still uses `Ps2VuLitTrianglePayload` in the untextured branch.

- [ ] **Step 3: Write the minimal contract-alignment implementation**

Do not redesign the payload layout in this task. Keep the binary layout stable and rename the untextured payload type plus helper signatures so the code matches the transform-only design.

Apply this shape in `Ps2VuVifPacketBuilder.cpp`:

```cpp
struct alignas(16) Ps2VuOpaqueUntexturedTrianglePayload final {
    Ps2VuGifQword LightingPalette[LightingPaletteEntryCount];
    std::uint8_t GifPacketTemplate[TriangleGifPacketTemplateByteCount];
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

void PopulateTriangleGifPacketTemplate(
    const Ps2VuOpaqueBatch& batch,
    const Ps2VuFlatColor& resolvedFlatColor,
    GSGLOBAL* gsGlobal,
    Ps2VuOpaqueUntexturedTrianglePayload& payload);

void PopulateTrianglePayloadFromSetup(
    const Ps2VuOpaqueBatch& batch,
    const Ps2VuOpaqueUntexturedTriangleSetup& triangleSetup,
    GSGLOBAL* gsGlobal,
    Ps2VuOpaqueUntexturedTrianglePayload& payload);

std::vector<Ps2VuOpaqueUntexturedTrianglePayload> trianglePayloads;
trianglePayloads.reserve(triangleSetups.size());
for (const Ps2VuOpaqueUntexturedTriangleSetup& triangleSetup : triangleSetups) {
    Ps2VuOpaqueUntexturedTrianglePayload payload {};
    PopulateTrianglePayloadFromSetup(batch, triangleSetup, gsGlobal, payload);
    trianglePayloads.push_back(payload);
}

for (const Ps2VuOpaqueUntexturedTrianglePayload& trianglePayload : trianglePayloads) {
    packet2_utils_vu_open_unpack(packet.get(), XtopGifPacketAddress, 1);
    std::memcpy(packet.get()->next, &trianglePayload, sizeof(Ps2VuOpaqueUntexturedTrianglePayload));
    packet2_advance_next(packet.get(), sizeof(Ps2VuOpaqueUntexturedTrianglePayload));
    packet2_utils_vu_close_unpack(packet.get());
```

Also tighten the setup-builder header comment so it describes a transform-only contract:

```cpp
struct alignas(16) Ps2VuOpaqueUntexturedTriangleSetup final {
    // Raw source triangle plus CPU-authored transform constants for the transform-only untextured VU XYZ2 path.
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

This task is a naming and contract-alignment pass only. It must not change the actual qword layout consumed by `Ps2OpaqueDraw3D.vsm`.

- [ ] **Step 4: Run the focused tests to verify they pass**

Run:

```bash
rtk dotnet test C:\dev\helworks\helengine-ps2\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2_vu_vif_packet_builder_uses_transform_only_untextured_payload_contract|FullyQualifiedName~Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedPath_ShouldNotUseCpuTriangleRejectionMarkers|FullyQualifiedName~Ps2_renderer3d_dispatches_assembled_vu_packets_over_vif1" -v minimal
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
rtk git -C C:\dev\helworks\helengine-ps2 add builder.tests/Ps2NativeBuildInputsTests.cs src/platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.hpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp
rtk git -C C:\dev\helworks\helengine-ps2 commit -m "Align untextured VU payload with transform-only pass"
```

### Task 3: Rebuild, Export, Launch, And Verify The Cube Test Runtime Path

**Files:**
- Verify only: `C:\dev\helworks\helengine\helengine.ui\helengine.editor.app\helengine.editor.app.csproj`
- Verify only: `C:\dev\helprojs\output\ps2-vu-colored-baseline\game.iso`
- Verify only: `C:\Users\Helena\Documents\PCSX2\logs\emulog.txt`

- [ ] **Step 1: Run the combined PS2 builder test slice**

Run:

```bash
rtk dotnet test C:\dev\helworks\helengine-ps2\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2NativeBuildExecutorTests|FullyQualifiedName~Ps2NativeBuildInputsTests" -v minimal
```

Expected: PASS.

- [ ] **Step 2: Rebuild the editor app used for PS2 export**

Run:

```bash
rtk dotnet build C:\dev\helworks\helengine\helengine.ui\helengine.editor.app\helengine.editor.app.csproj -v minimal
```

Expected: PASS.

If this fails with `MSB3021` / `MSB3027` because `helengine.editor.app\bin\Debug\net9.0-windows\*.dll` is locked by `.NET Host`, stop the specific locking PID and rerun the same build command before continuing.

- [ ] **Step 3: Export the fresh PS2 build**

Run:

```bash
rtk proxy powershell.exe -NoProfile -Command "& 'C:\dev\helworks\helengine\helengine.ui\helengine.editor.app\bin\Debug\net9.0-windows\helengine.editor.app.exe' --project 'C:\dev\helprojs\city\project.heproj' --build ps2 --output 'C:\dev\helprojs\output\ps2-vu-colored-baseline' 2>&1 | Tee-Object -FilePath 'C:\tmp\ps2-vu-colored-baseline-build.log' | Select-Object -Last 120"
```

Expected output contains:

```text
[helengine-ps2] native build completed
[helengine-ps2] iso packaged
[helengine-ps2] packaged outputs verified
Build completed for platform 'ps2': C:\dev\helprojs\output\ps2-vu-colored-baseline
```

- [ ] **Step 4: Relaunch PCSX2 against the fresh ISO**

Run:

```bash
rtk proxy powershell.exe -NoProfile -Command "Get-Process | Where-Object { $_.ProcessName -like 'pcsx2*' } | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Process -FilePath 'C:\Program Files\PCSX2\pcsx2-qt.exe' -ArgumentList 'C:\dev\helprojs\output\ps2-vu-colored-baseline\game.iso' -WorkingDirectory 'C:\dev\helprojs\output\ps2-vu-colored-baseline'; Start-Sleep -Seconds 4; tasklist /FI 'IMAGENAME eq pcsx2-qt.exe'"
```

Expected output contains:

```text
pcsx2-qt.exe
```

- [ ] **Step 5: Verify the runtime boot and visual success criteria**

Check the emulator log:

```bash
rtk proxy powershell.exe -NoProfile -Command "Get-Content 'C:\Users\Helena\Documents\PCSX2\logs\emulog.txt' -Tail 80 | Out-String -Width 220"
```

Expected log lines contain:

```text
isoFile open ok: C:\dev\helprojs\output\ps2-vu-colored-baseline\game.iso
ELF Loading: cdrom0:\HELENGIN.ELF;1
ELF cdrom0:\HELENGIN.ELF;1 with entry point at
```

Then verify in the PCSX2 window:

- `cube_test` still renders a spinning cube
- the cube stays visibly stable in size and position relative to the current baseline
- the FPS overlay remains visible
- no black 3D scene with intact UI
- no giant triangle or stretched quad corruption

If any of those visual checks fail, stop and debug the transform math or the `XYZ2` packet-slot writes before adding any cull or lighting work.
