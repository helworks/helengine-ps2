# PS2 Tilt Play Perspective Texturing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove affine texture warping from the Tilt Play 01 textured cubes while retaining the stable CPU geometry path.

**Architecture:** The opaque CPU textured path will build gsKit `GSPRIMSTQPOINT` vertices. It will submit normalized `S * Q` and `T * Q`, where `Q` is the reciprocal of projection `clipW`; gsKit supplies the known-correct GIF packet state and coordinate conversion. The VU and HDR glow paths remain unchanged.

**Tech Stack:** C++17, PS2SDK, gsKit, xUnit source-contract tests, editor CLI PS2 ISO packaging, PCSX2.

---

## File structure

- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs` — assert the active CPU textured path emits STQ vertices rather than affine UVs.
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp` — create STQ vertices from the existing projected triangle and submit them through gsKit.

### Task 1: Lock the active textured path contract

**Files:**

- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Write the failing source-contract test**

```csharp
[Fact]
public void Ps2RenderManager3D_WhenDrawingOpaqueTexturedTriangles_UsesPerspectiveCorrectStqCoordinates() {
    string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
    string source = File.ReadAllText(sourcePath);

    Assert.Contains("ResolvePerspectiveTextureVertex(", source, StringComparison.Ordinal);
    Assert.Contains("vertex.stq = vertex_to_STQ(normalizedTexCoord.X * q, normalizedTexCoord.Y * q);", source, StringComparison.Ordinal);
    Assert.Contains("gsKit_prim_list_triangle_goraud_texture_stq_3d(", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the focused test to record RED**

Run: `dotnet test builder.tests/helengine.ps2.builder.tests.csproj --no-restore --filter Ps2RenderManager3D_WhenDrawingOpaqueTexturedTriangles_UsesPerspectiveCorrectStqCoordinates`

Expected: fail because the STQ helper and list submission do not exist. If the test project instead fails in the unrelated edited `Ps2PlatformAssetBuilderTests.cs`, record that baseline compiler failure before native validation.

### Task 2: Submit opaque textured triangles through STQ

**Files:**

- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp:348-356`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp:1676-1691`

- [ ] **Step 1: Add the minimal STQ vertex helper**

```cpp
float ResolveProjectionClipW(const ::float3& viewPosition, const ::float4x4& projection) {
    return (viewPosition.X * projection.M14)
        + (viewPosition.Y * projection.M24)
        + (viewPosition.Z * projection.M34)
        + projection.M44;
}

const float q = 1.0f / clipW;
vertex.rgbaq = rgba_to_RGBAQ(static_cast<std::uint32_t>(color), q);
vertex.stq = vertex_to_STQ(normalizedTexCoord.X * q, normalizedTexCoord.Y * q);
vertex.xyz2 = vertex_to_XYZ2(gsGlobal, screenX, screenY, static_cast<int>(screenZ));
```

- [ ] **Step 2: Replace only the active opaque textured primitive call**

```cpp
const GSPRIMSTQPOINT texturedVertices[] = {
    ResolvePerspectiveTextureVertex(GsGlobal, screenAX, screenAY, screenAZ, clippedA.TexCoord, ResolveProjectionClipW(clippedA.ViewPosition, projection), clippedColorA),
    ResolvePerspectiveTextureVertex(GsGlobal, screenBX, screenBY, screenBZ, clippedB.TexCoord, ResolveProjectionClipW(clippedB.ViewPosition, projection), clippedColorB),
    ResolvePerspectiveTextureVertex(GsGlobal, screenCX, screenCY, screenCZ, clippedC.TexCoord, ResolveProjectionClipW(clippedC.ViewPosition, projection), clippedColorC)
};
gsKit_prim_list_triangle_goraud_texture_stq_3d(GsGlobal, texture, 3, texturedVertices);
```

- [ ] **Step 3: Run focused source test and native build**

Run: `dotnet test builder.tests/helengine.ps2.builder.tests.csproj --no-restore --filter Ps2RenderManager3D_WhenDrawingOpaqueTexturedTriangles_UsesPerspectiveCorrectStqCoordinates`

Expected: source contract passes. The native build completes and packages a fresh `game.iso`.

- [ ] **Step 4: Launch and validate Tilt Play 01**

Run: `scripts/launch_in_emulator.ps1 -ArtifactPath <editor-output>\game.iso`

Expected: the scene boots in PCSX2; textured cubes remain visible and no longer exhibit affine face warping.

- [ ] **Step 5: Commit source and test only after validation**

Run: `git add builder.tests/Ps2RenderManager3DSourceTests.cs src/platform/ps2/rendering/Ps2RenderManager3D.cpp; git commit -m "Fix PS2 perspective texture mapping"`
