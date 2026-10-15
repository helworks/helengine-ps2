using helengine.baseplatform.Definitions;

namespace helengine.ps2.builder;

/// <summary>
/// Resolves the project's PS2 video mode from the build's graphics selection and stages the boot record. Graphics option
/// ids the selected profile does not declare (such as the removed default-width/default-height) are refused, so an old
/// project fails loudly instead of booting at 640x448.
/// </summary>
public static class Ps2VideoModeBootRecordWriter {
    /// <summary>Project file the graphics options come from, named in failures.</summary>
    const string SettingsFile = "settings/platform.ps2.json";

    /// <summary>
    /// Validates the graphics selection, resolves the mode and writes the record at the staging root.
    /// </summary>
    /// <param name="stagingRootPath">PS2 staging root.</param>
    /// <param name="definition">PS2 platform definition.</param>
    /// <param name="selectedGraphicsProfileId">Selected graphics profile.</param>
    /// <param name="selectedGraphicsOptionValues">Selected graphics option values.</param>
    /// <returns>The resolved mode.</returns>
    /// <exception cref="InvalidOperationException">Thrown for an unknown profile, an undeclared option, or a missing or unknown mode.</exception>
    public static PlatformVideoMode Write(string stagingRootPath, PlatformDefinition definition, string selectedGraphicsProfileId, IReadOnlyDictionary<string, string> selectedGraphicsOptionValues) {
        if (string.IsNullOrWhiteSpace(stagingRootPath)) {
            throw new ArgumentException("The PS2 staging root is required.", nameof(stagingRootPath));
        } else if (definition == null) {
            throw new ArgumentNullException(nameof(definition));
        } else if (selectedGraphicsOptionValues == null) {
            throw new InvalidOperationException($"The PS2 build received no graphics options; select a graphics profile and video mode in {SettingsFile}.");
        }

        PlatformGraphicsProfileDefinition profile = Array.Find(definition.GraphicsProfiles, candidate => string.Equals(candidate.ProfileId, selectedGraphicsProfileId, StringComparison.Ordinal));
        if (profile == null) {
            throw new InvalidOperationException($"PS2 graphics profile '{selectedGraphicsProfileId}' does not exist; fix graphics.selectedGraphicsProfileId in {SettingsFile}.");
        }

        foreach (string optionId in selectedGraphicsOptionValues.Keys) {
            if (!Array.Exists(profile.Settings, setting => string.Equals(setting.SettingId, optionId, StringComparison.Ordinal))) {
                throw new InvalidOperationException($"Graphics option '{optionId}' is not declared by PS2 graphics profile '{profile.ProfileId}'; remove it from graphics.selectedOptionValues in {SettingsFile}.");
            }
        }

        if (!selectedGraphicsOptionValues.TryGetValue(Ps2VideoModes.SettingId, out string modeId) || string.IsNullOrWhiteSpace(modeId)) {
            throw new InvalidOperationException($"No '{Ps2VideoModes.SettingId}' is selected in graphics.selectedOptionValues of {SettingsFile}.");
        }

        PlatformVideoMode mode = definition.VideoModeCapability.Resolve(modeId, definition.PlatformId);
        Directory.CreateDirectory(stagingRootPath);
        File.WriteAllBytes(Path.Combine(stagingRootPath, Ps2VideoModeBootRecord.LogicalPath), Ps2VideoModeBootRecord.Encode(mode));
        return mode;
    }
}
