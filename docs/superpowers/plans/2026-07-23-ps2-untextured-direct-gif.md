# PS2 Untextured Direct GIF Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every opaque untextured aggregate self-contained so Colored Cubes renders all 16 cubes without VU1 payload reuse.

**Architecture:** Retain CPU view-space transforms, clipping, flat lighting, and the existing untextured GIF template. Patch each template's three `XYZ2` words with CPU-projected positions and concatenate the completed GIF records. Submit this owned stream directly to GIF DMA; no untextured aggregate creates a VIF packet or enters a VU packet slot.

**Tech Stack:** C++20, PS2SDK packet2/gsKit, xUnit source-contract tests, Demo Disc PS2 build script, PCSX2.

---

### Task 1: Define the direct-GIF untextured contract

**Files:**
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`

- [ ] **Step 1: Write a failing builder contract test**

Add a fact named `Ps2VuVifPacketBuilder_WhenDirectGifUntexturedSubmissionIsEnabled_EmitsFinalTriangleRegisters`. It must read `Ps2VuVifPacketBuilder.cpp` and require all of:

```csharp
Assert.Contains("bool createVifPacket", source, StringComparison.Ordinal);
Assert.Contains("if (!createVifPacket) {", source, StringComparison.Ordinal);
Assert.Contains("BuildUntexturedTriangleGifPacketBytes(", source, StringComparison.Ordinal);
Assert.Contains("GifPacketBytes.resize(untexturedTrianglePackets.size() * TriangleGifPacketTemplateByteCount);", source, StringComparison.Ordinal);
Assert.DoesNotContain("packet2_utils_vu_open_unpack(packet.get(), XtopGifPacketAddress, 1);", untexturedDirectGifEncoder, StringComparison.Ordinal);
```

- [ ] **Step 2: Run the test and verify it fails because the direct-GIF branch does not exist**

Run:

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2VuVifPacketBuilder_WhenDirectGifUntexturedSubmissionIsEnabled_EmitsFinalTriangleRegisters" --no-restore
```

Expected: `FAIL`, with the missing `BuildUntexturedTriangleGifPacketBytes` contract.

- [ ] **Step 3: Write a failing renderer ownership test**

Add a fact named `Ps2RenderManager3D_WhenSubmittingUntexturedAggregates_UsesOwnedDirectGifPackets`. It must require:

```csharp
Assert.Contains("constexpr bool UseDirectGifUntexturedSubmission = true;", source, StringComparison.Ordinal);
Assert.Contains("!UseDirectGifUntexturedSubmission);", source, StringComparison.Ordinal);
Assert.Contains("dma_channel_send_packet2(gifPacket, DMA_CHANNEL_GIF, true);", untexturedAggregateRoute, StringComparison.Ordinal);
Assert.DoesNotContain("VuPacketSlots[ActiveVuPacketSlotIndex] = VuVifPacketBuilder.ReleasePacket();", untexturedAggregateRoute, StringComparison.Ordinal);
Assert.DoesNotContain("dma_channel_send_packet2(packet, DMA_CHANNEL_VIF1, 1);", untexturedAggregateRoute, StringComparison.Ordinal);
```

- [ ] **Step 4: Run the renderer test and verify it fails**

Run:

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3D_WhenSubmittingUntexturedAggregates_UsesOwnedDirectGifPackets" --no-restore
```

Expected: `FAIL`, because the aggregate route still releases VIF packets to `VuPacketSlots`.

### Task 2: Emit complete untextured GIF records

**Files:**
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Test: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Extend the aggregate builder API**

Add the final `bool createVifPacket` argument to `AddOpaqueUntexturedBatches` in the header and implementation. The renderer passes `false` only for production direct-GIF untextured submission.

- [ ] **Step 2: Add a focused completed-record helper**

Add `BuildUntexturedTriangleGifPacketBytes` beside `BuildTexturedTriangleGifPacketBytes`. It accepts a completed `Ps2VuUntexturedTrianglePayload`, projection, viewport, and GS global. It must:

```cpp
std::array<std::uint8_t, TriangleGifPacketTemplateByteCount> packetBytes {};
std::memcpy(packetBytes.data(), trianglePayload.TriangleRecord.GifPacketTemplate, TriangleGifPacketTemplateByteCount);
```

Project `PositionA`, `PositionB`, and `PositionC` through `TryBuildVertexPositionRegister`. Patch the three blank `XYZ2` words at the same qword locations used by `Ps2OpaqueDraw3D.vsm`: template qwords `6`, `8`, and `10`. Return `false` when projection rejects a vertex, so clipped or invalid triangles are not emitted.

- [ ] **Step 3: Assemble direct GIF bytes instead of a VIF packet**

In `AddOpaqueUntexturedBatches`, after clipping and flat-color template creation, build `untexturedTrianglePackets` only when `createVifPacket` is false. Concatenate each complete 11-qword record into `GifPacketBytes`, set `LastCompletedPhase = 11`, and return `acceptedBatchCount` before `CreatePacketOrThrow`.

Keep the existing VIF/VU branch intact for diagnostics only. Restore `MaximumOpaqueUntexturedPacketQwords` to `4096u`; packet capacity must no longer affect correctness.

- [ ] **Step 4: Run the builder contract test and verify it passes**

Run:

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2VuVifPacketBuilder_WhenDirectGifUntexturedSubmissionIsEnabled_EmitsFinalTriangleRegisters" --no-restore
```

Expected: `PASS`.

### Task 3: Submit untextured aggregates over GIF DMA

**Files:**
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Test: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Select direct GIF for production untextured groups**

Add `constexpr bool UseDirectGifUntexturedSubmission = true;` next to the textured direct-GIF setting. Pass `!UseDirectGifUntexturedSubmission` to `AddOpaqueUntexturedBatches` and obtain `GetGifPacketBytes()` after building.

- [ ] **Step 2: Submit one owned GIF packet per bounded aggregate**

For a non-empty direct-GIF byte stream, validate qword alignment and the `0xFFFF` qword packet limit. Allocate a `P2_TYPE_NORMAL`, `P2_MODE_NORMAL` packet, copy `gifPacketBytes`, then submit and drain it:

```cpp
dma_channel_wait(DMA_CHANNEL_GIF, 0);
dma_channel_send_packet2(gifPacket, DMA_CHANNEL_GIF, true);
dma_channel_wait(DMA_CHANNEL_GIF, 0);
packet2_free(gifPacket);
```

Record the byte count, submitted triangle count, dispatch count, and GIF phase. Do not call `WaitForVif1BeforePacketReuse`, `ReleaseVuPacketSlot`, `ReleasePacket`, or change `ActiveVuPacketSlotIndex` in this branch.

- [ ] **Step 3: Run the ownership test and the existing metric tests**

Run:

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3D_WhenSubmittingUntexturedAggregates_UsesOwnedDirectGifPackets|FullyQualifiedName~Ps2BootHost_WhenProfilingColoredCubes|FullyQualifiedName~Ps2BootHost_WhenPresentingPacketTimings" --no-restore
```

Expected: `PASS`.

### Task 4: Build and verify the new ISO

**Files:**
- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Test: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Advance the hardcoded build stamp**

Change `FrameTimingOverlayBuildNumber` from `B43` to `B44`, then change the corresponding source-contract test name and expected literal.

- [ ] **Step 2: Run the focused complete test set**

Run:

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2VuVifPacketBuilder_WhenDirectGifUntexturedSubmissionIsEnabled_EmitsFinalTriangleRegisters|FullyQualifiedName~Ps2RenderManager3D_WhenSubmittingUntexturedAggregates_UsesOwnedDirectGifPackets|FullyQualifiedName~Ps2BootHost_WhenPublishingFrameTiming_PrefixesTheFpsRowWithBuildNumberB44|FullyQualifiedName~Ps2BootHost_WhenProfilingColoredCubes" --no-restore
```

Expected: all selected tests pass.

- [ ] **Step 3: Produce and launch the exact B44 ISO**

Run the project’s PS2 build script with:

```powershell
rtk powershell -NoProfile -ExecutionPolicy Bypass -File C:\dev\helworks\helengine\scripts\build-platform.ps1 -Project C:\dev\helprojs\demodisc\project.heproj -Platform ps2 -Output C:\dev\helprojs\demodisc\ps2-build-colored-cubes-b44
```

After `ps2-build-phase.txt` contains `packaged outputs verified`, launch:

```powershell
rtk powershell -NoProfile -ExecutionPolicy Bypass -File scripts\launch_in_emulator.ps1 -ArtifactPath C:\dev\helprojs\demodisc\ps2-build-colored-cubes-b44\game.iso
```

- [ ] **Step 4: Verify runtime behavior**

Use the permitted HelenUI capture-and-OCR workflow only. Confirm the game is not in the `FPS: N/A` failure state, then ask the user to confirm that all 16 cubes are visible under the `B44` stamp.
