# PS2 Rendering Performance Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish trustworthy PS2 renderer timings and reduce opaque untextured VIF1 submission overhead without changing scene content, alpha ordering, or textured-material behavior.

**Architecture:** Add one `Ps2RenderPerformanceMetrics` record owned by `Ps2RenderManager3D`; the PS2 boot host samples that record after the GIF drain and publishes stable overlay/log lines. For untextured opaque work, aggregate batches with the same runtime material into bounded VIF packets. Detach packet ownership from `Ps2VuVifPacketBuilder` into two render-manager slots so the EE can encode the next group while VIF1 processes the current one, waiting only immediately before a DMA channel or packet slot is reused.

**Tech Stack:** C++20, PS2SDK `packet2` and DMA APIs, gsKit, C# xUnit source-contract tests, DemoDisc PS2 build pipeline, PCSX2.

---

## Preconditions and Boundaries

- Work only in `C:\dev\helworks\helengine-ps2` for this plan. Do not edit the DemoDisc scene, generated code, or the parent `helengine` repository.
- Preserve the existing uncommitted changes to `builder.tests/Ps2PlatformAssetBuilderTests.cs` and `builder/Ps2NativeBuildExecutor.cs`.
- The current test project is blocked before test execution by the pre-existing `Ps2PlatformAssetBuilderTests.cs(2761,45)` argument-type error. Do not repair that unrelated change as part of this plan. Run the focused test commands after that workspace issue is resolved, or record that exact blocker.
- Keep `UseLegacyCpuOpaquePath`, `EnableLegacyCpuTexturedOpaquePath`, alpha rendering, HDR/glow behavior, and camera clipping semantics unchanged. Textured VU migration, proxy culling, and quality tiers belong to later plans.
- Do not make a hidden fallback the standard path. Any diagnostic fallback must be explicitly named and counted.

## File Structure

| Path | Responsibility |
| --- | --- |
| `src/platform/ps2/rendering/Ps2RenderPerformanceMetrics.hpp` | Defines one value-type record for the most recent PS2 3D render and its counters. |
| `src/platform/ps2/rendering/Ps2RenderManager3D.hpp` | Exposes the metrics record, packet-slot lifecycle, and packet-detach helpers. |
| `src/platform/ps2/rendering/Ps2RenderManager3D.cpp` | Measures opaque work, aggregates compatible untextured batches, owns detached packet slots, and schedules VIF1 safely. |
| `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp` | Adds detach and bounded untextured aggregate packet APIs. |
| `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp` | Builds one untextured VIF packet for a compatible bounded group without freeing a submitted packet. |
| `src/platform/ps2/Ps2BootHost.cpp` | Measures the post-3D GIF drain, samples the renderer record, and displays the timing source labels. |
| `builder.tests/Ps2RenderManager3DSourceTests.cs` | Guards metric ownership, aggregation, and legacy timing source contracts. |
| `builder.tests/Ps2NativeBuildExecutorTests.cs` | Guards packet ownership and VIF1 wait/submit scheduling contracts. |
| `docs/ps2-rendering-performance-benchmark.md` | Defines the reproducible DemoDisc measurement path and result-record format. |

## Metric Names and Semantics

The implementation uses these exact fields in `Ps2RenderPerformanceMetrics`:

```cpp
double ProxySyncMilliseconds;
double FramePlanMilliseconds;
double VuBatchBuildMilliseconds;
double PacketEncodeMilliseconds;
double VifReuseWaitMilliseconds;
double VifSubmitMilliseconds;
double GifDrainMilliseconds;
double LegacyOpaqueMilliseconds;
std::size_t SubmittedTriangleCount;
std::size_t LegacyOpaqueTriangleCount;
std::size_t VifPacketCount;
std::size_t VifPacketByteCount;
std::size_t CompatibleUntexturedGroupCount;
```

`GifDrainMilliseconds` is the elapsed time around the existing `dma_channel_wait(DMA_CHANNEL_GIF, 0)` after 3D draw. It is the measured GIF/GS drain boundary; it must not be labelled as pure GS fill rate. `VifReuseWaitMilliseconds` is only time spent waiting for VIF1 before a packet slot or the VIF1 channel is reused. `PacketEncodeMilliseconds` remains EE CPU time.

## Task 1: Add the Performance Metrics Value Type

**Files:**

- Create: `src/platform/ps2/rendering/Ps2RenderPerformanceMetrics.hpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.hpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Test: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Write the failing source-contract test**

Add this test to `Ps2RenderManager3DSourceTests.cs`:

```csharp
[Fact]
public void Ps2RenderManager3D_WhenPublishingPerformanceData_UsesDedicatedMetricsRecord() {
    string repositoryRootPath = GetRepositoryRootPath();
    string headerPath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.hpp");
    string sourcePath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
    string metricsPath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderPerformanceMetrics.hpp");

    Assert.True(File.Exists(metricsPath));
    Assert.Contains("const Ps2RenderPerformanceMetrics& GetLastPerformanceMetrics() const;", File.ReadAllText(headerPath), StringComparison.Ordinal);
    Assert.Contains("Ps2RenderPerformanceMetrics LastPerformanceMetrics;", File.ReadAllText(headerPath), StringComparison.Ordinal);

    string metrics = File.ReadAllText(metricsPath);
    Assert.Contains("double VifReuseWaitMilliseconds;", metrics, StringComparison.Ordinal);
    Assert.Contains("double GifDrainMilliseconds;", metrics, StringComparison.Ordinal);
    Assert.Contains("double LegacyOpaqueMilliseconds;", metrics, StringComparison.Ordinal);
    Assert.Contains("std::size_t CompatibleUntexturedGroupCount;", metrics, StringComparison.Ordinal);

    string source = File.ReadAllText(sourcePath);
    Assert.Contains("const Ps2RenderPerformanceMetrics& Ps2RenderManager3D::GetLastPerformanceMetrics() const", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the focused test and record the baseline result**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3D_WhenPublishingPerformanceData_UsesDedicatedMetricsRecord" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Expected before the implementation: the new assertions fail. If compilation stops first at `Ps2PlatformAssetBuilderTests.cs(2761,45)`, record that blocker and do not modify the unrelated file.

- [ ] **Step 3: Create the zero-initialized metrics record**

Create `Ps2RenderPerformanceMetrics.hpp`:

```cpp
#pragma once

#include <cstddef>

namespace helengine::ps2 {
    struct Ps2RenderPerformanceMetrics final {
        double ProxySyncMilliseconds = 0.0;
        double FramePlanMilliseconds = 0.0;
        double VuBatchBuildMilliseconds = 0.0;
        double PacketEncodeMilliseconds = 0.0;
        double VifReuseWaitMilliseconds = 0.0;
        double VifSubmitMilliseconds = 0.0;
        double GifDrainMilliseconds = 0.0;
        double LegacyOpaqueMilliseconds = 0.0;
        std::size_t SubmittedTriangleCount = 0u;
        std::size_t LegacyOpaqueTriangleCount = 0u;
        std::size_t VifPacketCount = 0u;
        std::size_t VifPacketByteCount = 0u;
        std::size_t CompatibleUntexturedGroupCount = 0u;
    };
}
```

Include this file in `Ps2RenderManager3D.hpp`, add `LastPerformanceMetrics`, and add:

```cpp
const Ps2RenderPerformanceMetrics& GetLastPerformanceMetrics() const;
void SetLastGifDrainMilliseconds(double milliseconds);
```

`SetLastGifDrainMilliseconds` must reject negative values with `std::invalid_argument`; a negative elapsed duration is invalid diagnostic input.

- [ ] **Step 4: Populate the record from existing renderer measurements**

At the beginning of `Ps2RenderManager3D::Draw`, replace the individual per-frame timing resets with:

```cpp
LastPerformanceMetrics = Ps2RenderPerformanceMetrics {};
```

Assign the existing measured values to the record at their existing boundaries:

```cpp
LastPerformanceMetrics.ProxySyncMilliseconds = ResolveMillisecondsFromClockTicks(proxySyncStartTicks, proxySyncEndTicks);
LastPerformanceMetrics.FramePlanMilliseconds = ResolveMillisecondsFromClockTicks(framePlanStartTicks, framePlanEndTicks);
LastPerformanceMetrics.VuBatchBuildMilliseconds = ResolveMillisecondsFromClockTicks(vuBatchBuildStartTicks, vuBatchBuildEndTicks);
LastPerformanceMetrics.PacketEncodeMilliseconds += ResolveMillisecondsFromClockTicks(vuPacketEncodeStartTicks, vuPacketEncodeEndTicks);
LastPerformanceMetrics.VifReuseWaitMilliseconds += ResolveMillisecondsFromClockTicks(vuInitialWaitStartTicks, vuInitialWaitEndTicks);
LastPerformanceMetrics.VifSubmitMilliseconds += ResolveMillisecondsFromClockTicks(vuSubmitStartTicks, vuSubmitEndTicks);
LastPerformanceMetrics.SubmittedTriangleCount += VuVifPacketBuilder.GetSubmittedTriangleCount();
LastPerformanceMetrics.VifPacketByteCount += VuVifPacketBuilder.GetPacketByteCount();
LastPerformanceMetrics.VifPacketCount += 1u;
```

Add a private `DrawOpaqueProxyLegacyTimed` wrapper and replace only normal opaque uses of `DrawOpaqueProxyLegacy` with it. The wrapper must not be used by `DrawAlphaProxy`, because alpha is intentionally outside this milestone's legacy-opaque metric:

```cpp
void Ps2RenderManager3D::DrawOpaqueProxyLegacyTimed(
    const Ps2RenderProxy& proxy,
    const ::float4x4& view,
    const ::float4x4& projection,
    const ::float4& viewport,
    float nearPlaneDistance) {
    const std::clock_t startTicks = std::clock();
    DrawOpaqueProxyLegacy(proxy, view, projection, viewport, nearPlaneDistance);
    const std::clock_t endTicks = std::clock();
    LastPerformanceMetrics.LegacyOpaqueMilliseconds += ResolveMillisecondsFromClockTicks(startTicks, endTicks);

    Ps2RuntimeModel* model = proxy.GetModel();
    if (model != nullptr && model->GetVuPackedModel() != nullptr) {
        LastPerformanceMetrics.LegacyOpaqueTriangleCount += model->GetVuPackedModel()->GetTriangleVertexCount() / 3u;
    }
}
```

Keep the existing public `GetLastVu...` methods for this milestone, but make each return its matching value from `LastPerformanceMetrics`. `GetLastVuBatchDispatchCount()` returns `LastPerformanceMetrics.VifPacketCount`.

- [ ] **Step 5: Add the getter and GIF-drain setter**

Implement the two public methods in `Ps2RenderManager3D.cpp`:

```cpp
const Ps2RenderPerformanceMetrics& Ps2RenderManager3D::GetLastPerformanceMetrics() const {
    return LastPerformanceMetrics;
}

void Ps2RenderManager3D::SetLastGifDrainMilliseconds(double milliseconds) {
    if (milliseconds < 0.0) {
        throw std::invalid_argument("PS2 GIF drain duration cannot be negative.");
    }

    LastPerformanceMetrics.GifDrainMilliseconds = milliseconds;
}
```

- [ ] **Step 6: Run the focused tests**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3DSourceTests" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Expected: the source-contract tests pass once the unrelated compile blocker is resolved.

- [ ] **Step 7: Commit the metrics record**

```powershell
rtk git add src\platform\ps2\rendering\Ps2RenderPerformanceMetrics.hpp src\platform\ps2\rendering\Ps2RenderManager3D.hpp src\platform\ps2\rendering\Ps2RenderManager3D.cpp builder.tests\Ps2RenderManager3DSourceTests.cs
rtk git commit -m "Instrument PS2 renderer performance metrics"
```

## Task 2: Sample the GIF Drain and Publish Clear Timing Labels

**Files:**

- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`
- Create: `docs/ps2-rendering-performance-benchmark.md`

- [ ] **Step 1: Write the failing boot-host source-contract test**

Add this test to `Ps2RenderManager3DSourceTests.cs`:

```csharp
[Fact]
public void Ps2BootHost_WhenCollectingFrameTiming_RecordsGifDrainInRendererMetrics() {
    string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
    string source = File.ReadAllText(sourcePath);

    Assert.Contains("RenderManager3DBackend.SetLastGifDrainMilliseconds(", source, StringComparison.Ordinal);
    Assert.Contains("GetLastPerformanceMetrics()", source, StringComparison.Ordinal);
    Assert.Contains("Gif ", source, StringComparison.Ordinal);
    Assert.Contains("Vif ", source, StringComparison.Ordinal);
    Assert.Contains("Leg ", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the test and confirm it fails before the host change**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2BootHost_WhenCollectingFrameTiming_RecordsGifDrainInRendererMetrics" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Expected: FAIL because the host does not yet forward the GIF-drain duration to the renderer record.

- [ ] **Step 3: Record the existing GIF drain in the renderer metrics record**

Immediately after the existing `dma_channel_wait(DMA_CHANNEL_GIF, 0)` and `frameGifWaitEndTicks` assignment, add:

```cpp
RenderManager3DBackend.SetLastGifDrainMilliseconds(
    ResolveMillisecondsFromClockTicks(frameDraw3dEndTicks, frameGifWaitEndTicks));
```

Do not add another GIF wait or `FINISH` command. The plan measures the already-required frame boundary.

- [ ] **Step 4: Make the sample accumulator consume the record**

At the beginning of `RecordRenderManagerTimingSample`, bind one reference:

```cpp
const helengine::ps2::Ps2RenderPerformanceMetrics& metrics = renderManager3DBackend.GetLastPerformanceMetrics();
```

Replace the repeated renderer getter reads with the record fields. Add sample totals for `GifDrainMilliseconds`, `LegacyOpaqueMilliseconds`, `LegacyOpaqueTriangleCount`, `VifPacketByteCount`, and `CompatibleUntexturedGroupCount`. Preserve `FrameTimingDraw3dSeconds` as the total CPU draw time; it is not interchangeable with GIF drain time.

- [ ] **Step 5: Publish unambiguous overlay and boot-log labels**

Keep the existing first two visible lines compatible with the current overlay layout. Change the detail and additional text to include these exact labels:

```text
Enc <ms> Vif <ms> Sub <ms> Gif <ms>
Leg <ms> Tri <count> Pkt <count> Bytes <count> Grp <count>
```

`Gif` means post-draw GIF/GS drain, `Vif` means VIF1 reuse wait, and `Leg` means legacy opaque CPU route. Emit the same labels in the `frame timing avg` boot-log entry.

- [ ] **Step 6: Add the benchmark protocol**

Create `docs/ps2-rendering-performance-benchmark.md` containing:

```markdown
# PS2 Rendering Performance Benchmark

## Standard capture

1. Build the newest DemoDisc PS2 ISO with the render test scene selected.
2. Launch that ISO in PCSX2.
3. Wait for the timing sample window to complete.
4. Capture the `frame timing avg` boot-log line.
5. Repeat the same free-camera path: scene start, close to a large box, then coin behind the camera.

## Required result row

`commit | scene | camera path | FPS | Drw | Set | Enc | Vif | Sub | Gif | Leg | Tri | Pkt | Bytes | Grp | visual result`

Do not compare captures made from different ISOs, scene selections, or timing sample windows.
```

- [ ] **Step 7: Run the focused source tests and commit**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3DSourceTests" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Then commit:

```powershell
rtk git add src\platform\ps2\Ps2BootHost.cpp builder.tests\Ps2RenderManager3DSourceTests.cs docs\ps2-rendering-performance-benchmark.md
rtk git commit -m "Expose PS2 renderer timing breakdown"
```

## Task 3: Add Safe Packet Detachment and Slot Ownership

**Files:**

- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.hpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Test: `builder.tests/Ps2NativeBuildExecutorTests.cs`

- [ ] **Step 1: Write failing packet-ownership tests**

Add this test to `Ps2NativeBuildExecutorTests.cs`:

```csharp
[Fact]
public void Ps2VuVifPacketBuilder_WhenPacketIsSubmitted_TransfersPacketOwnershipToRendererSlot() {
    string root = ResolveRepositoryRoot();
    string builderHeader = File.ReadAllText(Path.Combine(root, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.hpp"));
    string builderSource = File.ReadAllText(Path.Combine(root, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));
    string managerHeader = File.ReadAllText(Path.Combine(root, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.hpp"));
    string managerSource = File.ReadAllText(Path.Combine(root, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp"));

    Assert.Contains("packet2_t* ReleasePacket();", builderHeader, StringComparison.Ordinal);
    Assert.Contains("packet2_t* Ps2VuVifPacketBuilder::ReleasePacket()", builderSource, StringComparison.Ordinal);
    Assert.Contains("packet2_t* VuPacketSlots[2];", managerHeader, StringComparison.Ordinal);
    Assert.Contains("void Ps2RenderManager3D::ReleaseVuPacketSlot", managerSource, StringComparison.Ordinal);
    Assert.Contains("VuVifPacketBuilder.ReleasePacket()", managerSource, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the focused test and confirm it fails**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2VuVifPacketBuilder_WhenPacketIsSubmitted_TransfersPacketOwnershipToRendererSlot" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Expected: FAIL because the builder currently owns and frees `Packet` in `Reset()`.

- [ ] **Step 3: Add the ownership-transfer API**

Add to `Ps2VuVifPacketBuilder.hpp`:

```cpp
packet2_t* ReleasePacket();
```

Implement it without allocating or freeing memory:

```cpp
packet2_t* Ps2VuVifPacketBuilder::ReleasePacket() {
    packet2_t* packet = Packet;
    Packet = nullptr;
    return packet;
}
```

`Reset()` continues to free only a packet the builder still owns.

- [ ] **Step 4: Add two render-manager packet slots**

In `Ps2RenderManager3D.hpp`, add these private members and methods:

```cpp
packet2_t* VuPacketSlots[2];
std::size_t ActiveVuPacketSlotIndex;

void ReleaseVuPacketSlot(std::size_t slotIndex);
void WaitForVif1BeforePacketReuse();
```

Initialize both slots to `nullptr` and `ActiveVuPacketSlotIndex` to `0u` in the constructor. In the destructor, wait for VIF1 once if either slot is non-null, then call `ReleaseVuPacketSlot(0u)` and `ReleaseVuPacketSlot(1u)`.

- [ ] **Step 5: Implement the safe reuse boundary**

Implement the wait helper:

```cpp
void Ps2RenderManager3D::WaitForVif1BeforePacketReuse() {
    const std::clock_t waitStartTicks = std::clock();
    dma_channel_wait(DMA_CHANNEL_VIF1, 0);
    const std::clock_t waitEndTicks = std::clock();
    LastPerformanceMetrics.VifReuseWaitMilliseconds += ResolveMillisecondsFromClockTicks(waitStartTicks, waitEndTicks);
}
```

`ReleaseVuPacketSlot` must call `packet2_free` only when the slot is non-null, then clear it. Always call `WaitForVif1BeforePacketReuse()` before freeing a non-null slot or submitting another VIF1 packet. This preserves packet memory until DMA has finished reading it.

- [ ] **Step 6: Replace builder-owned send with slot-owned send**

For each normal VIF1 submission:

```cpp
WaitForVif1BeforePacketReuse();
ReleaseVuPacketSlot(ActiveVuPacketSlotIndex);
VuPacketSlots[ActiveVuPacketSlotIndex] = VuVifPacketBuilder.ReleasePacket();
packet2_t* packet = VuPacketSlots[ActiveVuPacketSlotIndex];
if (packet == nullptr) {
    continue;
}

const std::clock_t submitStartTicks = std::clock();
dma_channel_send_packet2(packet, DMA_CHANNEL_VIF1, 1);
const std::clock_t submitEndTicks = std::clock();
LastPerformanceMetrics.VifSubmitMilliseconds += ResolveMillisecondsFromClockTicks(submitStartTicks, submitEndTicks);
ActiveVuPacketSlotIndex = (ActiveVuPacketSlotIndex + 1u) % 2u;
```

Build the next packet before this wait boundary whenever a next compatible group exists. Do not submit to a busy VIF1 channel.

- [ ] **Step 7: Run ownership/scheduling source tests and commit**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2NativeBuildExecutorTests" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Then commit:

```powershell
rtk git add src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.hpp src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.cpp src\platform\ps2\rendering\Ps2RenderManager3D.hpp src\platform\ps2\rendering\Ps2RenderManager3D.cpp builder.tests\Ps2NativeBuildExecutorTests.cs
rtk git commit -m "Own PS2 VIF packets until DMA completion"
```

## Task 4: Build Bounded Compatible Untextured Packet Groups

**Files:**

- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Test: `builder.tests/Ps2NativeBuildExecutorTests.cs`

- [ ] **Step 1: Write the failing aggregation source-contract test**

Add this test to `Ps2NativeBuildExecutorTests.cs`:

```csharp
[Fact]
public void Ps2Renderer_WhenOpaqueUntexturedBatchesShareMaterial_AggregatesOneBoundedVifPacket() {
    string root = ResolveRepositoryRoot();
    string builderHeader = File.ReadAllText(Path.Combine(root, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.hpp"));
    string builderSource = File.ReadAllText(Path.Combine(root, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));
    string managerSource = File.ReadAllText(Path.Combine(root, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp"));

    Assert.Contains("std::size_t AddOpaqueUntexturedBatches(", builderHeader, StringComparison.Ordinal);
    Assert.Contains("constexpr std::uint16_t MaximumOpaqueUntexturedPacketQwords", builderSource, StringComparison.Ordinal);
    Assert.Contains("BuildCompatibleUntexturedGroups", managerSource, StringComparison.Ordinal);
    Assert.Contains("LastPerformanceMetrics.CompatibleUntexturedGroupCount", managerSource, StringComparison.Ordinal);
    Assert.DoesNotContain("VuVifPacketBuilder.Reset();\n            const std::clock_t vuPacketEncodeStartTicks", managerSource, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the aggregation test and confirm it fails**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2Renderer_WhenOpaqueUntexturedBatchesShareMaterial_AggregatesOneBoundedVifPacket" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Expected: FAIL because each batch currently resets, encodes, waits, and submits independently.

- [ ] **Step 3: Add the aggregate builder contract**

Add to `Ps2VuVifPacketBuilder.hpp`:

```cpp
std::size_t AddOpaqueUntexturedBatches(
    const std::vector<const Ps2VuOpaqueBatch*>& batches,
    const std::vector<::float4x4>& worlds,
    const ::float4x4& view,
    const ::float4x4& projection,
    const ::float4& viewport,
    float nearPlaneDistance,
    const ::float3& lightDirection,
    GSGLOBAL* gsGlobal);
```

The method must throw `std::invalid_argument` when `batches.size() != worlds.size()`. It returns `0u` without allocating when `batches.empty()` and otherwise returns the number of leading batches accepted into the packet.

- [ ] **Step 4: Implement aggregate packet assembly with a fixed upper bound**

In `Ps2VuVifPacketBuilder.cpp`, declare:

```cpp
constexpr std::uint16_t MaximumOpaqueUntexturedPacketQwords = 4096u;
```

Refactor the existing untextured branch of `AddOpaqueBatch` into one internal sequence that appends every valid triangle from the provided batches to the same source-payload and GIF-template vectors. Preserve all current near and screen frustum clipping, normal calculation, lighting, submitted-triangle diagnostics, and VU microprogram commands.

Before allocating, calculate the packet qword requirement. If appending the next batch would exceed `MaximumOpaqueUntexturedPacketQwords`, stop before that batch, return the accepted leading-batch count, and let the render manager begin the next packet from that batch. Never truncate a triangle or create a packet larger than the cap.

The aggregate builder creates exactly one `packet2_t` for its accepted group and sets `Packet` only after the chain is complete.

- [ ] **Step 5: Build groups in the render manager without changing texture behavior**

Add a private `BuildCompatibleUntexturedGroups` method to `Ps2RenderManager3D.cpp`. It must:

1. Ignore `batch.Textured` batches; they remain on their existing path.
2. Start a group with the first untextured batch.
3. Append only following untextured batches whose `Material` pointer equals the first batch's `Material` pointer.
4. Preserve the source order of batches within each material-compatible group.
5. Let `AddOpaqueUntexturedBatches` split a material-compatible group at the packet qword cap and return the number of consumed batches.

For each group, call `VuGifStateEncoder.EncodeOpaqueState(*group.front(), GsGlobal)` once for every packet slice, then call `AddOpaqueUntexturedBatches` with the unconsumed suffix. Advance by the returned count; throw `std::runtime_error` if a non-empty suffix returns `0u`, because that means the qword cap cannot represent even its first batch. Increment `LastPerformanceMetrics.CompatibleUntexturedGroupCount` once per non-empty packet slice and increment `VifPacketCount` only after a non-null packet is submitted.

Do not sort batches in this plan. State sorting is part of the later visibility/submission plan; preserving current order limits depth and material-regression risk.

- [ ] **Step 6: Retain a named diagnostic fallback**

Add this renderer-local constant beside the other VU diagnostics:

```cpp
constexpr bool EnableUntexturedAggregatePacketDiagnostics = false;
```

When true, render the old one-batch-at-a-time untextured sequence with the new packet-slot lifetime rules. When false, use aggregate groups. Increment `CompatibleUntexturedGroupCount` in both modes so the overlay exposes which route ran.

- [ ] **Step 7: Run focused source tests and commit**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2NativeBuildExecutorTests|FullyQualifiedName~Ps2RenderManager3DSourceTests" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Then commit:

```powershell
rtk git add src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.hpp src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.cpp src\platform\ps2\rendering\Ps2RenderManager3D.cpp builder.tests\Ps2NativeBuildExecutorTests.cs
rtk git commit -m "Batch compatible PS2 opaque VIF packets"
```

## Task 5: Verify the Newest DemoDisc ISO and Capture the Result

**Files:**

- Verify: `docs/ps2-rendering-performance-benchmark.md`
- Verify: `C:\dev\helprojs\demodisc\tmp\<new-performance-build>\game.iso`

- [ ] **Step 1: Build the newest render-test ISO**

Use the existing DemoDisc PS2 build workflow with the render test scene selected. The output directory name must be new for this optimization pass; do not launch a prior completed ISO.

Expected: the final phase log contains `native build completed`, `iso packaged`, and `packaged outputs verified` for the new output directory.

- [ ] **Step 2: Launch the newly built ISO in PCSX2**

Launch only the ISO produced in Step 1. Do not take screenshots unless the user explicitly authorizes them.

- [ ] **Step 3: Validate the baseline and clipping positions**

Check these camera positions in order:

```text
1. Render-test scene initial camera position: opaque scene is visible.
2. Close to a large box: no warped face when a vertex reaches the camera boundary.
3. Coin behind camera: no near-fullscreen coin geometry.
```

Expected: no black frame, missing geometry, texture regression, depth regression, or camera-clipping regression.

- [ ] **Step 4: Capture before/after metrics using the documented row**

Record the `frame timing avg` line after the same sample window and camera path. Compare `FPS`, `Drw`, `Set`, `Enc`, `Vif`, `Sub`, `Gif`, `Leg`, `Tri`, `Pkt`, `Bytes`, and `Grp` against the baseline.

Acceptance for this foundation slice:

```text
Visual correctness: unchanged
Vif packet count: lower when a material-compatible untextured group contains multiple proxies
Vif reuse wait: not higher after equivalent grouping
Packet encode: not materially higher for the same submitted triangle count
Legacy opaque: unchanged because textured migration is out of scope
```

- [ ] **Step 5: Diagnose before expanding scope**

If `Gif` dominates after the aggregate path, stop this plan and schedule the GS quality-policy plan. If `Leg` dominates, schedule the textured opaque VU plan. If `Enc` dominates, schedule compact VU input payload work. If `Vif` dominates while packet count is already low, inspect VIF/VU microprogram throughput before adding culling or post-processing changes.

- [ ] **Step 6: Commit the benchmark record**

Append the before/after rows to `docs/ps2-rendering-performance-benchmark.md`, then commit:

```powershell
rtk git add docs\ps2-rendering-performance-benchmark.md
rtk git commit -m "Record PS2 renderer performance foundation benchmark"
```

## Final Verification

- [ ] Run `rtk git diff --check` and confirm no whitespace errors.
- [ ] Run the focused builder test project once the known unrelated compile blocker is resolved.
- [ ] Confirm `rtk git status --short` shows only intentionally preserved user changes or a clean worktree.
- [ ] Confirm the benchmark used the newest output ISO and not a previous completed build.
- [ ] Confirm all visual checks pass before claiming a performance improvement.

## Follow-up Plans Triggered by the Result

| Dominant measured cost | Next approved plan |
| --- | --- |
| `Leg` | Textured opaque VU route. |
| `Enc` | Compact VU input payload and shared batch constants. |
| `Vif` with low packet count | VIF/VU microprogram throughput and packet double-buffering. |
| `Gif` | GS fill-rate quality policy, alpha-overdraw controls, and post-effect tiers. |
| High triangle/byte count with low waits | Proxy bounds/frustum classification and LOD plan. |
