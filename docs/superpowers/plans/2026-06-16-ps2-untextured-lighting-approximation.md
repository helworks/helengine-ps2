# PS2 Untextured Lighting Approximation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce the PS2 untextured VU lighting hotspot by replacing the per-triangle specular `std::pow` path with a cheaper approximation, while preserving the current diffuse-lit look and avoiding a `Set` regression.

**Architecture:** Keep the current untextured VU packet contract, keep diffuse lighting unchanged, and change only the specular term inside the PS2 flat-color resolver. The approximation should use a small repeated-squaring based curve selector instead of `std::pow`, so the work stays in the hot loop but becomes materially cheaper.

**Tech Stack:** C++20 PS2 runtime, xUnit source-shape tests, native PS2 toolchain in Docker, PCSX2 runtime verification.

---

### Task 1: Lock The New Lighting Contract With A Failing Source Test

**Files:**
- Modify: `builder.tests/Ps2VuUntexturedPathSourceTests.cs`
- Test: `builder.tests/helengine.ps2.builder.tests.csproj`

- [ ] **Step 1: Write the failing source test for the new hot-loop contract**

Add one new `[Fact]` in `Ps2VuUntexturedPathSourceTests` that:
- extracts the body of `ResolveTexturedVertexColor(const Ps2VuLightingConstants& ...)`
- asserts the body does **not** contain `std::pow(`
- asserts the file contains a helper such as `ResolveApproximateSpecularFactor(`
- asserts the resolver uses that helper instead of the old `pow`-based line

- [ ] **Step 2: Run the focused test to verify it fails for the right reason**

Run:

```powershell
rtk proxy powershell.exe -NoProfile -Command "dotnet test 'builder.tests\helengine.ps2.builder.tests.csproj' --no-restore --filter 'FullyQualifiedName~Ps2VuUntexturedPathSourceTests' -p:BaseIntermediateOutputPath='artifacts\plan-ps2-lighting\obj\' -p:OutputPath='artifacts\plan-ps2-lighting\bin\' -v minimal 2>&1 | Select-Object -First 220"
```

Expected:
- the new source test fails because `ResolveTexturedVertexColor(const Ps2VuLightingConstants& ...)` still contains `std::pow(`

- [ ] **Step 3: Commit the failing-test checkpoint**

```bash
git add builder.tests/Ps2VuUntexturedPathSourceTests.cs
git commit -m "test: lock ps2 untextured lighting approximation contract"
```

### Task 2: Replace The Expensive Specular Term With A Cheap Approximation

**Files:**
- Modify: `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`
- Test: `builder.tests/helengine.ps2.builder.tests.csproj`

- [ ] **Step 1: Add one helper for cheap PS2 specular approximation**

In `Ps2VuVifPacketBuilder.cpp`, add one anonymous-namespace helper:

```cpp
double ResolveApproximateSpecularFactor(const Ps2VuLightingConstants& lightingConstants, double ndotl);
```

Implement it with repeated squaring only:
- compute `ndotl2`, `ndotl4`, `ndotl8`, and `ndotl16`
- select a curve bucket from roughness-derived sharpness:
  - rougher materials use a broader curve like `ndotl4`
  - mid materials use `ndotl8`
  - glossier materials use `ndotl16`
- return the selected factor multiplied by `lightingConstants.SpecularScale`

Do **not** use:
- `std::pow`
- lookup tables
- new fields on `Ps2VuLightingConstants`
- any precompute that runs before `triangleSetupEndTicks`

- [ ] **Step 2: Swap the hot resolver to the helper**

In `ResolveTexturedVertexColor(const Ps2VuLightingConstants& ...)`:
- keep the existing `ndotl`
- keep the existing diffuse and emissive terms
- replace only:

```cpp
const double specularBoost = std::pow(ndotl, lightingConstants.SpecularPower) * lightingConstants.SpecularScale;
```

with:

```cpp
const double specularBoost = ResolveApproximateSpecularFactor(lightingConstants, ndotl);
```

- [ ] **Step 3: Keep the rest of the flat-color path unchanged**

Do not change:
- `PopulateLightingConstants(...)`
- `LastTriangleLightingMilliseconds` timing placement
- payload fill logic
- packet assembly logic
- render-manager overlay wiring

- [ ] **Step 4: Run the focused source test to verify it passes**

Run:

```powershell
rtk proxy powershell.exe -NoProfile -Command "dotnet test 'builder.tests\helengine.ps2.builder.tests.csproj' --no-restore --filter 'FullyQualifiedName~Ps2VuUntexturedPathSourceTests' -p:BaseIntermediateOutputPath='artifacts\plan-ps2-lighting\obj\' -p:OutputPath='artifacts\plan-ps2-lighting\bin\' -v minimal 2>&1 | Select-Object -First 220"
```

Expected:
- the new source test passes
- the existing untextured-path source tests still pass

- [ ] **Step 5: Commit the implementation checkpoint**

```bash
git add src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp builder.tests/Ps2VuUntexturedPathSourceTests.cs
git commit -m "perf: approximate ps2 untextured specular lighting"
```

### Task 3: Rebuild The Native Runtime With The Known-Good Generated Core

**Files:**
- Rebuild: `build/helengine_ps2.elf`
- Input snapshot: `tmp/run42-generated-core`

- [ ] **Step 1: Rebuild the native ELF against the matching generated-core snapshot**

Run:

```powershell
rtk proxy docker run --rm -v C:\dev\helworks\helengine-ps2:/workspace -w /workspace -e HELENGINE_CORE_CPP_ROOT=/workspace/tmp/run42-generated-core helengine-ps2-inline sh -lc "make >/tmp/ps2-lighting-approx.log 2>&1; status=$?; tail -c 3000 /tmp/ps2-lighting-approx.log; exit $status"
```

Expected:
- `Ps2VuVifPacketBuilder.cpp` recompiles
- link completes
- strip completes

- [ ] **Step 2: Stage the rebuilt ELF into the trusted export root**

Run:

```powershell
rtk proxy powershell.exe -NoProfile -Command "Copy-Item 'build\helengine_ps2.elf' 'C:\tmp\city-ps2-fresh-export-20260616\disc\HELENGIN.ELF' -Force; Get-Item 'build\helengine_ps2.elf','C:\tmp\city-ps2-fresh-export-20260616\disc\HELENGIN.ELF' | Format-List FullName,LastWriteTimeUtc,Length"
```

Expected:
- the staged disc ELF matches the rebuilt ELF timestamp and size

- [ ] **Step 3: Repack the ISO**

Run:

```powershell
rtk proxy docker run --rm -v C:\tmp\city-ps2-fresh-export-20260616:/export -w /export helengine-ps2-inline xorriso -as mkisofs -iso-level 2 -V HELENGINE_PS2 -o /export/game.iso /export/disc 2>&1 | Select-Object -First 120
```

Expected:
- image writing completes successfully

### Task 4: Verify Runtime On Colored Cubes

**Files:**
- Runtime image: `C:\tmp\city-ps2-fresh-export-20260616\game.iso`
- Scene: `Colored Cubes`

- [ ] **Step 1: Launch PCSX2 with the exact working absolute-ISO form**

Run:

```powershell
rtk proxy powershell.exe -NoProfile -Command "Get-Process pcsx2-qt -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Seconds 2; if (Test-Path 'tmp\pcsx2-launcher') { Remove-Item 'tmp\pcsx2-launcher' -Recurse -Force -ErrorAction SilentlyContinue }; New-Item -ItemType Directory -Force -Path 'tmp\pcsx2-launcher' | Out-Null; Start-Process -FilePath 'C:\Program Files\PCSX2\pcsx2-qt.exe' -ArgumentList '-fastboot','-logfile','C:\dev\helworks\helengine-ps2\tmp\pcsx2-launcher\pcsx2-emulog.txt','--','C:\tmp\city-ps2-fresh-export-20260616\game.iso' -WorkingDirectory 'C:\Program Files\PCSX2' -PassThru | Select-Object Id,ProcessName"
```

Expected:
- PCSX2 starts
- no `Failed to open CDVD 'game.iso'` message appears in the fresh log

- [ ] **Step 2: Confirm the image really booted**

Run:

```powershell
rtk proxy powershell.exe -NoProfile -Command "Start-Sleep -Seconds 3; Get-Content 'tmp\pcsx2-launcher\pcsx2-emulog.txt' -Tail 60"
```

Expected:
- log shows ELF loading or execution
- log does not show the stale relative-ISO permission error

- [ ] **Step 3: Measure Colored Cubes**

Manual check in PCSX2:
- open `Colored Cubes`
- record `Upd` / `Rdr`
- especially record:
  - `Set`
  - `Emit`
  - `Tpl`
  - `Wt`

Success target:
- `Tpl` drops materially below the current `~9.9`
- `Set` stays near the sane baseline rather than exploding
- no crash or black screen
- visual lighting still looks acceptably lit

- [ ] **Step 4: Decide whether the approximation is acceptable**

Accept the change only if all of these are true:
- `Tpl` improves materially
- `Set` does not regress badly
- scene still looks acceptably lit
- runtime stays stable

If any of those fail:
- revert only the approximation helper change
- keep the profiling split and runtime baseline intact

- [ ] **Step 5: Commit only if runtime validation is good**

```bash
git add src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp builder.tests/Ps2VuUntexturedPathSourceTests.cs
git commit -m "perf: cheapen ps2 untextured specular lighting"
```
