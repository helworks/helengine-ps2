# PS2 VU Single-Buffer Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render 48 textured source triangles per VU job without input data overlapping VU GIF output.

**Architecture:** The textured VU path will use fixed VU memory regions: input data at QW 0 through 511 and GIF output at QW 512 through 1023. VIF UNPACK commands will address fixed memory rather than the current double-buffer TOP; each FLUSH before MSCAL serializes reuse of the input and output regions.

**Tech Stack:** PS2SDK packet2 VIF DMA, VU1 microprogram assembly, xUnit source-contract tests.

---

### Task 1: Lock the non-overlapping VU layout in tests

**Files:**
- Modify: `builder.tests/Ps2TexturedVuBatchWidthSourceTests.cs`
- Modify: `builder.tests/Ps2TexturedVuReferencePayloadSourceTests.cs`
- Test: `builder.tests/helengine.ps2.builder.tests.csproj`

- [ ] **Step 1: Write failing source-contract tests**

```csharp
Assert.Contains("constexpr std::size_t MaximumTexturedVuSourceTriangleCount = 48u;", renderManagerSource);
Assert.Contains("constexpr std::size_t MaximumTexturedVuSourceTriangleCount = 48u;", packetBuilderSource);
Assert.Contains("packet2_utils_vu_open_unpack(packet.get(), TexturedVuInputStartQword, 0);", source);
Assert.Contains("iaddiu VI03, VI00, 0x00000200", microprogramSource);
```

- [ ] **Step 2: Run the focused tests and verify they fail because the old 32-triangle/TOP layout remains**

Run: `dotnet test builder.tests/helengine.ps2.builder.tests.csproj --no-restore --filter FullyQualifiedName~Ps2TexturedVu --results-directory C:\dev\helworks\builds\helengine-ps2\test-results\B138`

Expected: failure identifying the absent 48-triangle and fixed-layout constants.

### Task 2: Move textured VU input and output to fixed regions

**Files:**
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp:31-60,2356-2387`
- Modify: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm:24-27`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp:142`
- Modify: `src/platform/ps2/Ps2BootHost.cpp:189`

- [ ] **Step 1: Define fixed layout constants and raise the texture source slice width**

```cpp
constexpr std::uint32_t TexturedVuInputStartQword = 0u;
constexpr std::size_t MaximumTexturedVuSourceTriangleCount = 48u;
```

- [ ] **Step 2: Emit all textured shared-state and payload UNPACK commands with `t_use_top = 0`**

```cpp
packet2_utils_vu_open_unpack(packet.get(), TexturedVuInputStartQword, 0);
packet2_utils_vu_add_unpack_data(packet.get(), TexturedVuInputStartQword + sharedStateQwordCount, payload, payloadQwordCount, 0);
```

- [ ] **Step 3: Make the textured microprogram load fixed input and write fixed upper-memory GIF output**

```asm
NOP                                                        iaddiu VI02, VI00, 0x00000000
NOP                                                        iaddiu VI03, VI00, 0x00000200
```

- [ ] **Step 4: Leave the shared VIF double-buffer initialization unchanged and increment the overlay marker to B138**

```cpp
constexpr const char* FrameTimingOverlayBuildNumber = "B138";
```

- [ ] **Step 5: Run the focused source-contract tests and verify they pass**

Run: `dotnet test builder.tests/helengine.ps2.builder.tests.csproj --no-restore --filter FullyQualifiedName~Ps2TexturedVu --results-directory C:\dev\helworks\builds\helengine-ps2\test-results\B138`

Expected: all focused VU source tests pass.

### Task 3: Build and validate the isolated scene

**Files:**
- Output: `C:\dev\helworks\builds\demodisc\ps2\B138-tilt-play-level-01-vu48-single-buffer-cli\game.iso`

- [ ] **Step 1: Produce the ISO with the editor CLI using the existing isolated Tilt Play staging project.**

- [ ] **Step 2: Verify the ISO exists and is newer than the build start.**

- [ ] **Step 3: Launch only with `scripts/launch_in_emulator.ps1`.**

- [ ] **Step 4: Capture and OCR the `HELENGIN.ELF` PCSX2 window through HelenUI.**

- [ ] **Step 5: Treat the build as valid only if it shows B138 and the user confirms no triangle corruption.**

No commit is included: the user has not requested one.
