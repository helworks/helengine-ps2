# PS2 VU1 Flat Diffuse Lighting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the recovered PS2 packed `VU1` opaque renderer so the spinning cube uses `VU1`-side flat directional diffuse lighting instead of a constant CPU-selected flat color.

**Architecture:** Keep the known-good CPU-side transform, clipping, backface culling, and packed mesh decoding path. Extend the opaque VU packet payload to carry per-triangle `XYZ2`, one flat face normal, one base color, and one directional light vector, then update the `VU1` microprogram to compute a flat diffuse intensity and kick `RGBAQ + XYZ2` to the `GS`.

**Tech Stack:** C# xUnit source-contract tests, C++20 runtime code, PS2SDK VU1 assembly, gsKit, dmaKit, `rtk`, PCSX2, ScreenshotCli.

---

## File Structure

### Existing files to modify

- Modify: `/mnt/c/dev/helworks/helengine-ps2/builder.tests/Ps2NativeBuildInputsTests.cs`
  - add source-contract tests for the lit VU packet builder, renderer light handoff, and `VU1` microprogram payload contract
- Modify: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/Ps2RenderManager3D.hpp`
  - extend the opaque VU render path to pass a directional light vector into the packet builder
- Modify: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
  - resolve the scene directional light for the VU path and preserve the current diagnostics/watch window
- Modify: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`
  - extend the packet-builder contract for light-direction input and expose helper-level state needed by diagnostics if necessary
- Modify: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
  - read packed normals, build the six-qword lit triangle payload, and emit the lit GIF packet shape
- Modify: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm`
  - compute flat diffuse intensity on `VU1` and kick one flat-shaded lit triangle at a time

### Existing files to reference during implementation

- Reference: `/mnt/c/dev/helworks/helengine-ps2/docs/superpowers/specs/2026-05-10-ps2-vu1-flat-diffuse-lighting-design.md`
- Reference: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/vu/Ps2VuPackedModel.hpp`
- Reference: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/Ps2RuntimeMaterial.hpp`
- Reference: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/Ps2BootHost.cpp`

---

### Task 1: Lock the lit VU contract with source-level regression tests

**Files:**
- Modify: `/mnt/c/dev/helworks/helengine-ps2/builder.tests/Ps2NativeBuildInputsTests.cs`
- Test: `/mnt/c/dev/helworks/helengine-ps2/builder.tests/Ps2NativeBuildInputsTests.cs`

- [ ] **Step 1: Write the failing source test for directional-light handoff into the VU packet builder**

```csharp
/// <summary>
/// Ensures the real VU opaque path resolves one directional light vector and passes it into the VU packet builder.
/// </summary>
[Fact]
public void Ps2_renderer3d_passes_directional_light_into_vu_opaque_packet_builder() {
    string header = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\Ps2RenderManager3D.hpp");
    string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

    Assert.Contains("void RenderOpaqueWithVuPath(const Ps2FramePlan& plan, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance);", header, StringComparison.Ordinal);
    Assert.Contains("::float3 lightDirection = DefaultForward;", source, StringComparison.Ordinal);
    Assert.Contains("TryResolveDirectionalLightDirection(lightDirection);", source, StringComparison.Ordinal);
    Assert.Contains("VuVifPacketBuilder.AddOpaqueBatch(batch, worldMatrix, view, projection, viewport, nearPlaneDistance, lightDirection, GsGlobal);", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Write the failing source test for the lit triangle payload and GIF packet shape**

```csharp
/// <summary>
/// Ensures the VU packet builder reads packed normals and emits a lit flat triangle payload for the VU program.
/// </summary>
[Fact]
public void Ps2_vu_vif_packet_builder_packs_normals_light_direction_and_rgbaq_for_flat_diffuse_lighting() {
    string header = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.hpp");
    string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.cpp");

    Assert.Contains("void AddOpaqueBatch(const Ps2VuOpaqueBatch& batch, const ::float4x4& world, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance, const ::float3& lightDirection, GSGLOBAL* gsGlobal);", header, StringComparison.Ordinal);
    Assert.Contains("const float* packedNormalWords = reinterpret_cast<const float*>(batch.Model->GetNormalBlockBytes());", source, StringComparison.Ordinal);
    Assert.Contains("projectedVertices.push_back(positionARegister);", source, StringComparison.Ordinal);
    Assert.Contains("triangleFaceNormals.push_back(faceNormal);", source, StringComparison.Ordinal);
    Assert.Contains("triangleBaseColors.push_back(::float4(", source, StringComparison.Ordinal);
    Assert.Contains("triangleLightDirections.push_back(lightDirection);", source, StringComparison.Ordinal);
    Assert.Contains("packet2_add_u64(gifPacket.get(), GS_SETREG_RGBAQ(", source, StringComparison.Ordinal);
    Assert.Contains("packet2_add_u64(gifPacket.get(), positionRegister);", source, StringComparison.Ordinal);
}
```

- [ ] **Step 3: Write the failing source test for the `VU1` diffuse-lighting microprogram**

```csharp
/// <summary>
/// Ensures the VU1 opaque draw microprogram computes flat diffuse lighting from face-normal and light-direction payload qwords.
/// </summary>
[Fact]
public void Ps2_opaque_draw_vu_program_computes_flat_diffuse_lighting_before_xgkick() {
    string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\src\platform\ps2\rendering\vu\programs\Ps2OpaqueDraw3D.vsm");

    Assert.Contains("lq.xyzw", source, StringComparison.Ordinal);
    Assert.Contains("mul.xyz", source, StringComparison.Ordinal);
    Assert.Contains("addx.x", source, StringComparison.Ordinal);
    Assert.Contains("maxx.x", source, StringComparison.Ordinal);
    Assert.Contains("ftoi4.xyz", source, StringComparison.Ordinal);
    Assert.Contains("sq.xyzw", source, StringComparison.Ordinal);
    Assert.Contains("xgkick", source, StringComparison.Ordinal);
}
```

- [ ] **Step 4: Run the focused source tests and verify they fail**

Run:

```bash
rtk dotnet test /mnt/c/dev/helworks/helengine-ps2/builder.tests/helengine.ps2.builder.tests.csproj -c Debug --filter FullyQualifiedName~Ps2_renderer3d_passes_directional_light_into_vu_opaque_packet_builder|FullyQualifiedName~Ps2_vu_vif_packet_builder_packs_normals_light_direction_and_rgbaq_for_flat_diffuse_lighting|FullyQualifiedName~Ps2_opaque_draw_vu_program_computes_flat_diffuse_lighting_before_xgkick
```

Expected: `FAIL` because the renderer does not yet pass the light vector, the packet builder does not yet pack normals and light direction, and the `VU1` program is still the trivial `xtop/xgkick` stub.

- [ ] **Step 5: Commit the failing-test checkpoint**

```bash
rtk git -C /mnt/c/dev/helworks/helengine-ps2 add builder.tests/Ps2NativeBuildInputsTests.cs
rtk git -C /mnt/c/dev/helworks/helengine-ps2 commit -m "test: lock ps2 vu flat diffuse lighting contract"
```

---

### Task 2: Extend the host-side opaque VU path for normals and directional-light payloads

**Files:**
- Modify: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/Ps2RenderManager3D.hpp`
- Modify: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Modify: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`
- Modify: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Test: `/mnt/c/dev/helworks/helengine-ps2/builder.tests/Ps2NativeBuildInputsTests.cs`

- [ ] **Step 1: Change the packet-builder signature to accept the resolved light vector**

```cpp
void AddOpaqueBatch(
    const Ps2VuOpaqueBatch& batch,
    const ::float4x4& world,
    const ::float4x4& view,
    const ::float4x4& projection,
    const ::float4& viewport,
    float nearPlaneDistance,
    const ::float3& lightDirection,
    GSGLOBAL* gsGlobal);
```

- [ ] **Step 2: Update the renderer call site to resolve and pass the light vector**

```cpp
void Ps2RenderManager3D::RenderOpaqueWithVuPath(
    const Ps2FramePlan& plan,
    const ::float4x4& view,
    const ::float4x4& projection,
    const ::float4& viewport,
    float nearPlaneDistance) {
    ::float3 lightDirection = DefaultForward;
    TryResolveDirectionalLightDirection(lightDirection);

    std::vector<Ps2VuOpaqueBatch> batches = VuOpaqueBatchBuilder.Build(plan);
    for (const Ps2VuOpaqueBatch& batch : batches) {
        const ::float4x4 worldMatrix = BuildWorldMatrix(*batch.Proxy);
        VuVifPacketBuilder.AddOpaqueBatch(
            batch,
            worldMatrix,
            view,
            projection,
            viewport,
            nearPlaneDistance,
            lightDirection,
            GsGlobal);
    }
}
```

- [ ] **Step 3: Add a local triangle payload structure for the host-side packet builder**

```cpp
struct Ps2LitTrianglePayload final {
    std::uint64_t PositionA;
    std::uint64_t PositionB;
    std::uint64_t PositionC;
    ::float3 FaceNormal;
    ::float4 BaseColor;
    ::float3 LightDirection;
};
```

- [ ] **Step 4: Read packed normals and derive one flat face normal per surviving emitted triangle**

```cpp
const float* packedNormalWords = reinterpret_cast<const float*>(batch.Model->GetNormalBlockBytes());

const ::float3 packedNormalA(
    packedNormalWords[positionWordIndexA + 0u],
    packedNormalWords[positionWordIndexA + 1u],
    packedNormalWords[positionWordIndexA + 2u]);
const ::float3 packedNormalB(
    packedNormalWords[positionWordIndexB + 0u],
    packedNormalWords[positionWordIndexB + 1u],
    packedNormalWords[positionWordIndexB + 2u]);
const ::float3 packedNormalC(
    packedNormalWords[positionWordIndexC + 0u],
    packedNormalWords[positionWordIndexC + 1u],
    packedNormalWords[positionWordIndexC + 2u]);

::float3 faceNormal = ::float3::Normalize(::float3(
    packedNormalA.X + packedNormalB.X + packedNormalC.X,
    packedNormalA.Y + packedNormalB.Y + packedNormalC.Y,
    packedNormalA.Z + packedNormalB.Z + packedNormalC.Z));
```

- [ ] **Step 5: Replace the old position-only stream with a six-qword per-triangle lit payload**

```cpp
std::vector<Ps2LitTrianglePayload> trianglePayloads;
trianglePayloads.push_back(Ps2LitTrianglePayload {
    positionARegister,
    positionBRegister,
    positionCRegister,
    faceNormal,
    ::float4(
        static_cast<float>(batch.Material->GetBaseColorR()) / 255.0f,
        static_cast<float>(batch.Material->GetBaseColorG()) / 255.0f,
        static_cast<float>(batch.Material->GetBaseColorB()) / 255.0f,
        static_cast<float>(batch.Material->GetBaseColorA()) / 255.0f),
    ::float3::Normalize(lightDirection)
});
```

- [ ] **Step 6: Emit the new GIF packet shape with `RGBAQ + XYZ2 + XYZ2 + XYZ2` per triangle**

```cpp
for (const Ps2LitTrianglePayload& trianglePayload : trianglePayloads) {
    packet2_add_u64(gifPacket.get(), GS_SETREG_RGBAQ(
        batch.Material->GetBaseColorR(),
        batch.Material->GetBaseColorG(),
        batch.Material->GetBaseColorB(),
        batch.Material->GetBaseColorA(),
        0x00));
    packet2_add_u64(gifPacket.get(), trianglePayload.PositionA);
    packet2_add_u64(gifPacket.get(), trianglePayload.PositionB);
    packet2_add_u64(gifPacket.get(), trianglePayload.PositionC);
}
```

- [ ] **Step 7: Run the focused source tests and verify the host-side contract now passes except for the microprogram test**

Run:

```bash
rtk dotnet test /mnt/c/dev/helworks/helengine-ps2/builder.tests/helengine.ps2.builder.tests.csproj -c Debug --filter FullyQualifiedName~Ps2_renderer3d_passes_directional_light_into_vu_opaque_packet_builder|FullyQualifiedName~Ps2_vu_vif_packet_builder_packs_normals_light_direction_and_rgbaq_for_flat_diffuse_lighting|FullyQualifiedName~Ps2_opaque_draw_vu_program_computes_flat_diffuse_lighting_before_xgkick
```

Expected: the first two tests `PASS`, and the `VU1` program test still `FAIL`.

- [ ] **Step 8: Commit the host-side payload change**

```bash
rtk git -C /mnt/c/dev/helworks/helengine-ps2 add src/platform/ps2/rendering/Ps2RenderManager3D.hpp src/platform/ps2/rendering/Ps2RenderManager3D.cpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp
rtk git -C /mnt/c/dev/helworks/helengine-ps2 commit -m "feat: add ps2 vu flat diffuse triangle payloads"
```

---

### Task 3: Implement flat diffuse lighting in the VU1 microprogram

**Files:**
- Modify: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm`
- Modify: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Test: `/mnt/c/dev/helworks/helengine-ps2/builder.tests/Ps2NativeBuildInputsTests.cs`

- [ ] **Step 1: Replace the trivial `xtop/xgkick` body with a linear six-qword triangle reader**

```asm
Ps2OpaqueDraw3D_CodeStart:
__ps2_opaque_draw_3d:
         NOP                                                        xtop          VI02
         lq.xyzw        VF01, 0(VI02)                               NOP
         lq.xyzw        VF02, 1(VI02)                               NOP
         lq.xyzw        VF03, 2(VI02)                               NOP
         lq.xyzw        VF04, 3(VI02)                               NOP
         lq.xyzw        VF05, 4(VI02)                               NOP
         lq.xyzw        VF06, 5(VI02)                               NOP
```

- [ ] **Step 2: Add the flat diffuse dot-product and ambient-bias sequence**

```asm
         mul.xyz        VF07, VF04, VF06                            NOP
         addx.x         VF08, VF07, VF07[X]                         NOP
         addz.x         VF08, VF08, VF07[Z]                         NOP
         maxx.x         VF08, VF08, VF00[X]                         NOP
         mulx.xyz       VF09, VF05, VF08[X]                         NOP
         addi.xyz       VF09, VF09, I                               NOP
```

- [ ] **Step 3: Convert the lit color to GS-ready integer lanes and store the output packet**

```asm
         ftoi4.xyz      VF10, VF09                                  NOP
         sq.xyzw        VF10, 6(VI02)                               NOP
         sq.xyzw        VF01, 7(VI02)                               NOP
         sq.xyzw        VF02, 8(VI02)                               NOP
         sq.xyzw        VF03, 9(VI02)                               NOP
         NOP                                                        xgkick        VI02
         NOP[E]                                                     NOP
         NOP                                                        NOP
```

- [ ] **Step 4: Update the host packet builder to allocate enough unpack space for the output kick payload**

```cpp
constexpr std::uint32_t XtopGifPacketAddress = 0;
constexpr std::uint32_t LitTriangleInputQwordCount = 6;
constexpr std::uint32_t LitTriangleOutputQwordCount = 4;

std::uint32_t gifPacketQwordCapacity = std::max<std::uint32_t>(
    32u,
    static_cast<std::uint32_t>(trianglePayloads.size()) * LitTriangleOutputQwordCount + 16u);
```

- [ ] **Step 5: Run the focused source tests and verify all three pass**

Run:

```bash
rtk dotnet test /mnt/c/dev/helworks/helengine-ps2/builder.tests/helengine.ps2.builder.tests.csproj -c Debug --filter FullyQualifiedName~Ps2_renderer3d_passes_directional_light_into_vu_opaque_packet_builder|FullyQualifiedName~Ps2_vu_vif_packet_builder_packs_normals_light_direction_and_rgbaq_for_flat_diffuse_lighting|FullyQualifiedName~Ps2_opaque_draw_vu_program_computes_flat_diffuse_lighting_before_xgkick
```

Expected: `PASS`

- [ ] **Step 6: Commit the lit VU1 microprogram**

```bash
rtk git -C /mnt/c/dev/helworks/helengine-ps2 add src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp
rtk git -C /mnt/c/dev/helworks/helengine-ps2 commit -m "feat: add ps2 vu1 flat diffuse lighting"
```

---

### Task 4: Verify the real PS2 runtime path against the spinning cube scene

**Files:**
- Modify: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/Ps2BootHost.cpp`
  - only if a slightly longer watch window or a narrower post-draw diagnostic message is needed
- Reference: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Reference: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Reference: `/mnt/c/dev/helworks/helengine-ps2/src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm`

- [ ] **Step 1: Build the focused source tests one more time before export**

Run:

```bash
rtk dotnet test /mnt/c/dev/helworks/helengine-ps2/builder.tests/helengine.ps2.builder.tests.csproj -c Debug --filter FullyQualifiedName~Ps2_renderer3d_passes_directional_light_into_vu_opaque_packet_builder|FullyQualifiedName~Ps2_vu_vif_packet_builder_packs_normals_light_direction_and_rgbaq_for_flat_diffuse_lighting|FullyQualifiedName~Ps2_opaque_draw_vu_program_computes_flat_diffuse_lighting_before_xgkick
```

Expected: `PASS`

- [ ] **Step 2: Export the PS2 build through the DLL entrypoint**

Run:

```bash
rtk dotnet /mnt/c/dev/helworks/helengine/.worktrees/normalize-camera-viewport-core/helengine.ui/helengine.editor.app/bin/Debug/net9.0-windows/helengine.editor.app.dll --build ps2 --project /mnt/c/dev/helprojs/city/project.heproj --output /mnt/c/dev/helprojs/output/ps2-vu-flatdiffuse
```

Expected: the export completes and writes `/mnt/c/dev/helprojs/output/ps2-vu-flatdiffuse/game.iso`.

- [ ] **Step 3: Copy the ISO to a short temp path and launch PCSX2**

Run:

```powershell
Copy-Item C:\dev\helprojs\output\ps2-vu-flatdiffuse\game.iso C:\tmp\ps2-vu-flatdiffuse.iso -Force
Start-Process 'C:\Program Files\PCSX2\pcsx2-qt.exe' -ArgumentList 'C:\tmp\ps2-vu-flatdiffuse.iso'
```

Expected: PCSX2 launches the game window from the short temp ISO path.

- [ ] **Step 4: Capture the game window while preserving the current watch-window discipline**

Run:

```powershell
C:\dev\helenui\artifacts\navbuild\bin\ScreenshotCli.exe list
C:\dev\helenui\artifacts\navbuild\bin\ScreenshotCli.exe capture --title 'HELENGIN.ELF' --output C:\tmp\ps2-vu-flatdiffuse.png
```

Expected: the capture shows the spinning cube with directional face brightness changes instead of a uniform white cube.

- [ ] **Step 5: Close PCSX2 immediately after capture**

Run:

```powershell
Stop-Process -Name pcsx2-qt
```

Expected: PCSX2 is closed and no emulator state is left running.

- [ ] **Step 6: If the cube is black or geometry corrupt, add one narrow runtime checkpoint and rerun**

```cpp
scr_printf("cube lit runtime checkpoint: after draw phase=%u packetBytes=%zu submitted=%zu\n",
    renderManager->GetLastVuPacketPhase(),
    renderManager->GetLastVuPacketByteCount(),
    renderManager->GetLastSubmittedTriangleCount());
```

- [ ] **Step 7: Commit the verified runtime result**

```bash
rtk git -C /mnt/c/dev/helworks/helengine-ps2 add src/platform/ps2/rendering/Ps2RenderManager3D.hpp src/platform/ps2/rendering/Ps2RenderManager3D.cpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp src/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.vsm builder.tests/Ps2NativeBuildInputsTests.cs
rtk git -C /mnt/c/dev/helworks/helengine-ps2 commit -m "feat: add ps2 vu1 flat diffuse cube lighting"
```

---

## Self-Review

- Spec coverage:
  - `VU1` ownership of lighting is covered by Tasks 2 and 3.
  - fixed six-qword per-triangle payload is covered by Task 2.
  - flat diffuse equation is covered by Task 3.
  - real runtime export and capture validation is covered by Task 4.
- Placeholder scan:
  - no `TODO`, `TBD`, or “implement later” placeholders remain.
  - every code-changing task includes an explicit code block and exact file paths.
- Type consistency:
  - the plan consistently uses `AddOpaqueBatch(..., const ::float3& lightDirection, GSGLOBAL* gsGlobal)`.
  - the renderer consistently resolves `::float3 lightDirection` and passes it to the packet builder.
  - the lit payload consistently uses `PositionA/PositionB/PositionC`, `FaceNormal`, `BaseColor`, and `LightDirection`.
