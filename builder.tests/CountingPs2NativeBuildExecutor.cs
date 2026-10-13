namespace helengine.ps2.builder.tests;

/// <summary>
/// Native build executor double that counts how often the builder asked for native compilation and ISO packaging,
/// so assets-only tests can prove those steps never ran, and writes placeholder outputs so full builds can still finish.
/// </summary>
public sealed class CountingPs2NativeBuildExecutor : IPs2NativeBuildExecutor {
    /// <summary>
    /// Gets how many times the builder requested a native ELF build.
    /// </summary>
    public int BuildCallCount { get; private set; }

    /// <summary>
    /// Gets how many times the builder requested ISO packaging.
    /// </summary>
    public int PackageIsoCallCount { get; private set; }

    /// <summary>
    /// Records one native build request and writes a placeholder ELF where the real build would place it.
    /// </summary>
    /// <param name="workspace">Workspace the builder asked to compile.</param>
    /// <param name="cancellationToken">Unused cancellation token.</param>
    public void Build(Ps2BuildWorkspace workspace, CancellationToken cancellationToken) {
        if (workspace == null) {
            throw new ArgumentNullException(nameof(workspace));
        }

        BuildCallCount++;
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.NativeExecutablePath));
        File.WriteAllText(workspace.NativeExecutablePath, "elf");
    }

    /// <summary>
    /// Records one ISO packaging request and writes a placeholder image where the real packager would place it.
    /// </summary>
    /// <param name="workspace">Workspace the builder asked to package.</param>
    /// <param name="cancellationToken">Unused cancellation token.</param>
    public void PackageIso(Ps2BuildWorkspace workspace, CancellationToken cancellationToken) {
        if (workspace == null) {
            throw new ArgumentNullException(nameof(workspace));
        }

        PackageIsoCallCount++;
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.IsoOutputPath));
        File.WriteAllText(workspace.IsoOutputPath, "iso");
    }
}
