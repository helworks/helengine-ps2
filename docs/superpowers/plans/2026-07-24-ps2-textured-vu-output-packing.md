# PS2 Textured VU Scratch-Buffer Isolation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the existing VU1 textured transform path without corrupting its double-buffered source payload, then use it only for fully visible batches.

**Architecture:** `InitializeVuOpaqueDoubleBuffer` alternates VIF input payloads between qwords `8…95` and `504…591`. The textured VU program currently writes its GIF output at qword `0x200` (`512`), overwriting the second input payload in place. Move only that GIF scratch region to qword `0x100` (`256`), which is outside both input ranges. CPU clipping remains the fallback; no changes are made to the established CPU textured renderer.

**Tech Stack:** PS2 VU1 assembly (`dvp-as`), PS2 VIF double buffering, C++20 native renderer, C# xUnit source-contract tests.

---

### Task 1: Protect the VU output scratch region

**Files:**
- Modify: `builder.tests/Ps2NativeBuildInputsTests.cs`
- Modify: `src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm`

- [ ] **Step 1: Write the failing source-contract test**

Add `Ps2_textured_vu_program_writes_gif_output_outside_double_buffered_inputs`. It must read `Ps2OpaqueTexturedDraw3D.vsm` and require:

```csharp
Assert.Contains("iaddiu VI03, VI00, 0x00000100", source, StringComparison.Ordinal);
Assert.Contains("iaddiu VI04, VI03, 0x00000000", source, StringComparison.Ordinal);
Assert.DoesNotContain("iaddiu VI03, VI00, 0x00000200", source, StringComparison.Ordinal);
```

The test documents that the output starts at qword `256`, safely between VIF input regions `8…95` and `504…591` configured by `InitializeVuOpaqueDoubleBuffer`.

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --no-restore --filter "FullyQualifiedName~Ps2_textured_vu_program_writes_gif_output_outside_double_buffered_inputs"
```

Expected: FAIL because the program still starts its output at `0x200`, overlapping the second VIF buffer.

- [ ] **Step 3: Move only the VU GIF output address**

In `Ps2OpaqueTexturedDraw3D.vsm`, replace:

```asm
NOP                                                        iaddiu VI03, VI00, 0x00000200
```

with:

```asm
NOP                                                        iaddiu VI03, VI00, 0x00000100
```

Do not alter matrix math, perspective-Q scheduling, GIF tags, vertex output lanes, or VIF source packing.

- [ ] **Step 4: Run the focused test and native assembler validation**

Run the focused test from Step 2, then a full PS2 build through the build waiter. Expected: test passes, `dvp-as` succeeds, and the waiter confirms a fresh ISO.

### Task 2: Prove one stable authored-texture VU triangle

**Files:**
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`
- Modify: `src/platform/ps2/Ps2BootHost.cpp`

- [ ] **Step 1: Write the failing routing test**

Add a focused source test requiring `StartupSceneDiagnosticOverrideId = "textured_cube_grid"` and retaining both dynamic VU limits at `1u`.

- [ ] **Step 2: Run the focused test and verify it fails**

Expected: FAIL while B88 still boots `colored_cube_grid`.

- [ ] **Step 3: Configure only the existing one-triangle diagnostic**

Set the startup override to `textured_cube_grid`, retain `DynamicTexturedVuDiagnosticBatchLimit = 1u` and `DynamicTexturedVuDiagnosticTriangleLimit = 1u`, and increment the build label.

- [ ] **Step 4: Build, launch, and validate**

Validate that the first visible authored-texture triangle is stable, finite, and perspective-correct. If it fails, capture and trace the VIF source address and VU output address before any further assembly edit.

### Task 3: Enable Colored Cubes only after the proof passes

**Files:**
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Test: `builder.tests/Ps2NativeBuildInputsTests.cs`

- [ ] **Step 1: Write an eligibility test**

Require runtime-white color-only batches to remain on the CPU path unless the entire batch passes `CanUseTexturedVuFastPath(...)`.

- [ ] **Step 2: Preserve material semantics before routing**

Do not enable runtime-white batches until the VU payload carries the same per-triangle lighting color produced by `ResolveTexturedVertexColor`. This is a separate behavior change with its own failing test and visual comparison against B88.

- [ ] **Step 3: Measure gradual scope increases**

After correctness is proven, increase one variable at a time: one VU triangle, one full cube, then all fully visible Colored Cubes. Retain CPU fallback for every clip-boundary batch and stop at the first visual regression.

