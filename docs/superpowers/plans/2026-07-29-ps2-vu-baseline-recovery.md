# PS2 VU Baseline Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the committed `29acaa0` VIF/VU renderer compile without changing its packet behavior.

**Architecture:** Align `Ps2VuVifPacketBuilder.hpp` with the already committed implementation and retain BootHost telemetry compatibility in the render manager. No draw-path branches, packet formats, or VU programs change.

**Tech Stack:** C++17, PS2SDK, .NET source-contract tests.

## Global Constraints

- Preserve the recovered `29acaa0` VIF/VU implementation exactly.
- Build outputs belong under `C:\dev\helworks\builds\demodisc\ps2`.
- Verify the header/implementation contract before packaging.

---

### Task 1: Restore the builder declaration contract

**Files:**
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp`

**Interfaces:**
- Consumes: `Ps2VuTexturedPacketCache` and `Ps2VuVifPacketBuilder::AddOpaqueTexturedVuBatches` implementation.
- Produces: a header declaring the implementation's `lightDirection` parameter, `DirectGifPacketWords`, and `TexturedPacketCache`.

- [ ] **Step 1: Write the failing test**

Assert the header includes `Ps2VuTexturedPacketCache.hpp`, accepts `const ::float3& lightDirection` in `AddOpaqueTexturedVuBatches`, and declares both required private members.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test builder.tests --filter "FullyQualifiedName~Ps2TexturedVuBuilder_WhenRecoveringCommittedBaseline_DeclaresItsPacketCacheContract" --no-restore`

Expected: FAIL because the header lacks the recovered contract.

- [ ] **Step 3: Write minimal implementation**

Include the packet-cache header, add the missing method parameter, and declare only the two members used by the committed `.cpp`.

- [ ] **Step 4: Run test to verify it passes**

Run the same filtered test. Expected: PASS.

### Task 2: Package the recovered baseline

**Files:**
- Modify: `src/platform/ps2/Ps2BootHost.cpp`

- [ ] **Step 1: Stamp B238 and build**

Set the overlay build number to `B238`, build into `C:\dev\helworks\builds\demodisc\ps2\B238-committed-fast-vif-vu-baseline`, and verify `game.iso` exists.
