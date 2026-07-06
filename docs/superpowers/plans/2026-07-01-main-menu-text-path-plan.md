# Main Menu Text Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the per-frame unwrapped-text copy from the shared 2D command builder so the PS2 main menu can idle without leaking through the hottest text path.

**Architecture:** Guard the allocation-sensitive path with a source-level test, then split wrapped and unwrapped text emission so only wrapped text materializes a temporary string. Validate with the narrow editor test first, then rebuild and relaunch the PS2 build for an on-device soak check.

**Tech Stack:** C#, xUnit, generated-core C++ output, PS2 build pipeline

---

### Task 1: Guard and Patch the 2D Text Builder

**Files:**
- Create: `C:\dev\helworks\helengine\engine\helengine.editor.tests\managers\rendering\RenderCommandListBuilder2DSourceTests.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.core\managers\rendering\RenderCommandListBuilder2D.cs`

- [ ] **Step 1: Write the failing source-level test**
- [ ] **Step 2: Run the test to verify the old allocation pattern still fails**
- [ ] **Step 3: Update the unwrapped text path to iterate authored text directly**
- [ ] **Step 4: Run the targeted tests to verify they pass**
- [ ] **Step 5: Build and relaunch the PS2 main-menu boot for soak validation**
