namespace helengine.ps2.builder;

/// <summary>
/// Describes the prepared filesystem layout consumed by the native PS2 Docker build.
/// </summary>
public sealed class Ps2BuildWorkspace {
    /// <summary>
    /// BIOS-safe 8.3 boot executable filename staged into the PS2 disc root.
    /// </summary>
    public const string DiscExecutableFileName = "HELENGIN.ELF";

    /// <summary>
    /// Initializes one PS2 build workspace.
    /// </summary>
    /// <param name="repositoryRootPath">PS2 native repository root that contains the Dockerfile and Makefile.</param>
    /// <param name="stagingRootPath">Prepared staged package root used as the source of cooked runtime files.</param>
    /// <param name="generatedCoreRootPath">Generated native core root compiled into the PS2 ELF.</param>
    /// <param name="outputRootPath">Final export root that receives the ELF and cooked runtime files.</param>
    /// <param name="artifactRootPath">Invocation-private artifact root used to stage the disc tree and ISO before final publication.</param>
    /// <param name="nativeExecutablePath">Absolute path to the built ELF produced by the Docker build.</param>
    public Ps2BuildWorkspace(
        string repositoryRootPath,
        string stagingRootPath,
        string generatedCoreRootPath,
        string outputRootPath,
        string artifactRootPath,
        string nativeExecutablePath) {
        if (string.IsNullOrWhiteSpace(repositoryRootPath)) {
            throw new ArgumentException("Repository root path must be provided.", nameof(repositoryRootPath));
        }
        if (string.IsNullOrWhiteSpace(stagingRootPath)) {
            throw new ArgumentException("Staging root path must be provided.", nameof(stagingRootPath));
        }
        if (string.IsNullOrWhiteSpace(generatedCoreRootPath)) {
            throw new ArgumentException("Generated core root path must be provided.", nameof(generatedCoreRootPath));
        }
        if (string.IsNullOrWhiteSpace(outputRootPath)) {
            throw new ArgumentException("Output root path must be provided.", nameof(outputRootPath));
        }
        if (string.IsNullOrWhiteSpace(artifactRootPath)) {
            throw new ArgumentException("Artifact root path must be provided.", nameof(artifactRootPath));
        }
        if (string.IsNullOrWhiteSpace(nativeExecutablePath)) {
            throw new ArgumentException("Native executable path must be provided.", nameof(nativeExecutablePath));
        }

        RepositoryRootPath = Path.GetFullPath(repositoryRootPath);
        StagingRootPath = Path.GetFullPath(stagingRootPath);
        GeneratedCoreRootPath = Path.GetFullPath(generatedCoreRootPath);
        OutputRootPath = Path.GetFullPath(outputRootPath);
        ArtifactRootPath = Path.GetFullPath(artifactRootPath);
        NativeExecutablePath = Path.GetFullPath(nativeExecutablePath);
    }

    /// <summary>
    /// Gets the PS2 native repository root that contains the Dockerfile and Makefile.
    /// </summary>
    public string RepositoryRootPath { get; }

    /// <summary>
    /// Gets the prepared staged package root used as the source of cooked runtime files.
    /// </summary>
    public string StagingRootPath { get; }

    /// <summary>
    /// Gets the generated native core root compiled into the PS2 ELF.
    /// </summary>
    public string GeneratedCoreRootPath { get; }

    /// <summary>
    /// Gets the generated runtime folder that receives PS2-specific native manifest source.
    /// </summary>
    public string GeneratedRuntimeRootPath => Path.Combine(GeneratedCoreRootPath, "runtime");

    /// <summary>
    /// Gets the final export root that receives the ELF and cooked runtime files.
    /// </summary>
    public string OutputRootPath { get; }

    /// <summary>
    /// Gets the invocation-private artifact root that contains PS2 packaging outputs before publication.
    /// </summary>
    public string ArtifactRootPath { get; }

    /// <summary>
    /// Gets the absolute path to the built ELF produced by the Docker build.
    /// </summary>
    public string NativeExecutablePath { get; }

    /// <summary>
    /// Gets the invocation-private staged PS2 disc root.
    /// </summary>
    public string DiscRootPath => Path.Combine(ArtifactRootPath, "disc");

    /// <summary>
    /// Gets the generated PS2 runtime asset-path manifest header path.
    /// </summary>
    public string RuntimeAssetPathManifestHeaderPath => Path.Combine(GeneratedRuntimeRootPath, "runtime_ps2_asset_path_manifest.hpp");

    /// <summary>
    /// Gets the generated PS2 runtime asset-path manifest source path.
    /// </summary>
    public string RuntimeAssetPathManifestSourcePath => Path.Combine(GeneratedRuntimeRootPath, "runtime_ps2_asset_path_manifest.cpp");

    /// <summary>
    /// Gets the generated PS2 boot configuration file path inside the staged disc root.
    /// </summary>
    public string DiscBootConfigPath => Path.Combine(DiscRootPath, "SYSTEM.CNF");

    /// <summary>
    /// Gets the staged PS2 boot ELF path inside the staged disc root.
    /// </summary>
    public string DiscExecutablePath => Path.Combine(DiscRootPath, DiscExecutableFileName);

    /// <summary>
    /// Gets the invocation-private bootable PS2 ISO output path.
    /// </summary>
    public string IsoOutputPath => Path.Combine(ArtifactRootPath, "game.iso");

    /// <summary>
    /// Gets the final published PS2 disc root path.
    /// </summary>
    public string PublishedDiscRootPath => Path.Combine(OutputRootPath, "disc");

    /// <summary>
    /// Gets the final published PS2 ISO path.
    /// </summary>
    public string PublishedIsoOutputPath => Path.Combine(OutputRootPath, "game.iso");
}
