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
    /// <exception cref="IOException">Thrown when the record file cannot be written (for example, it is locked).</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the record path is not writable (for example, a directory occupies it).</exception>
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

        PlatformVideoMode mode = null;
        foreach (PlatformVideoMode candidate in definition.VideoModeCapability.Modes) {
            if (string.Equals(candidate.Id, modeId, StringComparison.Ordinal)) {
                mode = candidate;
                break;
            }
        }

        if (mode == null) {
            throw new InvalidOperationException($"Graphics option '{Ps2VideoModes.SettingId}' in graphics.selectedOptionValues of {SettingsFile} is '{modeId}', which is not a PS2 video mode; use one of: {string.Join(", ", definition.VideoModeCapability.GetModeIds())}.");
        }

        Directory.CreateDirectory(stagingRootPath);
        File.WriteAllBytes(Path.Combine(stagingRootPath, Ps2VideoModeBootRecord.LogicalPath), Ps2VideoModeBootRecord.Encode(mode));
        return mode;
    }
}
