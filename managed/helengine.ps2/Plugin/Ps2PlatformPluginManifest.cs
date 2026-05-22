using System.Text.Json.Nodes;

namespace helengine.ps2.Plugin;

/// <summary>
/// Creates the metadata-only external platform plugin manifest exposed by the PS2 repository.
/// </summary>
public static class Ps2PlatformPluginManifest {
    /// <summary>
    /// Creates the metadata-only PS2 platform plugin manifest payload.
    /// </summary>
    /// <returns>Manifest object that contains only generic editor-consumable platform metadata.</returns>
    public static JsonObject Create() {
        return new JsonObject {
            ["platformId"] = "ps2",
            ["displayName"] = "PlayStation 2",
            ["builderAssemblyPath"] = "builder/helengine.ps2.builder.dll",
            ["definitionFactoryType"] = "helengine.ps2.builder.Ps2PlatformDefinitionFactory"
        };
    }
}
