# PS2 Generic Texture Format Capabilities Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the PS2 platform definition publish the same generic texture-format capability metadata contract PSP uses, while keeping PS2 builder-owned texture and font-atlas work-item execution on that generic contract and still producing `Ps2TextureAsset` runtime outputs.

**Architecture:** The change stays in the existing PS2 texture-cook slice inside `C:\dev\helworks\helengine-ps2\.worktrees\ps2-platform-owned-asset-cook-work-items`. First, tighten PS2 builder tests around generic capability metadata and generic default settings. Then replace the PS2-specific settings contract with the generic `TextureAssetProcessorSettings` contract already used by PSP, and update the PS2 cooker to validate and map the supported generic combination into the current runtime texture payload.

**Tech Stack:** C# 13 / .NET 9, xUnit, helengine baseplatform metadata contracts, PS2 builder/runtime asset pipeline.

---

## File Structure

### Files to Modify

- `C:\dev\helworks\helengine-ps2\.worktrees\ps2-platform-owned-asset-cook-work-items\builder\Ps2PlatformDefinitionFactory.cs`
  - Replace PS2-specific texture capability metadata with the generic texture-format capability surface.
  - Publish generic default serialized settings for `texture` and `font-atlas-texture`.
- `C:\dev\helworks\helengine-ps2\.worktrees\ps2-platform-owned-asset-cook-work-items\builder\Ps2TextureCookSettingsSerializer.cs`
  - Stop serializing PS2-specific `Format`/`AlphaMode`.
  - Serialize and deserialize the generic `TextureAssetProcessorSettings` contract.
- `C:\dev\helworks\helengine-ps2\.worktrees\ps2-platform-owned-asset-cook-work-items\builder\Ps2PlatformCookWorkItemExecutor.cs`
  - Keep work-item execution intact while consuming generic texture settings.
- `C:\dev\helworks\helengine-ps2\.worktrees\ps2-platform-owned-asset-cook-work-items\builder\Ps2RuntimeTextureCooker.cs`
  - Validate supported generic `ColorFormat` / `AlphaPrecision` combinations and map them into `Ps2TextureAsset`.
- `C:\dev\helworks\helengine-ps2\.worktrees\ps2-platform-owned-asset-cook-work-items\builder.tests\Ps2PlatformAssetBuilderTests.cs`
  - Add and update the PS2 builder tests for generic capability metadata and generic settings defaults.

### Files That May Be Simplified

- `C:\dev\helworks\helengine-ps2\.worktrees\ps2-platform-owned-asset-cook-work-items\builder\Ps2TextureCookSettings.cs`
  - Keep only if still needed as a wrapper over generic settings. If it becomes dead weight after the serializer/cooker change, delete it and use `TextureAssetProcessorSettings` directly.

### Test Commands

- `rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_texture_and_font_atlas_defaults|Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_generic_texture_format_metadata|BuildAsync_WhenTextureCookWorkItemUsesCapabilityDefaults_WritesPs2RuntimeTextureWithDefaultSettings|BuildAsync_WhenFontAtlasTextureCookWorkItemIsPresent_WritesPs2RuntimeTextureToDeclaredOutputPath" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine`

## Task 1: Lock the Generic Metadata Contract in Tests

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\ps2-platform-owned-asset-cook-work-items\builder.tests\Ps2PlatformAssetBuilderTests.cs`
- Reference: `C:\dev\helworks\helengine-psp\builder.tests\PspPlatformAssetBuilderTests.cs`

- [ ] **Step 1: Write the failing PS2 capability metadata test**

Add a new test next to the existing PS2 capability-default test:

```csharp
[Fact]
public void Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_generic_texture_format_metadata() {
    Ps2PlatformAssetBuilder builder = new();

    Assert.Collection(
        builder.Definition.AssetCookCapabilities.OrderBy(capability => capability.SourceAssetKind, StringComparer.Ordinal),
        capability => {
            Assert.Equal("font-atlas-texture", capability.SourceAssetKind);
            Assert.Equal("runtime-texture", capability.TargetArtifactKind);
            Assert.Equal(PlatformAssetCookOwnershipKind.BuilderOwned, capability.OwnershipKind);
            Assert.Equal("ps2-font-atlas-texture", capability.SettingsContractId);
            Assert.Equal("{\"maxResolution\":0,\"colorFormat\":\"Rgba32\",\"alphaPrecision\":\"A8\"}", capability.DefaultSerializedPlatformSettings);
            AssertPs2TextureFormatCapabilities(capability.TextureFormatCapabilities);
        },
        capability => {
            Assert.Equal("texture", capability.SourceAssetKind);
            Assert.Equal("runtime-texture", capability.TargetArtifactKind);
            Assert.Equal(PlatformAssetCookOwnershipKind.BuilderOwned, capability.OwnershipKind);
            Assert.Equal("ps2-texture", capability.SettingsContractId);
            Assert.Equal("{\"maxResolution\":0,\"colorFormat\":\"Rgba32\",\"alphaPrecision\":\"A8\"}", capability.DefaultSerializedPlatformSettings);
            AssertPs2TextureFormatCapabilities(capability.TextureFormatCapabilities);
        });
}
```

Add the helper in the same test file:

```csharp
static void AssertPs2TextureFormatCapabilities(PlatformTextureFormatCapabilityDefinition textureFormatCapabilities) {
    Assert.NotNull(textureFormatCapabilities);
    Assert.Equal(
        [TextureAssetColorFormat.Rgba32],
        textureFormatCapabilities.SupportedColorFormats);
    Assert.Equal(
        [TextureAssetAlphaPrecision.A8],
        textureFormatCapabilities.SupportedAlphaPrecisions);
    Assert.Collection(
        textureFormatCapabilities.SupportedCombinations,
        combination => {
            Assert.Equal(TextureAssetColorFormat.Rgba32, combination.ColorFormat);
            Assert.Equal(TextureAssetAlphaPrecision.A8, combination.AlphaPrecision);
        });
}
```

- [ ] **Step 2: Tighten the existing defaults test to the generic contract**

Replace the current capability-default assertions with:

```csharp
Assert.Equal("runtime-texture", fontAtlasCapability.TargetArtifactKind);
Assert.Equal(PlatformAssetCookOwnershipKind.BuilderOwned, fontAtlasCapability.OwnershipKind);
Assert.Equal("ps2-font-atlas-texture", fontAtlasCapability.SettingsContractId);
Assert.Equal("{\"maxResolution\":0,\"colorFormat\":\"Rgba32\",\"alphaPrecision\":\"A8\"}", fontAtlasCapability.DefaultSerializedPlatformSettings);

Assert.Equal("runtime-texture", textureCapability.TargetArtifactKind);
Assert.Equal(PlatformAssetCookOwnershipKind.BuilderOwned, textureCapability.OwnershipKind);
Assert.Equal("ps2-texture", textureCapability.SettingsContractId);
Assert.Equal("{\"maxResolution\":0,\"colorFormat\":\"Rgba32\",\"alphaPrecision\":\"A8\"}", textureCapability.DefaultSerializedPlatformSettings);
```

- [ ] **Step 3: Run the metadata tests to verify they fail for the right reason**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_texture_and_font_atlas_defaults|Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_generic_texture_format_metadata" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Expected:

```text
FAIL
Expected: runtime-texture / ps2-texture / ps2-font-atlas-texture / generic texture format capability metadata
Actual:   ps2-runtime-texture / ps2.texture-settings.v1 / null or PS2-specific defaults
```

- [ ] **Step 4: Commit the red test change**

```powershell
git add builder.tests\Ps2PlatformAssetBuilderTests.cs
git commit -m "test: lock PS2 generic texture capability metadata"
```

## Task 2: Publish Generic PS2 Texture Capability Metadata

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\ps2-platform-owned-asset-cook-work-items\builder\Ps2PlatformDefinitionFactory.cs`
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\ps2-platform-owned-asset-cook-work-items\builder\Ps2TextureCookSettingsSerializer.cs`

- [ ] **Step 1: Replace the PS2-specific capability constants with generic capability helpers**

In `Ps2PlatformDefinitionFactory.cs`, add these helpers near the top of the file:

```csharp
static string CreateDefaultSerializedTextureCookSettings() {
    return Ps2TextureCookSettingsSerializer.Serialize(new TextureAssetProcessorSettings {
        MaxResolution = 0,
        ColorFormat = TextureAssetColorFormat.Rgba32,
        AlphaPrecision = TextureAssetAlphaPrecision.A8
    });
}

static string CreateDefaultSerializedFontAtlasTextureCookSettings() {
    return Ps2TextureCookSettingsSerializer.Serialize(new TextureAssetProcessorSettings {
        MaxResolution = 0,
        ColorFormat = TextureAssetColorFormat.Rgba32,
        AlphaPrecision = TextureAssetAlphaPrecision.A8
    });
}

static PlatformTextureFormatCapabilityDefinition CreateTextureFormatCapabilities() {
    return new PlatformTextureFormatCapabilityDefinition(
        [TextureAssetColorFormat.Rgba32],
        [TextureAssetAlphaPrecision.A8],
        [new PlatformTextureFormatCombinationDefinition(TextureAssetColorFormat.Rgba32, TextureAssetAlphaPrecision.A8)]);
}
```

- [ ] **Step 2: Replace the existing PS2 capability entries**

In the `assetCookCapabilities:` block, replace the current entries with:

```csharp
assetCookCapabilities: [
    new PlatformAssetCookCapabilityDefinition(
        "texture",
        "runtime-texture",
        PlatformAssetCookOwnershipKind.BuilderOwned,
        "ps2-texture",
        CreateDefaultSerializedTextureCookSettings(),
        CreateTextureFormatCapabilities()),
    new PlatformAssetCookCapabilityDefinition(
        "font-atlas-texture",
        "runtime-texture",
        PlatformAssetCookOwnershipKind.BuilderOwned,
        "ps2-font-atlas-texture",
        CreateDefaultSerializedFontAtlasTextureCookSettings(),
        CreateTextureFormatCapabilities())
]);
```

- [ ] **Step 3: Move the serializer to the generic settings contract**

In `Ps2TextureCookSettingsSerializer.cs`, change the API shape to:

```csharp
public static class Ps2TextureCookSettingsSerializer {
    public static string Serialize(TextureAssetProcessorSettings settings) {
        if (settings == null) {
            throw new ArgumentNullException(nameof(settings));
        }

        return JsonSerializer.Serialize(settings);
    }

    public static TextureAssetProcessorSettings Deserialize(string serializedSettings) {
        if (string.IsNullOrWhiteSpace(serializedSettings)) {
            throw new ArgumentException("Serialized PS2 texture settings are required.", nameof(serializedSettings));
        }

        TextureAssetProcessorSettings settings = JsonSerializer.Deserialize<TextureAssetProcessorSettings>(serializedSettings);
        if (settings == null) {
            throw new InvalidOperationException("Serialized PS2 texture settings did not produce a payload.");
        }

        return settings;
    }
}
```

Delete `CreateDefault()` and `SettingsContractId`. This plan does not keep the PS2-specific serializer contract id.

- [ ] **Step 4: Run the metadata tests to verify they pass**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_texture_and_font_atlas_defaults|Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_generic_texture_format_metadata" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Expected:

```text
PASS
2 tests passed
```

- [ ] **Step 5: Commit the metadata implementation**

```powershell
git add builder\Ps2PlatformDefinitionFactory.cs builder\Ps2TextureCookSettingsSerializer.cs builder.tests\Ps2PlatformAssetBuilderTests.cs
git commit -m "feat: publish generic PS2 texture format capabilities"
```

## Task 3: Keep PS2 Work-Item Execution on the Generic Contract

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\ps2-platform-owned-asset-cook-work-items\builder\Ps2PlatformCookWorkItemExecutor.cs`
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\ps2-platform-owned-asset-cook-work-items\builder\Ps2RuntimeTextureCooker.cs`
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\ps2-platform-owned-asset-cook-work-items\builder.tests\Ps2PlatformAssetBuilderTests.cs`

- [ ] **Step 1: Update the builder execution tests to use generic settings payloads**

In `Ps2PlatformAssetBuilderTests.cs`, replace the PS2-specific settings setup in the texture work-item tests with:

```csharp
TextureAssetProcessorSettings settings = new() {
    MaxResolution = 32,
    ColorFormat = TextureAssetColorFormat.Rgba32,
    AlphaPrecision = TextureAssetAlphaPrecision.A8
};

PlatformBuildManifest manifest = CreateManifestWithTextureWorkItem(
    sourceTexturePath,
    "texture",
    outputRelativePath,
    "runtime-texture:logo",
    Ps2TextureCookSettingsSerializer.Serialize(settings));
```

Update the output assertions from:

```csharp
Assert.Equal(Ps2TextureFormat.Rgba32, textureAsset.Format);
Assert.Equal(Ps2TextureAlphaMode.Full, textureAsset.AlphaMode);
```

to the same runtime expectations, since the runtime output stays PS2-native:

```csharp
Assert.Equal(Ps2TextureFormat.Rgba32, textureAsset.Format);
Assert.Equal(Ps2TextureAlphaMode.Full, textureAsset.AlphaMode);
```

The runtime assertion does not change. Only the work-item settings payload becomes generic.

- [ ] **Step 2: Run the focused execution tests to verify they fail on the old execution path**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "BuildAsync_WhenTextureCookWorkItemUsesCapabilityDefaults_WritesPs2RuntimeTextureWithDefaultSettings|BuildAsync_WhenFontAtlasTextureCookWorkItemIsPresent_WritesPs2RuntimeTextureToDeclaredOutputPath" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Expected:

```text
FAIL
Deserialized settings did not populate the PS2-specific Format / AlphaMode fields
```

- [ ] **Step 3: Update the executor to consume generic texture settings**

In `Ps2PlatformCookWorkItemExecutor.cs`, change:

```csharp
Ps2TextureCookSettings settings = Ps2TextureCookSettingsSerializer.Deserialize(workItem.SerializedPlatformSettings);
Ps2TextureAsset cookedTexture = RuntimeTextureCooker.Cook(sourceTexture, settings);
```

to:

```csharp
TextureAssetProcessorSettings settings = Ps2TextureCookSettingsSerializer.Deserialize(workItem.SerializedPlatformSettings);
Ps2TextureAsset cookedTexture = RuntimeTextureCooker.Cook(sourceTexture, settings);
```

- [ ] **Step 4: Update the runtime cooker to validate the supported generic combination**

In `Ps2RuntimeTextureCooker.cs`, change the method signature and add explicit validation:

```csharp
public Ps2TextureAsset Cook(TextureAsset sourceTexture, TextureAssetProcessorSettings settings) {
    if (sourceTexture == null) {
        throw new ArgumentNullException(nameof(sourceTexture));
    }
    if (settings == null) {
        throw new ArgumentNullException(nameof(settings));
    }
    if (sourceTexture.Colors == null || sourceTexture.Colors.Length == 0) {
        throw new InvalidOperationException("Decoded source textures must contain pixel data.");
    }
    if (settings.ColorFormat != TextureAssetColorFormat.Rgba32 || settings.AlphaPrecision != TextureAssetAlphaPrecision.A8) {
        throw new InvalidOperationException(
            $"PS2 does not support texture settings '{settings.ColorFormat}' + '{settings.AlphaPrecision}' yet.");
    }

    return new Ps2TextureAsset {
        Width = sourceTexture.Width,
        Height = sourceTexture.Height,
        Format = Ps2TextureFormat.Rgba32,
        AlphaMode = Ps2TextureAlphaMode.Full,
        PixelData = [.. sourceTexture.Colors],
        PaletteData = sourceTexture.PaletteColors == null ? Array.Empty<byte>() : [.. sourceTexture.PaletteColors]
    };
}
```

- [ ] **Step 5: Run the full focused PS2 texture-capability slice**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_texture_and_font_atlas_defaults|Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_generic_texture_format_metadata|BuildAsync_WhenTextureCookWorkItemUsesCapabilityDefaults_WritesPs2RuntimeTextureWithDefaultSettings|BuildAsync_WhenFontAtlasTextureCookWorkItemIsPresent_WritesPs2RuntimeTextureToDeclaredOutputPath" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Expected:

```text
PASS
4 tests passed
```

- [ ] **Step 6: Commit the execution-contract update**

```powershell
git add builder\Ps2PlatformCookWorkItemExecutor.cs builder\Ps2RuntimeTextureCooker.cs builder.tests\Ps2PlatformAssetBuilderTests.cs
git commit -m "feat: use generic PS2 texture cook settings contract"
```

## Task 4: Final Verification and Cleanup

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\ps2-platform-owned-asset-cook-work-items\builder\Ps2TextureCookSettings.cs` if it is still present only for the old PS2-specific contract.

- [ ] **Step 1: Remove dead PS2-specific settings structure if no longer referenced**

If `Ps2TextureCookSettings` is no longer referenced after Task 3, delete the file:

```powershell
Remove-Item builder\Ps2TextureCookSettings.cs
```

If it still has a real use, keep it. Do not keep it as an unused compatibility shell.

- [ ] **Step 2: Run a reference search to verify the old PS2-specific contract is gone**

Run:

```powershell
rg -n "ps2-runtime-texture|ps2\\.texture-settings\\.v1|Ps2TextureCookSettings|Format = Ps2TextureFormat|AlphaMode = Ps2TextureAlphaMode" builder builder.tests
```

Expected:

```text
Only legitimate runtime-output assertions remain, and no builder metadata still depends on the old PS2-specific settings contract.
```

- [ ] **Step 3: Re-run the focused PS2 builder validation slice**

Run:

```powershell
rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter "Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_texture_and_font_atlas_defaults|Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_generic_texture_format_metadata|BuildAsync_WhenTextureCookWorkItemUsesCapabilityDefaults_WritesPs2RuntimeTextureWithDefaultSettings|BuildAsync_WhenFontAtlasTextureCookWorkItemIsPresent_WritesPs2RuntimeTextureToDeclaredOutputPath" -v minimal -p:HelengineRoot=C:\dev\helworks\helengine
```

Expected:

```text
PASS
Focused PS2 builder metadata and execution slice is green.
```

- [ ] **Step 4: Commit the cleanup and final verification state**

```powershell
git add builder\Ps2TextureCookSettings.cs builder\Ps2PlatformDefinitionFactory.cs builder\Ps2TextureCookSettingsSerializer.cs builder\Ps2PlatformCookWorkItemExecutor.cs builder\Ps2RuntimeTextureCooker.cs builder.tests\Ps2PlatformAssetBuilderTests.cs
git commit -m "chore: finalize PS2 generic texture capability contract"
```
