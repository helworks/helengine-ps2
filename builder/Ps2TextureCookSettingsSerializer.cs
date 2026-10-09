using System.Text.Json;
using helengine.editor;

namespace helengine.ps2.builder;

/// <summary>Serializes explicit PS2 native format ids while retaining supported legacy generic settings.</summary>
public static class Ps2TextureCookSettingsSerializer {
    /// <summary>Writes the persisted format id without converting platform-owned names through the generic enum.</summary>
    public static string Serialize(TextureAssetProcessorSettings settings) {
        Validate(settings);
        return JsonSerializer.Serialize(new Dictionary<string, object> {
            ["maxResolution"] = settings.MaxResolution,
            ["colorFormat"] = settings.ColorFormatId,
            ["alphaPrecision"] = settings.AlphaPrecision.ToString(),
            ["indexingMethod"] = settings.IndexingMethodId ?? string.Empty
        });
    }

    /// <summary>Rejects unknown, duplicate and malformed settings rather than choosing a replacement texture format.</summary>
    public static TextureAssetProcessorSettings Deserialize(string serializedSettings) {
        if (string.IsNullOrWhiteSpace(serializedSettings)) throw new ArgumentException("Serialized PS2 texture settings are required.", nameof(serializedSettings));
        using JsonDocument document = JsonDocument.Parse(serializedSettings);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("PS2 texture settings must be an object.");
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject()) {
            if (!keys.Add(property.Name)) throw new InvalidOperationException($"Duplicate PS2 texture setting '{property.Name}'.");
            if (property.Name != "maxResolution" && property.Name != "colorFormat" && property.Name != "alphaPrecision" && property.Name != "indexingMethod") throw new InvalidOperationException($"Unknown PS2 texture setting '{property.Name}'.");
        }
        if (!root.TryGetProperty("maxResolution", out JsonElement maximum) || !root.TryGetProperty("colorFormat", out JsonElement format) || !root.TryGetProperty("alphaPrecision", out JsonElement alpha)) {
            throw new InvalidOperationException("PS2 texture settings require maxResolution, colorFormat, and alphaPrecision.");
        }
        string alphaName = alpha.GetString();
        if (!Enum.TryParse(alphaName, false, out TextureAssetAlphaPrecision precision) || precision.ToString() != alphaName) throw new InvalidOperationException($"Unsupported PS2 alpha precision '{alphaName}'.");
        TextureAssetProcessorSettings settings = new() {
            MaxResolution = maximum.GetInt32(),
            ColorFormatId = format.GetString(),
            AlphaPrecision = precision,
            IndexingMethodId = root.TryGetProperty("indexingMethod", out JsonElement indexing) ? indexing.GetString() : string.Empty
        };
        Validate(settings);
        return settings;
    }

    /// <summary>Maps only the three supported legacy names to their existing GS texture and CLUT formats.</summary>
    public static string ResolveNativeFormatId(string formatId) {
        if (formatId == "Rgba32") return "PS2.PSMCT32";
        if (formatId == "Indexed8") return "PS2.PSMT8";
        if (formatId == "Indexed4") return "PS2.PSMT4";
        return formatId;
    }

    /// <summary>Checks native alpha/CLUT compatibility and limits indexing to the supported generic quantization strategy.</summary>
    public static void Validate(TextureAssetProcessorSettings settings) {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.MaxResolution < 0) throw new InvalidOperationException("PS2 maxResolution cannot be negative.");
        if (!Ps2GsTextureFormatCatalog.TryGetFormat(ResolveNativeFormatId(settings.ColorFormatId), out Ps2GsTextureFormatDefinition format)) throw new InvalidOperationException($"Unsupported PS2 texture format '{settings.ColorFormatId}'.");
        bool legacy = settings.ColorFormatId == "Rgba32" || settings.ColorFormatId == "Indexed4" || settings.ColorFormatId == "Indexed8";
        if (legacy) {
            bool compatible = settings.AlphaPrecision == TextureAssetAlphaPrecision.A8 || (settings.ColorFormatId == "Rgba32" && settings.AlphaPrecision == TextureAssetAlphaPrecision.Opaque);
            if (!compatible) throw new InvalidOperationException($"Unsupported legacy PS2 format/alpha combination '{settings.ColorFormatId}' + '{settings.AlphaPrecision}'.");
        } else if (!format.SupportedAlphaPrecisions.Contains(settings.AlphaPrecision)) {
            throw new InvalidOperationException($"PS2 format '{format.Id}' does not support '{settings.AlphaPrecision}' alpha.");
        }
        string indexing = settings.IndexingMethodId ?? string.Empty;
        if (format.IsIndexed) {
            if (indexing.Length != 0 && indexing != TextureAssetIndexingMethod.QuantizedIndexed.ToString()) throw new InvalidOperationException($"Unsupported PS2 indexing method '{indexing}'.");
        } else if (indexing.Length != 0) {
            throw new InvalidOperationException("PS2 indexingMethod is only valid for indexed formats.");
        }
    }
}
