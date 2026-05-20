using helengine.ps2.builder;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies PS2 staged asset reference rewriting for editor-owned cooked artifacts.
/// </summary>
public sealed class Ps2CookedAssetPathRewriterTests {
    /// <summary>
    /// Ensures scene material references that already point at staged generic material assets resolve to their PS2 cooked material payloads.
    /// </summary>
    [Fact]
    public void Rewrite_WhenSceneReferencesUnprefixedCookedMaterialAsset_ResolvesCookedMaterialPhysicalPath() {
        string rootPath = Path.Combine(Path.GetTempPath(), "ps2-cooked-path-rewriter-tests", Guid.NewGuid().ToString("N"));
        string stagingRootPath = Path.Combine(rootPath, "staging");
        string scenePath = Path.Combine(stagingRootPath, "cooked", "scenes", "colored_cube_grid.hasset");
        string materialPath = Path.Combine(stagingRootPath, "cooked", "materials", "rendering", "colored_cube_grid", "Cube00.hasset");
        string sceneDirectoryPath = Path.GetDirectoryName(scenePath);
        string materialDirectoryPath = Path.GetDirectoryName(materialPath);
        if (sceneDirectoryPath == null) {
            throw new InvalidOperationException("Scene test path must include a directory.");
        } else if (materialDirectoryPath == null) {
            throw new InvalidOperationException("Material test path must include a directory.");
        }

        Directory.CreateDirectory(sceneDirectoryPath);
        Directory.CreateDirectory(materialDirectoryPath);

        SceneAsset sceneAsset = new() {
            AssetReferences = [
                new SceneAssetReference {
                    SourceKind = SceneAssetReferenceSourceKind.FileSystem,
                    RelativePath = "materials/rendering/colored_cube_grid/Cube00.hasset",
                    ProviderId = string.Empty,
                    AssetId = string.Empty
                }
            ]
        };
        Ps2MaterialAsset materialAsset = new();
        File.WriteAllBytes(scenePath, helengine.files.AssetSerializer.SerializeToBytes(sceneAsset));
        File.WriteAllBytes(materialPath, helengine.files.AssetSerializer.SerializeToBytes(materialAsset));

        Ps2BuildWorkspace workspace = new(
            rootPath,
            stagingRootPath,
            Path.Combine(rootPath, "generated-core"),
            Path.Combine(rootPath, "out"),
            Path.Combine(rootPath, "native", "helengine_ps2.elf"));
        Ps2DiscLayoutWriter layoutWriter = new();
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths = layoutWriter.BuildLogicalToPhysicalPathMap(workspace);

        Ps2CookedAssetPathRewriter rewriter = new();
        rewriter.Rewrite(stagingRootPath, logicalToPhysicalPaths);

        using FileStream rewrittenSceneStream = File.OpenRead(scenePath);
        SceneAsset rewrittenSceneAsset = (SceneAsset)helengine.files.AssetSerializer.Deserialize(rewrittenSceneStream);
        Assert.Equal(
            "cdrom0:" + logicalToPhysicalPaths["cooked/materials/rendering/colored_cube_grid/Cube00.hasset"],
            rewrittenSceneAsset.AssetReferences[0].RelativePath);
    }
}
