using System.Text.Json;

namespace helengine.ps2.builder;

/// <summary>
/// Serializes and deserializes PS2 texture cook settings payloads used by builder-owned asset cook work items.
/// </summary>
public static class Ps2TextureCookSettingsSerializer {
    /// <summary>
    /// Stable settings contract identifier published through the PS2 asset cook capabilities.
    /// </summary>
    public const string SettingsContractId = "ps2.texture-settings.v1";

    /// <summary>
    /// Builds the default PS2 texture cook settings used when the source asset has no explicit PS2 override.
    /// </summary>
    /// <returns>Default PS2 texture cook settings.</returns>
    public static Ps2TextureCookSettings CreateDefault() {
        return new Ps2TextureCookSettings {
            MaxResolution = 0,
            Format = Ps2TextureFormat.Rgba32,
            AlphaMode = Ps2TextureAlphaMode.Full
        };
    }

    /// <summary>
    /// Serializes one PS2 texture settings payload into the stable string form carried by platform cook work items.
    /// </summary>
    /// <param name="settings">Settings payload to serialize.</param>
    /// <returns>Serialized PS2 texture settings string.</returns>
    public static string Serialize(Ps2TextureCookSettings settings) {
        if (settings == null) {
            throw new ArgumentNullException(nameof(settings));
        }

        return JsonSerializer.Serialize(settings);
    }

    /// <summary>
    /// Deserializes one PS2 texture settings payload from the string form carried by platform cook work items.
    /// </summary>
    /// <param name="serializedSettings">Serialized settings string.</param>
    /// <returns>Deserialized PS2 texture settings payload.</returns>
    public static Ps2TextureCookSettings Deserialize(string serializedSettings) {
        if (string.IsNullOrWhiteSpace(serializedSettings)) {
            throw new ArgumentException("Serialized PS2 texture settings are required.", nameof(serializedSettings));
        }

        Ps2TextureCookSettings settings = JsonSerializer.Deserialize<Ps2TextureCookSettings>(serializedSettings);
        if (settings == null) {
            throw new InvalidOperationException("Serialized PS2 texture settings did not produce a payload.");
        }

        return settings;
    }
}
