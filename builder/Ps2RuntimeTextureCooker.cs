using helengine.editor;

namespace helengine.ps2.builder;

/// <summary>
/// Converts decoded source textures into PS2-native runtime texture assets.
/// </summary>
public sealed class Ps2RuntimeTextureCooker {
    /// <summary>
    /// Number of palette bytes stored per RGBA32 CLUT entry.
    /// </summary>
    const int PaletteEntryByteCount = 4;

    /// <summary>
    /// Number of CLUT entries grouped into one PS2 CSM1 swizzle block.
    /// </summary>
    const int Csm1PaletteBlockEntryCount = 32;

    /// <summary>
    /// Size of one swapped octet within a PS2 CSM1 swizzle block.
    /// </summary>
    const int Csm1PaletteOctetEntryCount = 8;

    readonly TextureAssetProcessor TextureAssetProcessor;

    /// <summary>
    /// Initializes a new PS2 runtime texture cooker with the shared generic texture processor used for max-resolution and format handling.
    /// </summary>
    public Ps2RuntimeTextureCooker() {
        TextureAssetProcessor = new TextureAssetProcessor();
    }

    /// <summary>
    /// Cooks one decoded source texture into a PS2-native runtime texture asset.
    /// </summary>
    /// <param name="sourceTexture">Decoded source texture.</param>
    /// <param name="settings">Resolved PS2 texture cook settings.</param>
    /// <returns>PS2-native runtime texture asset.</returns>
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
                PaletteData = SwizzlePaletteDataForPs2(processedTexture.PaletteColors)
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
                PaletteData = SwizzlePaletteDataForPs2(processedTexture.PaletteColors)
            };
        }

        throw new InvalidOperationException(
            $"PS2 does not support texture settings '{processedTexture.ColorFormat}' + '{processedTexture.AlphaPrecision}'.");
    }

    /// <summary>
    /// Reorders one RGBA32 palette into the PS2 GS CSM1 CLUT entry order so indexed textures render with the intended colors.
    /// </summary>
    /// <param name="paletteColors">Logical palette entries emitted by the shared texture processor.</param>
    /// <returns>PS2-ready CLUT payload bytes.</returns>
    static byte[] SwizzlePaletteDataForPs2(byte[] paletteColors) {
        if (paletteColors == null || paletteColors.Length == 0) {
            return Array.Empty<byte>();
        }

        if ((paletteColors.Length % PaletteEntryByteCount) != 0) {
            throw new InvalidOperationException("Indexed palette payload must be aligned to RGBA32 entry boundaries.");
        }

        int paletteEntryCount = paletteColors.Length / PaletteEntryByteCount;
        byte[] swizzledPalette = new byte[paletteColors.Length];
        for (int blockStartEntry = 0; blockStartEntry < paletteEntryCount; blockStartEntry += Csm1PaletteBlockEntryCount) {
            int blockEntryCount = Math.Min(Csm1PaletteBlockEntryCount, paletteEntryCount - blockStartEntry);
            for (int blockOffsetEntry = 0; blockOffsetEntry < blockEntryCount; blockOffsetEntry++) {
                int logicalEntryIndex = blockStartEntry + blockOffsetEntry;
                int physicalEntryIndex = blockStartEntry + ResolveCsm1PhysicalEntryOffset(blockOffsetEntry, blockEntryCount);
                CopyPaletteEntry(paletteColors, logicalEntryIndex, swizzledPalette, physicalEntryIndex);
            }
        }

        return swizzledPalette;
    }

    /// <summary>
    /// Resolves the physical CLUT slot used by one logical palette entry inside a PS2 CSM1 block.
    /// </summary>
    /// <param name="blockOffsetEntry">Zero-based entry offset inside the current 32-entry block.</param>
    /// <param name="blockEntryCount">Actual number of entries present in the current block.</param>
    /// <returns>Physical slot offset for the supplied logical entry.</returns>
    static int ResolveCsm1PhysicalEntryOffset(int blockOffsetEntry, int blockEntryCount) {
        if (blockOffsetEntry < 0 || blockOffsetEntry >= blockEntryCount) {
            throw new ArgumentOutOfRangeException(nameof(blockOffsetEntry));
        }

        int secondOctetStart = Csm1PaletteOctetEntryCount;
        int thirdOctetStart = Csm1PaletteOctetEntryCount * 2;
        int fourthOctetStart = Csm1PaletteOctetEntryCount * 3;
        if (blockEntryCount <= thirdOctetStart) {
            return blockOffsetEntry;
        }

        if (blockOffsetEntry >= secondOctetStart && blockOffsetEntry < thirdOctetStart) {
            int swappedEntryOffset = blockOffsetEntry + Csm1PaletteOctetEntryCount;
            if (swappedEntryOffset < blockEntryCount) {
                return swappedEntryOffset;
            }
        } else if (blockOffsetEntry >= thirdOctetStart && blockOffsetEntry < fourthOctetStart) {
            return blockOffsetEntry - Csm1PaletteOctetEntryCount;
        }

        return blockOffsetEntry;
    }

    /// <summary>
    /// Copies one RGBA32 palette entry between logical and PS2-swizzled palette buffers.
    /// </summary>
    /// <param name="sourcePalette">Source palette bytes.</param>
    /// <param name="sourceEntryIndex">Zero-based source entry index.</param>
    /// <param name="destinationPalette">Destination palette bytes.</param>
    /// <param name="destinationEntryIndex">Zero-based destination entry index.</param>
    static void CopyPaletteEntry(byte[] sourcePalette, int sourceEntryIndex, byte[] destinationPalette, int destinationEntryIndex) {
        int sourceByteOffset = sourceEntryIndex * PaletteEntryByteCount;
        int destinationByteOffset = destinationEntryIndex * PaletteEntryByteCount;
        Buffer.BlockCopy(sourcePalette, sourceByteOffset, destinationPalette, destinationByteOffset, PaletteEntryByteCount);
    }
}
