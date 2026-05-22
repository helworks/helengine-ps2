using System.Text.Json.Nodes;
using helengine.ps2.Plugin;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the PS2 external plugin manifest remains metadata-only.
/// </summary>
public sealed class Ps2PlatformPluginManifestTests {
    /// <summary>
    /// Ensures the in-memory plugin manifest payload does not declare runtime payload extension metadata.
    /// </summary>
    [Fact]
    public void Create_WhenSerialized_DoesNotContainRuntimePayloadTypeDeclarations() {
        JsonObject manifest = Ps2PlatformPluginManifest.Create();

        Assert.Null(manifest["runtimePayloadTypes"]);
        Assert.Null(manifest["serializerHooks"]);
        Assert.Equal("ps2", manifest["platformId"]?.GetValue<string>());
        Assert.Equal("PlayStation 2", manifest["displayName"]?.GetValue<string>());
        JsonArray generatedCoreProjectPaths = Assert.IsType<JsonArray>(manifest["generatedCoreProjectPaths"]);
        Assert.Single(generatedCoreProjectPaths);
        Assert.Equal("managed/helengine.ps2/helengine.ps2.csproj", generatedCoreProjectPaths[0]?.GetValue<string>());
    }

    /// <summary>
    /// Ensures the checked-in plugin manifest file matches the generated metadata-only payload.
    /// </summary>
    [Fact]
    public void CheckedInPluginManifest_MatchesGeneratedMetadataOnlyPayload() {
        string manifestFilePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "platform-plugin.json"));
        string expectedJson = Ps2PlatformPluginManifest.Create().ToJsonString();
        string actualJson = JsonNode.Parse(File.ReadAllText(manifestFilePath))?.ToJsonString();

        Assert.Equal(expectedJson, actualJson);
    }
}
