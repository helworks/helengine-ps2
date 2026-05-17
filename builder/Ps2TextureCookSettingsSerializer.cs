using System.Text.Json;
using helengine.editor;

namespace helengine.ps2.builder;

/// <summary>
/// Serializes and deserializes PS2 texture cook settings payloads used by builder-owned asset cook work items.
/// </summary>
public static class Ps2TextureCookSettingsSerializer {
    /// <summary>
    /// Serializes one PS2 texture settings payload into the stable string form carried by platform cook work items.
    /// </summary>
    /// <param name="settings">Settings payload to serialize.</param>
    /// <returns>Serialized PS2 texture settings string.</returns>
    public static string Serialize(TextureAssetProcessorSettings settings) {
        if (settings == null) {
            throw new ArgumentNullException(nameof(settings));
        }

        return JsonSerializer.Serialize(new Dictionary<string, object> {
            ["maxResolution"] = settings.MaxResolution,
            ["colorFormat"] = settings.ColorFormat.ToString(),
            ["alphaPrecision"] = settings.AlphaPrecision.ToString()
        });
    }

    /// <summary>
    /// Deserializes one PS2 texture settings payload from the string form carried by platform cook work items.
    /// </summary>
    /// <param name="serializedSettings">Serialized settings string.</param>
    /// <returns>Deserialized PS2 texture settings payload.</returns>
    public static TextureAssetProcessorSettings Deserialize(string serializedSettings) {
        if (string.IsNullOrWhiteSpace(serializedSettings)) {
            throw new ArgumentException("Serialized PS2 texture settings are required.", nameof(serializedSettings));
        }

        using JsonDocument document = JsonDocument.Parse(serializedSettings);
        JsonElement root = document.RootElement;
        int maxResolution = root.TryGetProperty("maxResolution", out JsonElement maxResolutionElement)
            ? maxResolutionElement.GetInt32()
            : 0;
        string colorFormatName = root.TryGetProperty("colorFormat", out JsonElement colorFormatElement)
            ? colorFormatElement.GetString() ?? TextureAssetColorFormat.Rgba32.ToString()
            : TextureAssetColorFormat.Rgba32.ToString();
        string alphaPrecisionName = root.TryGetProperty("alphaPrecision", out JsonElement alphaPrecisionElement)
            ? alphaPrecisionElement.GetString() ?? TextureAssetAlphaPrecision.A8.ToString()
            : TextureAssetAlphaPrecision.A8.ToString();

        if (!Enum.TryParse(colorFormatName, true, out TextureAssetColorFormat colorFormat)) {
            throw new InvalidOperationException($"Unsupported PS2 texture color format '{colorFormatName}'.");
        }

        if (!Enum.TryParse(alphaPrecisionName, true, out TextureAssetAlphaPrecision alphaPrecision)) {
            throw new InvalidOperationException($"Unsupported PS2 texture alpha precision '{alphaPrecisionName}'.");
        }

        return new TextureAssetProcessorSettings {
            MaxResolution = maxResolution,
            ColorFormat = colorFormat,
            AlphaPrecision = alphaPrecision
        };
    }
}
