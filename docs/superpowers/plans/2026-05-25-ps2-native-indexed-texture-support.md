# PS2 Native Indexed Texture Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add PS2-native cooked texture support for `Rgba32`, `Indexed8`, and `Indexed4`, so the PS2 runtime consumes payload-defined PSM/CLUT state instead of hardcoded `GS_PSM_CT32`.

**Architecture:** Narrow the PS2-advertised texture formats to the generic formats the GS path truly supports, extend `Ps2TextureAsset` so cooked payloads carry native texture metadata, then update the builder cooker and both native loaders to use that metadata directly. Keep the editor contract generic and strict: the editor only emits published formats, and the builder/runtime hard-fail on malformed unpublished formats.

**Tech Stack:** C#, xUnit, native C++, gsKit PS2 texture upload path, existing PS2 asset serialization.

---

### Task 1: Narrow PS2 Texture Capability Publication

**Files:**
- Modify: `builder/Ps2PlatformDefinitionFactory.cs`
- Modify: `builder.tests/Ps2PlatformAssetBuilderTests.cs`

- [ ] **Step 1: Write the failing capability test**

Add this assertion update in `builder.tests/Ps2PlatformAssetBuilderTests.cs` inside `AssertPs2TextureFormatCapabilities(...)`:

```csharp
static void AssertPs2TextureFormatCapabilities(PlatformTextureFormatCapabilityDefinition textureFormatCapabilities) {
    Assert.NotNull(textureFormatCapabilities);
    Assert.Equal(
        new[] {
            TextureAssetColorFormat.Rgba32.ToString(),
            TextureAssetColorFormat.Indexed4.ToString(),
            TextureAssetColorFormat.Indexed8.ToString()
        },
        textureFormatCapabilities.SupportedColorFormatIds);
    Assert.Equal(
        new[] { TextureAssetAlphaPrecision.A8 },
        textureFormatCapabilities.SupportedAlphaPrecisions);
    Assert.Collection(
        textureFormatCapabilities.SupportedCombinations.OrderBy(combination => combination.ColorFormatId, StringComparer.Ordinal),
        combination => {
            Assert.Equal(TextureAssetColorFormat.Indexed4.ToString(), combination.ColorFormatId);
            Assert.Equal(TextureAssetAlphaPrecision.A8, combination.AlphaPrecision);
        },
        combination => {
            Assert.Equal(TextureAssetColorFormat.Indexed8.ToString(), combination.ColorFormatId);
            Assert.Equal(TextureAssetAlphaPrecision.A8, combination.AlphaPrecision);
        },
        combination => {
            Assert.Equal(TextureAssetColorFormat.Rgba32.ToString(), combination.ColorFormatId);
            Assert.Equal(TextureAssetAlphaPrecision.A8, combination.AlphaPrecision);
        });
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --no-restore --filter FullyQualifiedName~Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_generic_texture_format_metadata -m:1 -nr:false -p:UseSharedCompilation=false -v minimal
```

Expected: FAIL because `CreateTextureFormatCapabilities()` still publishes only `Rgba32`.

- [ ] **Step 3: Implement the minimal capability change**

Update `builder/Ps2PlatformDefinitionFactory.cs`:

```csharp
static PlatformTextureFormatCapabilityDefinition CreateTextureFormatCapabilities() {
    return new PlatformTextureFormatCapabilityDefinition(
        [
            TextureAssetColorFormat.Rgba32.ToString(),
            TextureAssetColorFormat.Indexed4.ToString(),
            TextureAssetColorFormat.Indexed8.ToString()
        ],
        [
            TextureAssetAlphaPrecision.A8
        ],
        [
            new PlatformTextureFormatCombinationDefinition(TextureAssetColorFormat.Rgba32.ToString(), TextureAssetAlphaPrecision.A8),
            new PlatformTextureFormatCombinationDefinition(TextureAssetColorFormat.Indexed4.ToString(), TextureAssetAlphaPrecision.A8),
            new PlatformTextureFormatCombinationDefinition(TextureAssetColorFormat.Indexed8.ToString(), TextureAssetAlphaPrecision.A8)
        ]);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:

```powershell
rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --no-restore --filter FullyQualifiedName~Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_generic_texture_format_metadata -m:1 -nr:false -p:UseSharedCompilation=false -v minimal
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add builder/Ps2PlatformDefinitionFactory.cs builder.tests/Ps2PlatformAssetBuilderTests.cs
git commit -m "Publish indexed PS2 texture formats"
```

### Task 2: Extend the Managed PS2 Texture Payload Contract

**Files:**
- Modify: `managed/helengine.ps2/Assets/Ps2TextureFormat.cs`
- Create: `managed/helengine.ps2/Assets/Ps2TexturePixelStorageMode.cs`
- Modify: `managed/helengine.ps2/Assets/Ps2TextureAsset.cs`
- Modify: `managed/helengine.ps2/Serialization/Ps2AssetBinarySerializer.cs`
- Create: `builder.tests/Ps2TextureAssetSerializationTests.cs`

- [ ] **Step 1: Write the failing round-trip serialization test**

Create `builder.tests/Ps2TextureAssetSerializationTests.cs`:

```csharp
using helengine;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies PS2 texture payloads round-trip native texture metadata through the managed PS2 asset serializer.
/// </summary>
public sealed class Ps2TextureAssetSerializationTests {
    /// <summary>
    /// Ensures indexed PS2 texture payload metadata survives one serializer round trip.
    /// </summary>
    [Fact]
    public void SerializeAndDeserialize_WhenTextureUsesIndexed8_RoundTripsPixelStorageAndClutMetadata() {
        Ps2TextureAsset asset = new() {
            Id = "runtime-texture:test",
            Width = 16,
            Height = 16,
            Format = Ps2TextureFormat.Indexed8,
            PixelStorageMode = Ps2TexturePixelStorageMode.PsmT8,
            ClutPixelStorageMode = Ps2TexturePixelStorageMode.PsmCt32,
            AlphaMode = Ps2TextureAlphaMode.Full,
            PixelData = new byte[256],
            PaletteData = new byte[16 * 4]
        };

        byte[] bytes = Ps2AssetSerializer.SerializeToBytes(asset);
        Ps2TextureAsset roundTripped = Assert.IsType<Ps2TextureAsset>(Ps2AssetSerializer.Deserialize(new MemoryStream(bytes)));

        Assert.Equal(Ps2TextureFormat.Indexed8, roundTripped.Format);
        Assert.Equal(Ps2TexturePixelStorageMode.PsmT8, roundTripped.PixelStorageMode);
        Assert.Equal(Ps2TexturePixelStorageMode.PsmCt32, roundTripped.ClutPixelStorageMode);
        Assert.Equal(256, roundTripped.PixelData.Length);
        Assert.Equal(16 * 4, roundTripped.PaletteData.Length);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --no-restore --filter FullyQualifiedName~SerializeAndDeserialize_WhenTextureUsesIndexed8_RoundTripsPixelStorageAndClutMetadata -m:1 -nr:false -p:UseSharedCompilation=false -v minimal
```

Expected: FAIL because the new enum values and metadata fields do not exist yet.

- [ ] **Step 3: Add the new managed payload types and serializer fields**

Create `managed/helengine.ps2/Assets/Ps2TexturePixelStorageMode.cs`:

```csharp
namespace helengine {
    /// <summary>
    /// Identifies the GS pixel storage mode stored in one cooked PS2 texture payload.
    /// </summary>
    public enum Ps2TexturePixelStorageMode : byte {
        /// <summary>
        /// Stores one 32-bit direct-color texture payload.
        /// </summary>
        PsmCt32 = 0,

        /// <summary>
        /// Stores one 8-bit indexed texture payload.
        /// </summary>
        PsmT8 = 1,

        /// <summary>
        /// Stores one 4-bit indexed texture payload.
        /// </summary>
        PsmT4 = 2
    }
}
```

Update `managed/helengine.ps2/Assets/Ps2TextureFormat.cs`:

```csharp
public enum Ps2TextureFormat : byte {
    Rgba32 = 0,
    Indexed8 = 1,
    Indexed4 = 2
}
```

Update `managed/helengine.ps2/Assets/Ps2TextureAsset.cs`:

```csharp
public class Ps2TextureAsset : Asset {
    public ushort Width;
    public ushort Height;
    public Ps2TextureFormat Format;
    public Ps2TexturePixelStorageMode PixelStorageMode;
    public Ps2TexturePixelStorageMode ClutPixelStorageMode;
    public Ps2TextureAlphaMode AlphaMode;
    public byte[] PixelData;
    public byte[] PaletteData;
}
```

Update `managed/helengine.ps2/Serialization/Ps2AssetBinarySerializer.cs`:

```csharp
static void WritePs2TextureAsset(EngineBinaryWriter writer, Ps2TextureAsset asset) {
    WriteAssetIdentity(writer, asset);
    writer.WriteUInt16(asset.Width);
    writer.WriteUInt16(asset.Height);
    writer.WriteByte((byte)asset.Format);
    writer.WriteByte((byte)asset.PixelStorageMode);
    writer.WriteByte((byte)asset.ClutPixelStorageMode);
    writer.WriteByte((byte)asset.AlphaMode);
    writer.WriteByteArray(asset.PixelData);
    writer.WriteByteArray(asset.PaletteData);
}

static Ps2TextureAsset ReadPs2TextureAsset(EngineBinaryReader reader, byte version) {
    Ps2TextureAsset asset = new Ps2TextureAsset();
    ReadAssetIdentity(reader, asset, version);
    asset.Width = reader.ReadUInt16();
    asset.Height = reader.ReadUInt16();
    asset.Format = (Ps2TextureFormat)reader.ReadByte();
    asset.PixelStorageMode = (Ps2TexturePixelStorageMode)reader.ReadByte();
    asset.ClutPixelStorageMode = (Ps2TexturePixelStorageMode)reader.ReadByte();
    asset.AlphaMode = (Ps2TextureAlphaMode)reader.ReadByte();
    asset.PixelData = reader.ReadByteArray();
    asset.PaletteData = reader.ReadByteArray();
    return asset;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:

```powershell
rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --no-restore --filter FullyQualifiedName~SerializeAndDeserialize_WhenTextureUsesIndexed8_RoundTripsPixelStorageAndClutMetadata -m:1 -nr:false -p:UseSharedCompilation=false -v minimal
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add managed/helengine.ps2/Assets/Ps2TextureFormat.cs managed/helengine.ps2/Assets/Ps2TexturePixelStorageMode.cs managed/helengine.ps2/Assets/Ps2TextureAsset.cs managed/helengine.ps2/Serialization/Ps2AssetBinarySerializer.cs builder.tests/Ps2TextureAssetSerializationTests.cs
git commit -m "Extend PS2 texture payload metadata"
```

### Task 3: Add Indexed Texture Cooking

**Files:**
- Modify: `builder/Ps2RuntimeTextureCooker.cs`
- Modify: `builder.tests/Ps2RuntimeTextureCookerTests.cs`

- [ ] **Step 1: Write the failing cooker tests**

Add these tests to `builder.tests/Ps2RuntimeTextureCookerTests.cs`:

```csharp
[Fact]
public void Cook_WhenSettingsUseIndexed8_ProducesIndexed8Ps2TextureAsset() {
    Ps2RuntimeTextureCooker cooker = new();
    TextureAsset sourceTexture = CreatePaletteFriendlyTextureAsset();
    TextureAssetProcessorSettings settings = new() {
        MaxResolution = 0,
        ColorFormat = TextureAssetColorFormat.Indexed8,
        AlphaPrecision = TextureAssetAlphaPrecision.A8
    };

    Ps2TextureAsset cookedTexture = cooker.Cook(sourceTexture, settings);

    Assert.Equal(Ps2TextureFormat.Indexed8, cookedTexture.Format);
    Assert.Equal(Ps2TexturePixelStorageMode.PsmT8, cookedTexture.PixelStorageMode);
    Assert.Equal(Ps2TexturePixelStorageMode.PsmCt32, cookedTexture.ClutPixelStorageMode);
    Assert.NotEmpty(cookedTexture.PixelData);
    Assert.NotEmpty(cookedTexture.PaletteData);
}

[Fact]
public void Cook_WhenSettingsUseIndexed4_ProducesIndexed4Ps2TextureAsset() {
    Ps2RuntimeTextureCooker cooker = new();
    TextureAsset sourceTexture = CreateSmallFourColorTextureAsset();
    TextureAssetProcessorSettings settings = new() {
        MaxResolution = 0,
        ColorFormat = TextureAssetColorFormat.Indexed4,
        AlphaPrecision = TextureAssetAlphaPrecision.A8
    };

    Ps2TextureAsset cookedTexture = cooker.Cook(sourceTexture, settings);

    Assert.Equal(Ps2TextureFormat.Indexed4, cookedTexture.Format);
    Assert.Equal(Ps2TexturePixelStorageMode.PsmT4, cookedTexture.PixelStorageMode);
    Assert.Equal(Ps2TexturePixelStorageMode.PsmCt32, cookedTexture.ClutPixelStorageMode);
    Assert.NotEmpty(cookedTexture.PixelData);
    Assert.NotEmpty(cookedTexture.PaletteData);
}

static TextureAsset CreatePaletteFriendlyTextureAsset() {
    return new TextureAsset {
        Width = 16,
        Height = 16,
        ColorFormat = TextureAssetColorFormat.Rgba32,
        AlphaPrecision = TextureAssetAlphaPrecision.A8,
        Colors = BuildTwoColorCheckerboard()
    };
}

static TextureAsset CreateSmallFourColorTextureAsset() {
    return new TextureAsset {
        Width = 8,
        Height = 8,
        ColorFormat = TextureAssetColorFormat.Rgba32,
        AlphaPrecision = TextureAssetAlphaPrecision.A8,
        Colors = BuildFourColorBlocks()
    };
}

static byte[] BuildTwoColorCheckerboard() {
    byte[] colors = new byte[16 * 16 * 4];
    for (int y = 0; y < 16; y++) {
        for (int x = 0; x < 16; x++) {
            int colorIndex = ((y * 16) + x) * 4;
            bool white = ((x + y) & 1) == 0;
            colors[colorIndex] = white ? (byte)255 : (byte)0;
            colors[colorIndex + 1] = white ? (byte)255 : (byte)0;
            colors[colorIndex + 2] = white ? (byte)255 : (byte)0;
            colors[colorIndex + 3] = 255;
        }
    }

    return colors;
}

static byte[] BuildFourColorBlocks() {
    byte[] colors = new byte[8 * 8 * 4];
    for (int y = 0; y < 8; y++) {
        for (int x = 0; x < 8; x++) {
            int colorIndex = ((y * 8) + x) * 4;
            bool right = x >= 4;
            bool bottom = y >= 4;
            colors[colorIndex] = right ? (byte)255 : (byte)0;
            colors[colorIndex + 1] = bottom ? (byte)255 : (byte)0;
            colors[colorIndex + 2] = (!right && !bottom) ? (byte)255 : (byte)0;
            colors[colorIndex + 3] = 255;
        }
    }

    return colors;
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```powershell
rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --no-restore --filter FullyQualifiedName~Ps2RuntimeTextureCookerTests -m:1 -nr:false -p:UseSharedCompilation=false -v minimal
```

Expected: FAIL because `Ps2RuntimeTextureCooker` currently only accepts `Rgba32`.

- [ ] **Step 3: Implement the indexed mappings**

Update `builder/Ps2RuntimeTextureCooker.cs`:

```csharp
public Ps2TextureAsset Cook(TextureAsset sourceTexture, TextureAssetProcessorSettings settings) {
    if (sourceTexture == null) {
        throw new ArgumentNullException(nameof(sourceTexture));
    }
    if (settings == null) {
        throw new ArgumentNullException(nameof(settings));
    }

    TextureAsset processedTexture = TextureAssetProcessor.Apply(sourceTexture, settings);
    if (processedTexture.Colors == null || processedTexture.Colors.Length == 0) {
        throw new InvalidOperationException("Decoded source textures must contain pixel data.");
    }

    if (processedTexture.ColorFormat == TextureAssetColorFormat.Rgba32 && processedTexture.AlphaPrecision == TextureAssetAlphaPrecision.A8) {
        return new Ps2TextureAsset {
            Width = processedTexture.Width,
            Height = processedTexture.Height,
            Format = Ps2TextureFormat.Rgba32,
            PixelStorageMode = Ps2TexturePixelStorageMode.PsmCt32,
            ClutPixelStorageMode = Ps2TexturePixelStorageMode.PsmCt32,
            AlphaMode = Ps2TextureAlphaMode.Full,
            PixelData = [.. processedTexture.Colors],
            PaletteData = Array.Empty<byte>()
        };
    }

    if (processedTexture.ColorFormat == TextureAssetColorFormat.Indexed8 && processedTexture.AlphaPrecision == TextureAssetAlphaPrecision.A8) {
        return new Ps2TextureAsset {
            Width = processedTexture.Width,
            Height = processedTexture.Height,
            Format = Ps2TextureFormat.Indexed8,
            PixelStorageMode = Ps2TexturePixelStorageMode.PsmT8,
            ClutPixelStorageMode = Ps2TexturePixelStorageMode.PsmCt32,
            AlphaMode = Ps2TextureAlphaMode.Full,
            PixelData = [.. processedTexture.Colors],
            PaletteData = processedTexture.PaletteColors == null ? Array.Empty<byte>() : [.. processedTexture.PaletteColors]
        };
    }

    if (processedTexture.ColorFormat == TextureAssetColorFormat.Indexed4 && processedTexture.AlphaPrecision == TextureAssetAlphaPrecision.A8) {
        return new Ps2TextureAsset {
            Width = processedTexture.Width,
            Height = processedTexture.Height,
            Format = Ps2TextureFormat.Indexed4,
            PixelStorageMode = Ps2TexturePixelStorageMode.PsmT4,
            ClutPixelStorageMode = Ps2TexturePixelStorageMode.PsmCt32,
            AlphaMode = Ps2TextureAlphaMode.Full,
            PixelData = [.. processedTexture.Colors],
            PaletteData = processedTexture.PaletteColors == null ? Array.Empty<byte>() : [.. processedTexture.PaletteColors]
        };
    }

    throw new InvalidOperationException(
        $"PS2 does not support texture settings '{processedTexture.ColorFormat}' + '{processedTexture.AlphaPrecision}'.");
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:

```powershell
rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --no-restore --filter FullyQualifiedName~Ps2RuntimeTextureCookerTests -m:1 -nr:false -p:UseSharedCompilation=false -v minimal
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add builder/Ps2RuntimeTextureCooker.cs builder.tests/Ps2RuntimeTextureCookerTests.cs
git commit -m "Cook indexed PS2 texture payloads"
```

### Task 4: Update 2D Runtime Loading to Use Payload PSM and CLUT

**Files:**
- Modify: `src/platform/ps2/Ps2BootHost.cpp`
- Modify: `builder.tests/Ps2BootHostSourceTests.cs`

- [ ] **Step 1: Write the failing 2D source-contract test**

Add this test to `builder.tests/Ps2BootHostSourceTests.cs`:

```csharp
[Fact]
public void Ps2BootHost_WhenLoadingPs2Textures_UsesPayloadDefinedPsmAndClutState() {
    string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
    string source = File.ReadAllText(sourcePath);

    Assert.Contains("record.Texture.PSM = ResolveGsPixelStorageMode(data->PixelStorageMode);", source, StringComparison.Ordinal);
    Assert.Contains("if (data->PaletteData != nullptr && data->PaletteData->Length > 0)", source, StringComparison.Ordinal);
    Assert.Contains("record.Texture.VramClut = gsKit_vram_alloc(", source, StringComparison.Ordinal);
    Assert.DoesNotContain("if (data->Format != ::Ps2TextureFormat::Rgba32)", source, StringComparison.Ordinal);
    Assert.DoesNotContain("record.Texture.PSM = GS_PSM_CT32;", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --no-restore --filter FullyQualifiedName~Ps2BootHost_WhenLoadingPs2Textures_UsesPayloadDefinedPsmAndClutState -m:1 -nr:false -p:UseSharedCompilation=false -v minimal
```

Expected: FAIL because the loader still hardcodes `Rgba32` and `GS_PSM_CT32`.

- [ ] **Step 3: Implement the 2D runtime mapping**

Update `src/platform/ps2/Ps2BootHost.cpp` around `PopulateTextureRecordFromPs2TextureAsset(...)` and upload helpers:

```cpp
static int ResolveGsPixelStorageMode(::Ps2TexturePixelStorageMode mode) {
    if (mode == ::Ps2TexturePixelStorageMode::PsmCt32) {
        return GS_PSM_CT32;
    } else if (mode == ::Ps2TexturePixelStorageMode::PsmT8) {
        return GS_PSM_T8;
    } else if (mode == ::Ps2TexturePixelStorageMode::PsmT4) {
        return GS_PSM_T4;
    }

    throw std::runtime_error("Unsupported PS2 texture pixel storage mode.");
}

bool PopulateTextureRecordFromPs2TextureAsset(::Ps2TextureAsset* data, Ps2TextureRecord& record) {
    if (data == nullptr || data->PixelData == nullptr || data->PixelData->Length <= 0 || data->Width <= 0 || data->Height <= 0) {
        return false;
    }

    record.Texture.Width = data->Width;
    record.Texture.Height = data->Height;
    record.Texture.PSM = ResolveGsPixelStorageMode(data->PixelStorageMode);
    record.Texture.Clut = nullptr;
    record.Texture.VramClut = 0;
    record.Texture.Filter = GS_FILTER_NEAREST;
    record.Texture.Mem = static_cast<u32*>(memalign(128, static_cast<size_t>(data->PixelData->Length)));
    if (record.Texture.Mem == nullptr) {
        return false;
    }

    std::memcpy(record.Texture.Mem, data->PixelData->Data, static_cast<size_t>(data->PixelData->Length));
    record.Pixels = record.Texture.Mem;

    if (data->PaletteData != nullptr && data->PaletteData->Length > 0) {
        record.Texture.Clut = static_cast<u32*>(memalign(128, static_cast<size_t>(data->PaletteData->Length)));
        if (record.Texture.Clut == nullptr) {
            return false;
        }

        std::memcpy(record.Texture.Clut, data->PaletteData->Data, static_cast<size_t>(data->PaletteData->Length));
    }

    return true;
}

bool EnsureTextureUploaded(Ps2TextureRecord& record) {
    if (record.Uploaded) {
        return true;
    }

    record.Texture.Vram = gsKit_vram_alloc(
        ActiveGsGlobal,
        gsKit_texture_size(record.Texture.Width, record.Texture.Height, record.Texture.PSM),
        GSKIT_ALLOC_USERBUFFER);
    if (record.Texture.Vram == GSKIT_ALLOC_ERROR) {
        return false;
    }

    if (record.Texture.Clut != nullptr) {
        record.Texture.VramClut = gsKit_vram_alloc(
            ActiveGsGlobal,
            gsKit_texture_size(16, 16, GS_PSM_CT32),
            GSKIT_ALLOC_USERBUFFER);
        if (record.Texture.VramClut == GSKIT_ALLOC_ERROR) {
            return false;
        }
    }

    gsKit_texture_upload(ActiveGsGlobal, &record.Texture);
    record.Uploaded = true;
    return true;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:

```powershell
rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --no-restore --filter FullyQualifiedName~Ps2BootHost_WhenLoadingPs2Textures_UsesPayloadDefinedPsmAndClutState -m:1 -nr:false -p:UseSharedCompilation=false -v minimal
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/platform/ps2/Ps2BootHost.cpp builder.tests/Ps2BootHostSourceTests.cs
git commit -m "Load indexed PS2 textures in 2D runtime"
```

### Task 5: Update 3D Runtime Loading to Use Payload PSM and CLUT

**Files:**
- Modify: `src/platform/ps2/rendering/Ps2RenderManager3D.cpp`
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Write the failing 3D source-contract test**

Add this test to `builder.tests/Ps2RenderManager3DSourceTests.cs`:

```csharp
[Fact]
public void Ps2RenderManager3D_WhenLoadingPs2Textures_UsesPayloadDefinedPsmAndClutState() {
    string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
    string source = File.ReadAllText(sourcePath);

    Assert.Contains("texture->PSM = ResolveGsPixelStorageMode(data->PixelStorageMode);", source, StringComparison.Ordinal);
    Assert.Contains("texture->Clut = static_cast<u32*>(memalign(128, static_cast<std::size_t>(data->PaletteData->Length)));", source, StringComparison.Ordinal);
    Assert.DoesNotContain("if (data->Format != ::Ps2TextureFormat::Rgba32)", source, StringComparison.Ordinal);
    Assert.DoesNotContain("texture->PSM = GS_PSM_CT32;", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --no-restore --filter FullyQualifiedName~Ps2RenderManager3D_WhenLoadingPs2Textures_UsesPayloadDefinedPsmAndClutState -m:1 -nr:false -p:UseSharedCompilation=false -v minimal
```

Expected: FAIL because the 3D loader still hardcodes `GS_PSM_CT32`.

- [ ] **Step 3: Implement the 3D runtime mapping**

Update `src/platform/ps2/rendering/Ps2RenderManager3D.cpp` inside `BuildTextureFromAsset(...)`:

```cpp
static int ResolveGsPixelStorageMode(::Ps2TexturePixelStorageMode mode) {
    if (mode == ::Ps2TexturePixelStorageMode::PsmCt32) {
        return GS_PSM_CT32;
    } else if (mode == ::Ps2TexturePixelStorageMode::PsmT8) {
        return GS_PSM_T8;
    } else if (mode == ::Ps2TexturePixelStorageMode::PsmT4) {
        return GS_PSM_T4;
    }

    throw std::runtime_error("Unsupported PS2 texture pixel storage mode.");
}

GSTEXTURE* BuildTextureFromAsset(GSGLOBAL* gsGlobal, ::Ps2TextureAsset* data) {
    if (gsGlobal == nullptr || data == nullptr || data->PixelData == nullptr || data->PixelData->Length <= 0 || data->Width <= 0 || data->Height <= 0) {
        return nullptr;
    }

    GSTEXTURE* texture = new GSTEXTURE();
    texture->Width = data->Width;
    texture->Height = data->Height;
    texture->PSM = ResolveGsPixelStorageMode(data->PixelStorageMode);
    texture->Clut = nullptr;
    texture->VramClut = 0;
    texture->Filter = GS_FILTER_NEAREST;
    texture->Mem = static_cast<u32*>(memalign(128, static_cast<std::size_t>(data->PixelData->Length)));
    if (texture->Mem == nullptr) {
        delete texture;
        return nullptr;
    }

    std::memcpy(texture->Mem, data->PixelData->Data, static_cast<std::size_t>(data->PixelData->Length));
    if (data->PaletteData != nullptr && data->PaletteData->Length > 0) {
        texture->Clut = static_cast<u32*>(memalign(128, static_cast<std::size_t>(data->PaletteData->Length)));
        if (texture->Clut == nullptr) {
            free(texture->Mem);
            delete texture;
            return nullptr;
        }

        std::memcpy(texture->Clut, data->PaletteData->Data, static_cast<std::size_t>(data->PaletteData->Length));
    }

    texture->Vram = gsKit_vram_alloc(gsGlobal, gsKit_texture_size(texture->Width, texture->Height, texture->PSM), GSKIT_ALLOC_USERBUFFER);
    if (texture->Vram == GSKIT_ALLOC_ERROR) {
        free(texture->Clut);
        free(texture->Mem);
        delete texture;
        return nullptr;
    }

    return texture;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:

```powershell
rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --no-restore --filter FullyQualifiedName~Ps2RenderManager3D_WhenLoadingPs2Textures_UsesPayloadDefinedPsmAndClutState -m:1 -nr:false -p:UseSharedCompilation=false -v minimal
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/platform/ps2/rendering/Ps2RenderManager3D.cpp builder.tests/Ps2RenderManager3DSourceTests.cs
git commit -m "Load indexed PS2 textures in 3D runtime"
```

### Task 6: Run the Focused PS2 Builder Suite and Export Verification

**Files:**
- Modify: `builder.tests/Ps2PlatformAssetBuilderTests.cs`
- Modify: `builder.tests/Ps2RuntimeTextureCookerTests.cs`
- Modify: `builder.tests/Ps2TextureAssetSerializationTests.cs`
- Modify: `builder.tests/Ps2BootHostSourceTests.cs`
- Modify: `builder.tests/Ps2RenderManager3DSourceTests.cs`

- [ ] **Step 1: Run the full targeted PS2 test slice**

Run:

```powershell
rtk dotnet test builder.tests/helengine.ps2.builder.tests.csproj --no-restore --filter FullyQualifiedName~Ps2PlatformAssetBuilderTests|FullyQualifiedName~Ps2RuntimeTextureCookerTests|FullyQualifiedName~Ps2TextureAssetSerializationTests|FullyQualifiedName~Ps2BootHostSourceTests|FullyQualifiedName~Ps2RenderManager3DSourceTests -m:1 -nr:false -p:UseSharedCompilation=false -v minimal
```

Expected: PASS for the focused indexed-texture contract surface.

- [ ] **Step 2: Rebuild the PS2 export after one external PS2 asset setting has been saved**

Precondition: the menu logo asset in the city project has already been saved from the editor with one PS2 platform texture format the builder now publishes, such as `Indexed8`.

Run:

```powershell
$env:HELENGINE_PS2_REPOSITORY_ROOT='C:\dev\helworks\helengine-ps2'
rtk proxy dotnet C:\dev\helworks\helengine\helengine.ui\helengine.editor.app\bin\Debug\net9.0-windows\helengine.editor.app.dll --project C:\dev\helprojs\city\project.heproj --build ps2 --output C:\dev\helprojs\output\ps2-city-demo-disc-mainmenu-indexed
```

Expected: export succeeds and the cooked imported logo texture serializes as a `Ps2TextureAsset` using indexed payload metadata instead of a forced direct-color-only path.

- [ ] **Step 3: Launch PCSX2 and verify the menu logo path**

Run:

```powershell
Start-Process -FilePath "C:\Program Files\PCSX2\pcsx2-qt.exe" -ArgumentList "`"C:\dev\helprojs\output\ps2-city-demo-disc-mainmenu-indexed\game.iso`"" -WindowStyle Hidden
```

Expected:

```text
DemoDiscMainMenu boots
menu logo renders
text still renders
no regression to startup hangs
```

- [ ] **Step 4: Commit**

```powershell
git add builder.tests/Ps2PlatformAssetBuilderTests.cs builder.tests/Ps2RuntimeTextureCookerTests.cs builder.tests/Ps2TextureAssetSerializationTests.cs builder.tests/Ps2BootHostSourceTests.cs builder.tests/Ps2RenderManager3DSourceTests.cs
git commit -m "Verify indexed PS2 texture pipeline"
```
