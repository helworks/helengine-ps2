# PS2 Textured VU Renderer Probe Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a standalone cooked-asset PS2 renderer probe with four boxes and one coin, then repair the minimal textured VU path so the probe renders all five objects without vertex explosion or UV corruption.

**Architecture:** DemoDisc will generate an explicit PS2 renderer-probe scene using one cooked material and one cooked texture for four box meshes and one coin mesh. The PS2 renderer will submit textured objects through a validated one-object-at-a-time VU packet path, while retaining an explicit CPU reference mode for comparison and diagnostics.

**Tech Stack:** C++ PS2 runtime, VIF/VU microcode, gsKit/GIF packets, C# DemoDisc scene generators and source-contract tests, existing PS2 cooker and project build scripts.

---

## File map

Engine files:

- `src/platform/ps2/rendering/Ps2RenderManager3D.cpp` selects CPU versus VU textured dispatch and publishes packet diagnostics.
- `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp` owns cooked-stream decoding, per-object packet assembly, GIF payload layout, and textured VIF submission.
- `src/platform/ps2/rendering/vu/Ps2VuPackedModel.hpp` and `.cpp` own the packed-mesh header and stream-offset contract.
- `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm` consumes the textured VU payload and emits GIF data.
- `builder.tests/Ps2RenderManager3DSourceTests.cs` and a focused new VU source test protect the packet and dispatch contracts.

DemoDisc files:

- `assets/codebase/rendering.tools/RenderingSceneGenerator.cs` registers and writes the new generated scene.
- `assets/codebase/rendering.tools/RenderingSceneGenerationAssets.cs` and `RenderingSceneAssetPreparationService.cs` expose the cooked coin model alongside the existing generated cube model.
- `assets/codebase/rendering.tools/Ps2VuRendererProbeSceneFactory.cs` creates the fixed camera/light, four boxes, one coin, and shared material binding.
- `assets/codebase/rendering.tools.tests/Ps2VuRendererProbeSceneSourceTests.cs` verifies scene shape and shared-asset references.
- `settings/platform.ps2.json` and the generated scene output are updated only if the existing PS2 launch/build selection requires an explicit probe entry.

Existing dirty changes in both repositories must remain untouched unless they overlap one of these files.

### Task 1: Add failing renderer contract tests

**Files:**

- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`
- Create: `builder.tests/Ps2VuTexturedPathSourceTests.cs`

- [ ] **Step 1: Write failing assertions for dispatch ownership.**

Assert that the normal textured VU path no longer contains `EnableLegacyCpuTexturedOpaquePath = true` as an unconditional textured-batch escape, that the probe/default dispatch reaches `VuVifPacketBuilder.AddOpaqueTexturedBatches`, and that any CPU path is controlled only by the explicit diagnostic mode.

- [ ] **Step 2: Write failing assertions for the packed textured payload.**

Assert the packet builder validates the packed header offsets and triangle vertex count, emits one object/material submission, passes the resolved `GSTEXTURE`, and keeps the textured GIF register list ordered as `RGBAQ`, `UV`, `XYZ2` for each vertex.

- [ ] **Step 3: Run the focused tests and confirm they fail for the current implementation.**

Run from `C:\dev\helworks\helengine-ps2`:

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3DSourceTests|FullyQualifiedName~Ps2VuTexturedPathSourceTests"
```

Expected result: the new assertions fail because the current renderer explicitly routes textured batches through `DrawOpaqueProxyLegacy`.

### Task 2: Make packed-model validation authoritative

**Files:**

- Modify: `src/platform/ps2/rendering/vu/Ps2VuPackedModel.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuPackedModel.cpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Test: `builder.tests/Ps2VuTexturedPathSourceTests.cs`

- [ ] **Step 1: Define the validation boundary.**

Extend `Ps2VuPackedModel` loading/access so the header’s position, normal, and UV qword offsets are checked against the payload length and the declared triangle vertex count. Reject missing or overlapping streams with `std::invalid_argument` or `std::out_of_range` at load time; do not return synthesized pointers.

- [ ] **Step 2: Add the failing-to-passing packet-layout contract.**

Make `Ps2VuVifPacketBuilder` consume the validated offsets and declared vertex count rather than assuming a fixed block arrangement. Keep each object’s transform/material state local to its packet and preserve 16-byte alignment for every VIF upload block.

- [ ] **Step 3: Run the focused source tests.**

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2VuTexturedPathSourceTests"
```

Expected result: PASS for header validation, qword alignment, and register-order assertions.

### Task 3: Repair the minimal textured VU dispatch

**Files:**

- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Modify: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm`
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Add an explicit VU textured-mode switch.**

Replace the unconditional textured CPU escape with a named diagnostic/default policy. The normal path must call `RenderOpaqueWithVuPath`; the CPU path may be selected only by the existing explicit CPU diagnostic setting. Do not add a per-frame auto-attach or best-effort fallback.

- [ ] **Step 2: Submit one object/material batch at a time.**

Use the existing resolved texture path and `AddOpaqueTexturedBatches` machinery, but make the first proven path dispatch one `Ps2VuOpaqueBatch` per packet. Preserve the existing VIF1 synchronization around submissions and record packet byte count, dispatch count, triangle count, and timing for each batch.

- [ ] **Step 3: Correct VU attribute interpretation.**

Audit the microcode against the C++ payload layout. Ensure position, UV, and any normal/color data are read from the same qword offsets the packet builder writes; ensure the transformed position’s `w` is preserved through perspective divide; and ensure UV values reach the GIF `UV` register without being interpreted as position words. Keep the existing GS `TEX0`/`TEX1` setup and texture dimension exponent logic.

- [ ] **Step 4: Run the renderer source tests.**

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3DSourceTests|FullyQualifiedName~Ps2VuTexturedPathSourceTests"
```

Expected result: PASS with no source assertion requiring the textured CPU escape.

### Task 4: Add the standalone DemoDisc probe scene

**Files:**

- Create: `C:\dev\helprojs\demodisc\assets\codebase\rendering.tools\Ps2VuRendererProbeSceneFactory.cs`
- Modify: `C:\dev\helprojs\demodisc\assets\codebase\rendering.tools\RenderingSceneGenerator.cs`
- Modify: `C:\dev\helprojs\demodisc\assets\codebase\rendering.tools\RenderingSceneGenerationAssets.cs`
- Modify: `C:\dev\helprojs\demodisc\assets\codebase\rendering.tools\RenderingSceneAssetPreparationService.cs`
- Create: `C:\dev\helprojs\demodisc\assets\codebase\rendering.tools.tests\Ps2VuRendererProbeSceneSourceTests.cs`

- [ ] **Step 1: Add the failing scene-shape tests.**

Verify the new factory and generator contain one explicit PS2 probe scene id, exactly four box entities, exactly one coin entity, one shared material reference, one shared texture reference, one camera, one directional light, fixed transforms, and no Tilt Trial component or scene-id mutation.

- [ ] **Step 2: Prepare the cooked coin model.**

Expose the existing common golden-coin model as a `RuntimeModel` in rendering-scene preparation, or add a rendering-owned generated coin model asset if the existing game-tools generator is not available to the rendering-tools project. The resulting model must flow through the normal scene serialization and PS2 cooker; do not embed runtime-only vertices in the probe.

- [ ] **Step 3: Implement the factory.**

Create four fixed box entities from the generated cube model and one fixed coin entity from the cooked coin model. Bind the same runtime material instance to every `MeshComponent`, write one small deterministic texture/material asset, use a fixed camera/light, disable gameplay/physics components, and expose the probe scene id through the existing rendering scene generator.

- [ ] **Step 4: Register generation and run the scene-source tests.**

```powershell
rtk dotnet test C:\dev\helprojs\demodisc\user_settings\generated_code\projects\rendering.tools.tests\rendering.tools.tests.csproj --filter "FullyQualifiedName~Ps2VuRendererProbeSceneSourceTests"
```

Expected result: PASS with generated output containing the five required objects and shared assets.

### Task 5: Cook the probe and establish the CPU reference

**Files:**

- Generated/modified by the existing DemoDisc scene generation pipeline under `C:\dev\helprojs\demodisc\assets\scenes\rendering\` and the corresponding generated material/texture/model paths.
- Test evidence: `C:\dev\helprojs\demodisc\tmp_ps2_vu_renderer_probe_cpu_reference.txt`

- [ ] **Step 1: Generate the authored probe assets.**

Run the existing DemoDisc scene-generation command used by `GameSceneGenerator`/`RenderingSceneGenerator`, then confirm the probe scene, shared material, shared texture, box model, and coin model are present before cooking.

- [ ] **Step 2: Build the PS2 package with CPU textured mode selected.**

Use the project-local PS2 output folder and the repository’s existing platform build entrypoint. Capture object count, triangle count, screen bounds, texture dimensions, and UV orientation in the reference log.

- [ ] **Step 3: Verify cooked payloads.**

Inspect the generated PS2 model/material payloads and confirm both model headers have valid position/normal/UV blocks and 16-bit triangle streams, while the five scene objects reference the same material/texture identity.

### Task 6: Run the VU probe incrementally and verify performance

**Files:**

- Modify only if required by measured evidence: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`, `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`, or `Ps2OpaqueTexturedDraw3D.vsm`.
- Evidence: `C:\dev\helprojs\demodisc\tmp_ps2_vu_renderer_probe_vu.txt`

- [ ] **Step 1: Run one box through VU.**

Verify stable vertices, correct UV orientation, nonzero VU dispatch count, and matching CPU/VU screen bounds before adding the remaining objects.

- [ ] **Step 2: Run four boxes through VU.**

Verify no vertex explosion when dispatch count increases, stable depth ordering, and packet byte/triangle counts proportional to four objects.

- [ ] **Step 3: Add the coin.**

Verify the coin uses the same material/texture state, its UVs remain correct, and its separate cooked mesh does not corrupt the box streams.

- [ ] **Step 4: Compare timings and run the smallest final validation.**

Compare CPU and VU frame timings from the runtime overlay/log. Then run:

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj
rtk dotnet test C:\dev\helprojs\demodisc\user_settings\generated_code\projects\rendering.tools.tests\rendering.tools.tests.csproj
```

Expected result: all focused and repository tests pass; the five-object probe renders through VU by default, has no vertex explosion, maps the shared texture correctly, and is materially faster than CPU textured rendering.

### Task 7: Review and commit the implementation

- [ ] **Step 1: Review both worktrees for unrelated changes.**

Run `rtk git status --short --branch` in both repositories. Do not stage or modify the existing dirty files listed in the engine and DemoDisc status reports.

- [ ] **Step 2: Review the diff against the design spec.**

Confirm the implementation contains no generated-file rewrites, no hidden CPU fallback, no default geometry/UV construction, and no Tilt Trial scene changes.

- [ ] **Step 3: Commit the engine and DemoDisc changes separately.**

Use focused commits containing only the files for this probe and renderer repair, with messages such as `Fix PS2 textured VU probe path` and `Add standalone PS2 VU renderer probe`.

## Self-review

- The design’s standalone-scene requirement is covered by Task 4.
- The one shared material/texture requirement is covered by Tasks 4 and 5.
- Cooked model/material flow is preserved by Tasks 4 and 5.
- Vertex explosion and UV corruption are tested incrementally in Tasks 1–3 and 6.
- CPU comparison and explicit failure behavior are covered by Tasks 3, 5, and 6.
- Tilt Trial isolation is asserted in Task 4 and reviewed in Task 7.
- No placeholder steps, generated-code rewrites, or best-effort fallbacks are specified.
