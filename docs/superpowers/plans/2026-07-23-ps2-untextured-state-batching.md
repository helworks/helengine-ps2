# PS2 Untextured State Batching Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce Colored Cubes PS2 draw submissions by batching opaque untextured materials that share GS blend/test state while retaining their per-material triangle colors.

**Architecture:** Keep the existing 64-source-triangle VIF packet ceiling because its packet-size calculation includes worst-case clipping expansion. Replace the untextured group builder's material-pointer identity check with an alpha-mode compatibility check; `AddOpaqueUntexturedBatches` already computes lighting and RGBAQ separately for every batch.

**Tech Stack:** C++20, PS2 gsKit/packet2 renderer, C# source-contract tests, Docker PS2 toolchain.

---

### Task 1: Establish the source contract

**Files:**
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`

- [ ] **Step 1: Write the failing source-contract test**

```csharp
Assert.Contains("AreUntexturedBatchStatesCompatible", source, StringComparison.Ordinal);
Assert.Contains("candidate.Material->GetAlphaMode() != material->GetAlphaMode()", source, StringComparison.Ordinal);
Assert.DoesNotContain("candidate.Material != material", untexturedGroupSource, StringComparison.Ordinal);
```

- [ ] **Step 2: Run the focused source test and verify it fails**

Run: `dotnet test builder.tests/helengine.ps2.builder.tests.csproj --filter FullyQualifiedName~Untextured`

Expected: failure because the compatibility predicate does not yet exist.

- [ ] **Step 3: Implement state-compatible untextured grouping**

```cpp
bool AreUntexturedBatchStatesCompatible(const Ps2VuOpaqueBatch& first, const Ps2VuOpaqueBatch& candidate) {
    return first.Material != nullptr
        && candidate.Material != nullptr
        && first.Material->GetAlphaMode() == candidate.Material->GetAlphaMode();
}
```

Replace the strict pointer comparison in `BuildCompatibleUntexturedGroups` with this predicate. Do not change `MaximumBoundedUntexturedAggregateSourceTriangleCount`.

- [ ] **Step 4: Run the focused source test and verify it passes**

Run: `dotnet test builder.tests/helengine.ps2.builder.tests.csproj --filter FullyQualifiedName~Untextured`

Expected: 0 failing tests.

### Task 2: Verify native integration and the real scene

**Files:**
- Modify: no additional source files

- [ ] **Step 1: Compile the changed renderer object through the PS2 toolchain**

Run: `docker run --rm ... helengine-ps2 make build/platform/ps2/rendering/Ps2RenderManager3D.o`

Expected: object compilation exits 0.

- [ ] **Step 2: Build and launch a full DemoDisc PS2 ISO**

Run: `C:/dev/helworks/helengine/scripts/build-platform.ps1 -Project C:/dev/helprojs/demodisc/project.heproj -Platform ps2 ...`

Expected: `ps2-build-phase.txt` contains `packaged outputs verified`, then launch its `game.iso` in PCSX2.

