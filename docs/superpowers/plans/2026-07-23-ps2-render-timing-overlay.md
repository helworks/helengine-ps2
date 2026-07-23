# PS2 Render Timing Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show the v-sync-independent PS2 3D render cost and its main timing components in the existing performance overlay.

**Architecture:** `Ps2BootHost.cpp` already samples CPU 3D submission and GIF-drain time separately. The detail-row formatter will derive `Drw` from their sum and include the raw CPU submission metric as `3D`. No renderer or GS/VIF execution path changes.

**Tech Stack:** C++17, PS2SDK timing via `std::clock`, existing C# source-contract tests.

---

### Task 1: Define the overlay’s source contract

**Files:**
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`
- Test: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Write the failing test**

Add a source-contract test that reads `src/platform/ps2/Ps2BootHost.cpp` and verifies the timing formatter derives a `double averageRenderMilliseconds` from `averageDraw3dMilliseconds + averageGifWaitMilliseconds`, then emits all required labels on `FrameTimingOverlayDetailLine`:

```csharp
Assert.Contains("const double averageRenderMilliseconds = averageDraw3dMilliseconds + averageGifWaitMilliseconds;", source, StringComparison.Ordinal);
Assert.Contains("std::string(\"Drw \")", source, StringComparison.Ordinal);
Assert.Contains("+ \" 3D \")", source, StringComparison.Ordinal);
Assert.Contains("+ \" Enc \")", source, StringComparison.Ordinal);
Assert.Contains("+ \" Vif \")", source, StringComparison.Ordinal);
Assert.Contains("+ \" Gif \")", source, StringComparison.Ordinal);
```

- [ ] **Step 2: Run the focused test to verify it fails**

Run:

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3DSourceTests.Ps2BootHost_WhenPublishingFrameTiming_ShowsVsyncIndependentRenderCost" --no-restore
```

Expected: FAIL because `averageRenderMilliseconds`, `Drw`, and `3D` do not yet appear in the detail-row formatter.

- [ ] **Step 3: Commit the failing contract test**

```powershell
rtk git add -- builder.tests/Ps2RenderManager3DSourceTests.cs
rtk git commit -m "test(ps2): define render timing overlay contract"
```

### Task 2: Format the v-sync-independent renderer cost

**Files:**
- Modify: `src/platform/ps2/Ps2BootHost.cpp:743-853`
- Test: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Implement the minimal formatter change**

After calculating `averageGifWaitMilliseconds`, derive the renderer budget metric:

```cpp
const double averageRenderMilliseconds = averageDraw3dMilliseconds + averageGifWaitMilliseconds;
```

Replace the detail row with:

```cpp
FrameTimingOverlayDetailLine =
    std::string("Drw ")
    + FormatOverlayMilliseconds(averageRenderMilliseconds)
    + " 3D "
    + FormatOverlayMilliseconds(averageDraw3dMilliseconds)
    + " Enc "
    + FormatOverlayMilliseconds(averageVuPacketEncodeMilliseconds)
    + " Vif "
    + FormatOverlayMilliseconds(averageVuWaitMilliseconds)
    + " Gif "
    + FormatOverlayMilliseconds(averageGifDrainMilliseconds);
```

- [ ] **Step 2: Run the focused test to verify it passes**

Run:

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3DSourceTests.Ps2BootHost_WhenPublishingFrameTiming_ShowsVsyncIndependentRenderCost" --no-restore
```

Expected: PASS.

- [ ] **Step 3: Run the overlay source-test class**

Run:

```powershell
rtk dotnet test .\builder.tests\helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2RenderManager3DSourceTests" --no-restore
```

Expected: the new formatter test passes; any pre-existing stale source-string assertions are reported separately.

- [ ] **Step 4: Build and launch the next Colored Cubes PS2 ISO**

Increment the hard-coded `FrameTimingOverlayBuildNumber`, build the PS2 output from `C:\dev\helprojs\demodisc\project.heproj`, then launch the produced ISO with `scripts\launch_in_emulator.ps1`. Confirm the overlay renders `Drw`, `3D`, `Enc`, `Vif`, and `Gif`.

- [ ] **Step 5: Commit the implementation**

```powershell
rtk git add -- src/platform/ps2/Ps2BootHost.cpp builder.tests/Ps2RenderManager3DSourceTests.cs
rtk git commit -m "feat(ps2): show real render timing"
```
