# PS2 Flat-Color Diagnostic Renderer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one temporary PS2-only flat-color 3D diagnostic mode that removes textures, lighting, alpha branching, and HDR glow so the directional-shadow scene can be debugged as pure geometry.

**Architecture:** Keep the change entirely inside the PS2 renderer. Add one local diagnostic switch plus a stable per-proxy debug-color helper in the PS2 3D draw path, then force all submitted triangles through the untextured gouraud primitive with identical per-vertex colors. Validate the source-level behavior first, then rebuild the city PS2 ISO and inspect the scene.

**Tech Stack:** C++, gsKit, .NET test project, PS2 builder/export pipeline

---

### Task 1: Lock the Diagnostic Behavior with a Failing Source-Level Regression

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\Ps2NativeBuildInputsTests.cs`
- Test: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\helengine.ps2.builder.tests.csproj`

- [ ] **Step 1: Write the failing test**

Add this test to `Ps2NativeBuildInputsTests.cs`:

```csharp
    /// <summary>
    /// Ensures the PS2 renderer can force one flat-color diagnostic mode that bypasses textures, material alpha state, lighting, and HDR glow.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_supports_flat_color_diagnostic_submission_mode() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("constexpr bool EnableFlatColorDiagnostics = true;", source, StringComparison.Ordinal);
        Assert.Contains("ResolveDiagnosticProxyColor(proxy)", source, StringComparison.Ordinal);
        Assert.Contains("const bool useDiagnosticFlatColor = EnableFlatColorDiagnostics;", source, StringComparison.Ordinal);
        Assert.Contains("if (!useDiagnosticFlatColor) {", source, StringComparison.Ordinal);
        Assert.Contains("ApplyMaterialAlphaState(*material);", source, StringComparison.Ordinal);
        Assert.Contains("GSTEXTURE* texture = nullptr;", source, StringComparison.Ordinal);
        Assert.Contains("if (!useDiagnosticFlatColor && !material->GetTextureRelativePath().empty()) {", source, StringComparison.Ordinal);
        Assert.Contains("const std::uint64_t diagnosticColor = ResolveDiagnosticProxyColor(proxy);", source, StringComparison.Ordinal);
        Assert.Contains("const std::uint64_t colorA = useDiagnosticFlatColor ? diagnosticColor : ResolveVertexColor(*material, normalA);", source, StringComparison.Ordinal);
        Assert.Contains("const bool useTexture = !useDiagnosticFlatColor", source, StringComparison.Ordinal);
        Assert.Contains("if (!useDiagnosticFlatColor && !ShouldDrawAlphaTestTriangle(", source, StringComparison.Ordinal);
        Assert.Contains("if (!useDiagnosticFlatColor && HdrEnabled && ShouldEmitHdrGlow(*material, clippedColorA, clippedColorB, clippedColorC)) {", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_prim_triangle_gouraud_3d(", source, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
rtk dotnet test "C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\helengine.ps2.builder.tests.csproj" -c Debug -p:HelengineRoot="C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core" --filter FullyQualifiedName~Ps2_renderer3d_supports_flat_color_diagnostic_submission_mode
```

Expected: `FAIL` because the diagnostic switch and helper do not exist yet.

- [ ] **Step 3: Commit the failing test**

```powershell
rtk git -C "C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core" add -- "builder.tests/Ps2NativeBuildInputsTests.cs"
rtk git -C "C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core" commit -m "test: add ps2 flat-color diagnostic regression"
```

### Task 2: Implement the Flat-Color Diagnostic Render Path

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp`
- Test: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\Ps2NativeBuildInputsTests.cs`

- [ ] **Step 1: Add the diagnostic switch and stable color helper**

Inside the anonymous namespace near the other renderer constants, add:

```cpp
        constexpr bool EnableFlatColorDiagnostics = true;

        std::uint64_t ResolveDiagnosticProxyColor(const helengine::ps2::Ps2RenderProxy& proxy) {
            static constexpr std::uint64_t DiagnosticPalette[] = {
                GS_SETREG_RGBAQ(0xD0, 0x40, 0x40, 0x80, 0x00),
                GS_SETREG_RGBAQ(0x40, 0xD0, 0x40, 0x80, 0x00),
                GS_SETREG_RGBAQ(0x40, 0x70, 0xD0, 0x80, 0x00),
                GS_SETREG_RGBAQ(0xD0, 0xC0, 0x40, 0x80, 0x00),
                GS_SETREG_RGBAQ(0x40, 0xC0, 0xC0, 0x80, 0x00),
                GS_SETREG_RGBAQ(0xC0, 0x40, 0xC0, 0x80, 0x00),
                GS_SETREG_RGBAQ(0xD0, 0x80, 0x40, 0x80, 0x00),
                GS_SETREG_RGBAQ(0xD0, 0xD0, 0xD0, 0x80, 0x00)
            };

            ::IDrawable3D* drawable = proxy.GetDrawable();
            ::Entity* parent = drawable != nullptr ? drawable->get_Parent() : nullptr;
            std::string key = parent != nullptr ? parent->get_Id() : std::string();
            if (key.empty()) {
                key = parent != nullptr ? parent->get_Name() : std::string();
            }
            if (key.empty()) {
                key = "ps2-flat-color-diagnostic";
            }

            const std::size_t paletteIndex = std::hash<std::string>{}(key) % (sizeof(DiagnosticPalette) / sizeof(DiagnosticPalette[0]));
            return DiagnosticPalette[paletteIndex];
        }
```

- [ ] **Step 2: Force opaque untextured state in `DrawOpaqueProxy` when the switch is enabled**

Replace the start of `DrawOpaqueProxy(...)` setup with:

```cpp
        const bool useDiagnosticFlatColor = EnableFlatColorDiagnostics;
        if (!useDiagnosticFlatColor) {
            ApplyMaterialAlphaState(*material);
        } else {
            gsKit_set_test(GsGlobal, GS_ATEST_OFF);
            gsKit_set_primalpha(GsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
            GsGlobal->PrimAlphaEnable = GS_SETTING_OFF;
        }

        const bool doubleSided = material->GetDoubleSided();

        GSTEXTURE* texture = nullptr;
        if (!useDiagnosticFlatColor && !material->GetTextureRelativePath().empty()) {
            texture = ResolveTexture(GsGlobal, material->GetTextureRelativePath());
        }
```

- [ ] **Step 3: Force constant per-proxy colors and bypass alpha-test and glow**

Replace the color and per-triangle behavior with:

```cpp
            const std::uint64_t diagnosticColor = ResolveDiagnosticProxyColor(proxy);
            const std::uint64_t colorA = useDiagnosticFlatColor ? diagnosticColor : ResolveVertexColor(*material, normalA);
            const std::uint64_t colorB = useDiagnosticFlatColor ? diagnosticColor : ResolveVertexColor(*material, normalB);
            const std::uint64_t colorC = useDiagnosticFlatColor ? diagnosticColor : ResolveVertexColor(*material, normalC);
```

and:

```cpp
            const bool useTexture = !useDiagnosticFlatColor
                && texture != nullptr
                && indexA < texCoords.size()
                && indexB < texCoords.size()
                && indexC < texCoords.size();
```

and:

```cpp
                if (!useDiagnosticFlatColor && !ShouldDrawAlphaTestTriangle(
                        *material,
                        texture,
                        clippedA.TexCoord,
                        clippedB.TexCoord,
                        clippedC.TexCoord,
                        clippedA.Alpha,
                        clippedB.Alpha,
                        clippedC.Alpha)) {
                    continue;
                }
```

and:

```cpp
                if (!useDiagnosticFlatColor && HdrEnabled && ShouldEmitHdrGlow(*material, clippedColorA, clippedColorB, clippedColorC)) {
```

This keeps the same geometry path while removing material-dependent behavior.

- [ ] **Step 4: Run the focused regression to verify it passes**

Run:

```powershell
rtk dotnet test "C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\helengine.ps2.builder.tests.csproj" -c Debug -p:HelengineRoot="C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core" --filter FullyQualifiedName~Ps2_renderer3d_supports_flat_color_diagnostic_submission_mode
```

Expected: `PASS`

- [ ] **Step 5: Run the full PS2 builder suite**

Run:

```powershell
rtk dotnet test "C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\helengine.ps2.builder.tests.csproj" -c Debug -p:HelengineRoot="C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core"
```

Expected: `ok dotnet test:` with all tests passing.

- [ ] **Step 6: Commit the renderer change**

```powershell
rtk git -C "C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core" add -- "src/platform/ps2/rendering/Ps2RenderManager3D.cpp" "builder.tests/Ps2NativeBuildInputsTests.cs"
rtk git -C "C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core" commit -m "feat: add ps2 flat-color diagnostic renderer"
```

### Task 3: Export and Verify the Diagnostic ISO

**Files:**
- Use: `C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core\helengine.ui\helengine.editor.app\bin\Debug\net9.0-windows\helengine.editor.app.dll`
- Use: `C:\dev\helprojs\city\project.heproj`
- Output: `C:\dev\helprojs\output\ps2-flat-color-diagnostic`

- [ ] **Step 1: Build the PS2 ISO from the worktree editor binary**

Run:

```powershell
rtk proxy powershell.exe -NoProfile -Command "dotnet 'C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core\helengine.ui\helengine.editor.app\bin\Debug\net9.0-windows\helengine.editor.app.dll' --build ps2 --project 'C:\dev\helprojs\city\project.heproj' --output 'C:\dev\helprojs\output\ps2-flat-color-diagnostic'"
```

Expected:

```text
Build completed for platform 'ps2': C:\dev\helprojs\output\ps2-flat-color-diagnostic
```

- [ ] **Step 2: Verify the ISO artifact exists**

Run:

```powershell
rtk proxy powershell.exe -NoProfile -Command "Get-Item 'C:\dev\helprojs\output\ps2-flat-color-diagnostic\game.iso' | Select-Object FullName,Length,LastWriteTime"
```

Expected: one `game.iso` entry with a fresh timestamp.

- [ ] **Step 3: Boot-test the scene**

Manual verification:

1. Boot `C:\dev\helprojs\output\ps2-flat-color-diagnostic\game.iso`
2. Confirm the main menu still appears.
3. Open the directional-shadow scene.
4. Record whether:
   - geometry is now structurally correct with flat per-object colors
   - geometry is still stretched, offset, inside-out, or flickering
   - performance improves relative to the textured path

Expected diagnostic outcomes:

- If the scene becomes readable, the next fix should target textures, alpha, lighting, or glow.
- If the scene is still broken, the next fix should stay in geometry, projection, depth, or gsKit submission.

- [ ] **Step 4: Commit any last-mile diagnostic-only adjustments if needed**

If the export required no additional code changes, do not create a new commit. If a small follow-up tweak was required during export verification, commit it separately:

```powershell
rtk git -C "C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core" add --all
rtk git -C "C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core" commit -m "chore: finalize ps2 flat-color diagnostic export"
```
