namespace helengine.ps2.builder;

/// <summary>
/// Preserves the PS2 builder resolver surface while delegating path resolution to the shared engine contract.
/// </summary>
public static class Ps2DiscPathResolver {
    /// <summary>
    /// Resolves one logical cooked relative path into a PS2-safe physical disc relative path.
    /// </summary>
    /// <param name="logicalRelativePath">Logical cooked relative path that identifies one staged asset.</param>
    /// <returns>PS2-safe physical relative path using uppercase 8.3-compatible segments.</returns>
    public static string ResolveDiscRelativePath(string logicalRelativePath) {
        return helengine.baseplatform.Paths.Ps2DiscPathResolver.ResolveDiscRelativePath(logicalRelativePath);
    }

    /// <summary>
    /// Resolves one logical cooked relative path into the rooted runtime path consumed by the PS2 player.
    /// </summary>
    /// <param name="logicalRelativePath">Logical cooked relative path that identifies one staged asset.</param>
    /// <returns>Rooted PS2 runtime path including the disc root and version suffix.</returns>
    public static string ResolveRuntimePath(string logicalRelativePath) {
        return helengine.baseplatform.Paths.Ps2DiscPathResolver.ResolveRuntimePath(logicalRelativePath);
    }
}
