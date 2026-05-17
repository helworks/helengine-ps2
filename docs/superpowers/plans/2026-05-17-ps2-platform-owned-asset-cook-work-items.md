# PS2 Platform-Owned Asset Cook Work-Items Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the PS2 builder execute editor-emitted platform cook work items for `texture` and `font-atlas-texture`, produce PS2-native runtime texture payloads at the declared output paths, and make the PS2 runtime load those payloads directly.

**Architecture:** Publish PS2 builder-owned asset cook capabilities with PS2-specific serialized settings, add a PS2-native runtime texture asset contract, execute work items inside the PS2 builder instead of discovering assets by reference walking, and update PS2 runtime texture loading to consume the new PS2 asset type. Font atlas support will require evolving the packaged font contract so PS2 no longer depends on embedded generic raw `TextureAsset` atlas bytes.

**Tech Stack:** C#/.NET 9, helengine.baseplatform manifests/builders, helengine.files asset serialization, PS2 native runtime C++, xUnit.

---

## File Structure

### Existing files to modify

- `builder/Ps2PlatformDefinitionFactory.cs`
  Purpose: publish PS2 asset cook capabilities and default serialized settings.
- `builder/Ps2PlatformAssetBuilder.cs`
  Purpose: orchestrate staging, work-item execution, packaging, and diagnostics.
- `builder/helengine.ps2.builder.csproj`
  Purpose: add any required shared-project references for the new texture contract or source decode utilities.
- `builder.tests/Ps2PlatformAssetBuilderTests.cs`
  Purpose: focused builder tests for capability metadata and work-item execution.
- `builder.tests/helengine.ps2.builder.tests.csproj`
  Purpose: add editor project reference if needed for the editor-owned end-to-end verification.
- `src/platform/ps2/Ps2BootHost.cpp`
  Purpose: load PS2-native runtime texture assets for 2D/font usage instead of assuming generic `TextureAsset`.
- `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
  Purpose: load PS2-native runtime texture assets for 3D materials instead of assuming generic `TextureAsset`.
- `builder.tests/Ps2BootHostSourceTests.cs`
  Purpose: lock the 2D/font runtime loader onto the PS2 texture contract.
- `builder.tests/Ps2RenderManager3DSourceTests.cs`
  Purpose: lock the 3D runtime loader onto the PS2 texture contract.
- `../helengine/engine/helengine.files/assets/font/FontAssetBinarySerializer.cs`
  Purpose: packaged font serializer; currently embeds generic raw atlas texture bytes.
- `../helengine/engine/helengine.core/assets/font/FontAssetBinarySerializer.cs`
  Purpose: runtime packaged-font deserializer; currently rebuilds the atlas through `BuildTextureFromRaw(TextureAsset)`.
- `../helengine/engine/helengine.core/assets/font/FontAsset.cs`
  Purpose: runtime font asset contract that still exposes `SourceTextureAsset`.

### New files to create

- `builder/Ps2TextureCookSettings.cs`
  Purpose: builder/runtime serialized PS2 texture settings contract consumed from `PlatformCookWorkItem.SerializedPlatformSettings`.
- `builder/Ps2TextureCookSettingsSerializer.cs`
  Purpose: stable serializer/deserializer for PS2 texture settings defaults and work-item payloads.
- `builder/Ps2SourceTextureDecoder.cs`
  Purpose: builder-local source image decode service for declared texture source files.
- `builder/Ps2RuntimeTextureCooker.cs`
  Purpose: turn decoded source images plus PS2 settings into serialized PS2 runtime texture assets.
- `builder/Ps2PlatformCookWorkItemExecutor.cs`
  Purpose: execute supported PS2 work-item kinds and write outputs into `ps2-staging`.
- `../helengine/engine/helengine.core/assets/raw/ps2/Ps2TextureAsset.cs`
  Purpose: PS2-native runtime texture payload contract consumed by the runtime.
- `../helengine/engine/helengine.core/assets/raw/ps2/Ps2TextureFormat.cs`
  Purpose: enumerates PS2-native runtime texture formats.
- `../helengine/engine/helengine.core/assets/raw/ps2/Ps2TextureAlphaMode.cs`
  Purpose: enumerates PS2-native alpha/storage behavior used by the PS2 runtime.

## Task 1: Publish PS2 Builder-Owned Texture Capabilities And Contracts

**Files:**
- Create: `builder/Ps2TextureCookSettings.cs`
- Create: `builder/Ps2TextureCookSettingsSerializer.cs`
- Create: `../helengine/engine/helengine.core/assets/raw/ps2/Ps2TextureAsset.cs`
- Create: `../helengine/engine/helengine.core/assets/raw/ps2/Ps2TextureFormat.cs`
- Create: `../helengine/engine/helengine.core/assets/raw/ps2/Ps2TextureAlphaMode.cs`
- Modify: `builder/Ps2PlatformDefinitionFactory.cs`
- Test: `builder.tests/Ps2PlatformAssetBuilderTests.cs`

- [ ] **Step 1: Write the failing capability metadata test**

Add a test named:

```csharp
[Fact]
public void Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_texture_and_font_atlas_defaults() {
    Ps2PlatformAssetBuilder builder = new();

    Assert.Collection(
        builder.Definition.AssetCookCapabilities.OrderBy(capability => capability.SourceAssetKind),
        fontAtlas => {
            Assert.Equal("font-atlas-texture", fontAtlas.SourceAssetKind);
            Assert.Equal("ps2-runtime-texture", fontAtlas.TargetArtifactKind);
            Assert.Equal(PlatformAssetCookOwnershipKind.BuilderOwned, fontAtlas.OwnershipKind);
            Assert.Equal("ps2.texture-settings.v1", fontAtlas.SettingsContractId);
            Assert.False(string.IsNullOrWhiteSpace(fontAtlas.DefaultSerializedPlatformSettings));
        },
        texture => {
            Assert.Equal("texture", texture.SourceAssetKind);
            Assert.Equal("ps2-runtime-texture", texture.TargetArtifactKind);
            Assert.Equal(PlatformAssetCookOwnershipKind.BuilderOwned, texture.OwnershipKind);
            Assert.Equal("ps2.texture-settings.v1", texture.SettingsContractId);
            Assert.False(string.IsNullOrWhiteSpace(texture.DefaultSerializedPlatformSettings));
        });
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_texture_and_font_atlas_defaults" -v minimal
```

Expected: FAIL because `AssetCookCapabilities` is currently empty or missing the new PS2 entries.

- [ ] **Step 3: Add the shared PS2 texture settings/runtime contracts**

Create:

- `Ps2TextureCookSettings` with fields for `MaxResolution`, `Format`, and `AlphaMode`
- `Ps2TextureFormat` enum with the first supported PS2-native runtime formats
- `Ps2TextureAlphaMode` enum with the first supported PS2 alpha behaviors
- `Ps2TextureAsset` with the PS2 runtime fields the native loaders need
- `Ps2TextureCookSettingsSerializer` that produces the stable serialized capability default payload and parses work-item settings payloads

Keep the contract minimal. Do not carry over generic `TextureAssetColorFormat` semantics into the PS2 runtime asset.

- [ ] **Step 4: Publish the PS2 capabilities in `Ps2PlatformDefinitionFactory`**

Add `AssetCookCapabilities` for:

- `texture`
- `font-atlas-texture`

Each capability must use:

- `TargetArtifactKind = "ps2-runtime-texture"`
- `OwnershipKind = PlatformAssetCookOwnershipKind.BuilderOwned`
- `SettingsContractId = "ps2.texture-settings.v1"`
- serialized defaults produced by `Ps2TextureCookSettingsSerializer`

- [ ] **Step 5: Run the capability test to verify it passes**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_texture_and_font_atlas_defaults" -v minimal
```

Expected: PASS

- [ ] **Step 6: Commit**

```powershell
rtk git add builder/Ps2PlatformDefinitionFactory.cs builder/Ps2TextureCookSettings.cs builder/Ps2TextureCookSettingsSerializer.cs ..\helengine\engine\helengine.core\assets\raw\ps2\Ps2TextureAsset.cs ..\helengine\engine\helengine.core\assets\raw\ps2\Ps2TextureFormat.cs ..\helengine\engine\helengine.core\assets\raw\ps2\Ps2TextureAlphaMode.cs builder.tests/Ps2PlatformAssetBuilderTests.cs
rtk git commit -m "Add PS2 texture cook capabilities and contracts"
```

## Task 2: Execute `texture` Work Items In The PS2 Builder

**Files:**
- Create: `builder/Ps2SourceTextureDecoder.cs`
- Create: `builder/Ps2RuntimeTextureCooker.cs`
- Create: `builder/Ps2PlatformCookWorkItemExecutor.cs`
- Modify: `builder/Ps2PlatformAssetBuilder.cs`
- Modify: `builder/helengine.ps2.builder.csproj`
- Test: `builder.tests/Ps2PlatformAssetBuilderTests.cs`

- [ ] **Step 1: Write the failing texture work-item execution tests**

Add tests named:

```csharp
[Fact]
public async Task BuildAsync_WhenTextureCookWorkItemIsPresent_WritesPs2RuntimeTextureToDeclaredOutputPath()
```

and

```csharp
[Fact]
public async Task BuildAsync_WhenTextureCookWorkItemUsesCapabilityDefaults_WritesPs2RuntimeTextureWithDefaultSettings()
```

Each test should:

- create a temp source texture file
- build a `PlatformBuildManifest` containing a `PlatformCookWorkItem`
- call `BuildAsync(...)`
- assert the staged output file exists under `ps2-staging\<OutputRelativePath>`
- deserialize the output and assert it is a `Ps2TextureAsset`

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "BuildAsync_WhenTextureCookWorkItemIsPresent_WritesPs2RuntimeTextureToDeclaredOutputPath|BuildAsync_WhenTextureCookWorkItemUsesCapabilityDefaults_WritesPs2RuntimeTextureWithDefaultSettings" -v minimal
```

Expected: FAIL because `BuildAsync(...)` does not execute `PlatformCookWorkItems`.

- [ ] **Step 3: Add the PS2 work-item executor and texture cooker**

Implement:

- `Ps2SourceTextureDecoder` to decode declared source image files for the supported PS2 texture source formats
- `Ps2RuntimeTextureCooker` to convert decoded pixels plus `Ps2TextureCookSettings` into a serialized `Ps2TextureAsset`
- `Ps2PlatformCookWorkItemExecutor` to:
  - validate `SourceAssetPath`, `SourceAssetKind`, `OutputRelativePath`, and `SerializedPlatformSettings`
  - handle `texture`
  - write the final PS2 payload into `ps2-staging`

Keep discovery out of this layer. The executor must only do the work described by the manifest work item.

- [ ] **Step 4: Wire the executor into `Ps2PlatformAssetBuilder.BuildAsync(...)`**

Update `BuildAsync(...)` so the builder:

- validates the request
- stages editor-owned cooked artifacts
- executes PS2 platform cook work items
- continues into path rewriting, native build, disc layout, and ISO packaging

Do not mutate generic staged cooked texture blobs after staging.

- [ ] **Step 5: Run the focused texture tests to verify they pass**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "BuildAsync_WhenTextureCookWorkItemIsPresent_WritesPs2RuntimeTextureToDeclaredOutputPath|BuildAsync_WhenTextureCookWorkItemUsesCapabilityDefaults_WritesPs2RuntimeTextureWithDefaultSettings" -v minimal
```

Expected: PASS

- [ ] **Step 6: Commit**

```powershell
rtk git add builder/Ps2SourceTextureDecoder.cs builder/Ps2RuntimeTextureCooker.cs builder/Ps2PlatformCookWorkItemExecutor.cs builder/Ps2PlatformAssetBuilder.cs builder/helengine.ps2.builder.csproj builder.tests/Ps2PlatformAssetBuilderTests.cs
rtk git commit -m "Execute PS2 texture cook work items in builder"
```

## Task 3: Move Font Atlas Cooking Off Embedded Generic `TextureAsset`

**Files:**
- Modify: `../helengine/engine/helengine.core/assets/font/FontAsset.cs`
- Modify: `../helengine/engine/helengine.files/assets/font/FontAssetBinarySerializer.cs`
- Modify: `../helengine/engine/helengine.core/assets/font/FontAssetBinarySerializer.cs`
- Modify: `builder/Ps2PlatformCookWorkItemExecutor.cs`
- Modify: `builder.tests/Ps2PlatformAssetBuilderTests.cs`
- Test: `../helengine/engine/helengine.editor.tests/serialization/FontAssetBinarySerializerTests.cs`

- [ ] **Step 1: Write the failing font-atlas contract tests**

Add one test in `FontAssetBinarySerializerTests.cs` named:

```csharp
[Fact]
public void Serialize_when_external_atlas_runtime_path_is_present_writes_font_without_embedded_source_texture_bytes()
```

Add one PS2 builder test named:

```csharp
[Fact]
public async Task BuildAsync_WhenFontAtlasTextureCookWorkItemIsPresent_WritesPs2RuntimeAtlasTextureToDeclaredOutputPath()
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```powershell
rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "Serialize_when_external_atlas_runtime_path_is_present_writes_font_without_embedded_source_texture_bytes" -v minimal
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "BuildAsync_WhenFontAtlasTextureCookWorkItemIsPresent_WritesPs2RuntimeAtlasTextureToDeclaredOutputPath" -v minimal
```

Expected: FAIL because packaged fonts still embed `SourceTextureAsset` and the PS2 builder does not execute `font-atlas-texture`.

- [ ] **Step 3: Evolve the packaged font contract**

Update the packaged font serializers so a font can carry an external cooked atlas path for platform-owned runtime textures. The runtime payload must still preserve glyph metrics, line height, and atlas dimensions, but the PS2 path must no longer require embedded generic raw `TextureAsset` bytes.

Keep the old embedded-atlas path working for non-PS2 payloads unless a narrower existing test surface proves it is safe to remove.

- [ ] **Step 4: Implement `font-atlas-texture` in the PS2 work-item executor**

Handle `font-atlas-texture` exactly like `texture`, but target the atlas output path declared by the work item. The font asset should then point at that cooked PS2 atlas path rather than embedding raw atlas bytes for PS2.

- [ ] **Step 5: Run the focused font tests to verify they pass**

Run:

```powershell
rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "Serialize_when_external_atlas_runtime_path_is_present_writes_font_without_embedded_source_texture_bytes" -v minimal
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "BuildAsync_WhenFontAtlasTextureCookWorkItemIsPresent_WritesPs2RuntimeAtlasTextureToDeclaredOutputPath" -v minimal
```

Expected: PASS

- [ ] **Step 6: Commit**

```powershell
rtk git add ..\helengine\engine\helengine.core\assets\font\FontAsset.cs ..\helengine\engine\helengine.files\assets\font\FontAssetBinarySerializer.cs ..\helengine\engine\helengine.core\assets\font\FontAssetBinarySerializer.cs builder/Ps2PlatformCookWorkItemExecutor.cs builder.tests/Ps2PlatformAssetBuilderTests.cs ..\helengine\engine\helengine.editor.tests\serialization\FontAssetBinarySerializerTests.cs
rtk git commit -m "Move PS2 font atlas cooking to work-item owned textures"
```

## Task 4: Update PS2 Runtime Loaders To Consume `Ps2TextureAsset`

**Files:**
- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Test: `builder.tests/Ps2BootHostSourceTests.cs`
- Test: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Write the failing runtime source-contract tests**

Add tests named:

```csharp
[Fact]
public void Ps2BootHost_WhenLoadingPackagedFontAtlases_UsesPs2TextureAssetRuntimePath()
```

and

```csharp
[Fact]
public void Ps2RenderManager3D_WhenResolvingMaterialTextures_LoadsPs2TextureAssetInsteadOfTextureAsset()
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "Ps2BootHost_WhenLoadingPackagedFontAtlases_UsesPs2TextureAssetRuntimePath|Ps2RenderManager3D_WhenResolvingMaterialTextures_LoadsPs2TextureAssetInsteadOfTextureAsset" -v minimal
```

Expected: FAIL because both loaders still assume generic `TextureAsset`.

- [ ] **Step 3: Implement the runtime loader swap**

Update:

- `Ps2BootHost.cpp` so packaged-font atlas loading resolves the external PS2 texture payload and uploads that asset type
- `Ps2RenderManager3D.cpp` so material texture loading deserializes `Ps2TextureAsset` and uploads that payload

Keep `BuildTextureFromRaw(TextureAsset*)` only for editor/runtime raw paths that are still intentionally generic.

- [ ] **Step 4: Run the focused runtime source tests to verify they pass**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "Ps2BootHost_WhenLoadingPackagedFontAtlases_UsesPs2TextureAssetRuntimePath|Ps2RenderManager3D_WhenResolvingMaterialTextures_LoadsPs2TextureAssetInsteadOfTextureAsset" -v minimal
```

Expected: PASS

- [ ] **Step 5: Commit**

```powershell
rtk git add src/platform/ps2/Ps2BootHost.cpp src/platform/ps2/rendering/Ps2RenderManager3D.cpp builder.tests/Ps2BootHostSourceTests.cs builder.tests/Ps2RenderManager3DSourceTests.cs
rtk git commit -m "Load PS2 native runtime textures in PS2 runtime"
```

## Task 5: Add One Editor-Owned End-To-End Verification

**Files:**
- Modify: `builder.tests/helengine.ps2.builder.tests.csproj`
- Modify: `builder.tests/Ps2PlatformAssetBuilderTests.cs`

- [ ] **Step 1: Write the failing end-to-end verification**

Add a test named:

```csharp
[Fact]
public async Task BuildAsync_WhenManifestComesFromEditorCookService_ConsumesPs2TextureWorkItemsAndPackagesPs2NativeOutputs()
```

The test should:

- create a temp project root with one texture asset and one font asset
- invoke `EditorPlatformAssetCookService.Cook(...)` with a real `Ps2PlatformAssetBuilder`
- assert the manifest includes PS2 `PlatformCookWorkItems`
- invoke `BuildAsync(...)`
- assert the packaged texture/font atlas outputs at the declared relative paths deserialize as `Ps2TextureAsset`

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "BuildAsync_WhenManifestComesFromEditorCookService_ConsumesPs2TextureWorkItemsAndPackagesPs2NativeOutputs" -v minimal
```

Expected: FAIL because either the test project cannot yet access the editor cook service or the build path is still not fully PS2-owned end to end.

- [ ] **Step 3: Add the editor test-project reference and minimal test scaffolding support**

If needed, add:

- `ProjectReference` from `builder.tests/helengine.ps2.builder.tests.csproj` to `$(HelengineRoot)\engine\helengine.editor\helengine.editor.csproj`

Keep the test narrow. Do not pull unrelated editor UI systems into it.

- [ ] **Step 4: Make the end-to-end verification pass**

Tighten any remaining packaging or manifest assumptions so the full path is true:

- editor emits work items
- builder executes them
- packaged output is a PS2-native texture artifact
- runtime-facing packaged paths point to that artifact

- [ ] **Step 5: Run the full focused verification slice**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_texture_and_font_atlas_defaults|BuildAsync_WhenTextureCookWorkItemIsPresent_WritesPs2RuntimeTextureToDeclaredOutputPath|BuildAsync_WhenTextureCookWorkItemUsesCapabilityDefaults_WritesPs2RuntimeTextureWithDefaultSettings|BuildAsync_WhenFontAtlasTextureCookWorkItemIsPresent_WritesPs2RuntimeAtlasTextureToDeclaredOutputPath|Ps2BootHost_WhenLoadingPackagedFontAtlases_UsesPs2TextureAssetRuntimePath|Ps2RenderManager3D_WhenResolvingMaterialTextures_LoadsPs2TextureAssetInsteadOfTextureAsset|BuildAsync_WhenManifestComesFromEditorCookService_ConsumesPs2TextureWorkItemsAndPackagesPs2NativeOutputs" -v minimal
rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "Serialize_when_external_atlas_runtime_path_is_present_writes_font_without_embedded_source_texture_bytes" -v minimal
```

Expected: PASS

- [ ] **Step 6: Commit**

```powershell
rtk git add builder.tests/helengine.ps2.builder.tests.csproj builder.tests/Ps2PlatformAssetBuilderTests.cs
rtk git commit -m "Add end-to-end verification for PS2 platform cook work items"
```

## Final Verification

- [ ] Run the smallest complete validation set before claiming success:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_texture_and_font_atlas_defaults|BuildAsync_WhenTextureCookWorkItemIsPresent_WritesPs2RuntimeTextureToDeclaredOutputPath|BuildAsync_WhenTextureCookWorkItemUsesCapabilityDefaults_WritesPs2RuntimeTextureWithDefaultSettings|BuildAsync_WhenFontAtlasTextureCookWorkItemIsPresent_WritesPs2RuntimeAtlasTextureToDeclaredOutputPath|Ps2BootHost_WhenLoadingPackagedFontAtlases_UsesPs2TextureAssetRuntimePath|Ps2RenderManager3D_WhenResolvingMaterialTextures_LoadsPs2TextureAssetInsteadOfTextureAsset|BuildAsync_WhenManifestComesFromEditorCookService_ConsumesPs2TextureWorkItemsAndPackagesPs2NativeOutputs" -v minimal
rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "Serialize_when_external_atlas_runtime_path_is_present_writes_font_without_embedded_source_texture_bytes" -v minimal
```

- [ ] If the runtime loader changes require a source-contract guard beyond the focused tests above, add it before finishing.
