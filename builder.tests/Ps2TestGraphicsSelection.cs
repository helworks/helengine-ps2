namespace helengine.ps2.builder.tests;

/// <summary>
/// The PS2 graphics selection test builds pass: the standard forward profile with every declared option set.
/// </summary>
public static class Ps2TestGraphicsSelection {
    /// <summary>Graphics profile id used by test builds.</summary>
    public const string ProfileId = "ps2-standard-forward";

    /// <summary>
    /// Creates a complete set of graphics option values for the standard forward profile.
    /// </summary>
    /// <param name="videoModeId">Value of the video-mode option.</param>
    /// <returns>New option values.</returns>
    public static Dictionary<string, string> CreateOptionValues(string videoModeId) {
        return new Dictionary<string, string> {
            ["video-mode"] = videoModeId,
            ["vsync-enabled"] = "true",
            ["fullscreen-enabled"] = "false",
            ["depth-handler-mode"] = "hardware"
        };
    }
}
