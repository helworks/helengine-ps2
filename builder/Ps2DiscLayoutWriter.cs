namespace helengine.ps2.builder;

/// <summary>
/// Writes the staged PS2 disc layout consumed by ISO packaging.
/// </summary>
public sealed class Ps2DiscLayoutWriter {
    /// <summary>
    /// Stable boot configuration written into the staged PS2 disc root.
    /// </summary>
    static readonly string BootConfigContents = "BOOT2 = cdrom0:\\"
        + Ps2BuildWorkspace.DiscExecutableFileName
        + ";1\r\nVER = 1.00\r\n";

    /// <summary>
    /// Builds the logical cooked-path to physical PS2 disc-path mapping for one staged workspace without requiring the native ELF.
    /// </summary>
    /// <param name="workspace">Prepared PS2 build workspace.</param>
    /// <returns>Logical cooked paths mapped to their staged physical disc paths.</returns>
    public IReadOnlyDictionary<string, string> BuildLogicalToPhysicalPathMap(Ps2BuildWorkspace workspace) {
        if (workspace == null) {
            throw new ArgumentNullException(nameof(workspace));
        }

        string stagedCookedRootPath = Path.Combine(workspace.StagingRootPath, "cooked");
        if (!Directory.Exists(stagedCookedRootPath)) {
            throw new DirectoryNotFoundException($"Staged cooked root '{stagedCookedRootPath}' was not found.");
        }

        Dictionary<string, string> logicalToPhysicalPaths = new(StringComparer.OrdinalIgnoreCase);
        CollectPackageFileMappings(workspace.StagingRootPath, logicalToPhysicalPaths);
        return logicalToPhysicalPaths;
    }

    /// <summary>
    /// Writes the staged disc layout for the supplied workspace.
    /// </summary>
    /// <param name="workspace">Prepared PS2 build workspace.</param>
    /// <returns>Logical cooked paths mapped to their staged physical disc paths.</returns>
    public IReadOnlyDictionary<string, string> Write(Ps2BuildWorkspace workspace) {
        if (workspace == null) {
            throw new ArgumentNullException(nameof(workspace));
        }
        if (!File.Exists(workspace.NativeExecutablePath)) {
            throw new FileNotFoundException("Native PS2 executable is required before disc staging.", workspace.NativeExecutablePath);
        }

        if (Directory.Exists(workspace.DiscRootPath)) {
            Directory.Delete(workspace.DiscRootPath, recursive: true);
        }

        Directory.CreateDirectory(workspace.DiscRootPath);
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths = BuildLogicalToPhysicalPathMap(workspace);
        CopyPackageFiles(workspace.StagingRootPath, workspace.DiscRootPath);
        File.Copy(workspace.NativeExecutablePath, workspace.DiscExecutablePath, true);
        File.WriteAllText(workspace.DiscBootConfigPath, BootConfigContents);
        return logicalToPhysicalPaths;
    }

    /// <summary>
    /// Collects logical staged paths and their PS2-safe physical disc paths without writing files.
    /// </summary>
    /// <param name="sourceRootPath">Source package root to inspect.</param>
    /// <param name="logicalToPhysicalPaths">Logical-to-physical staged PS2 disc path mappings.</param>
    static void CollectPackageFileMappings(
        string sourceRootPath,
        Dictionary<string, string> logicalToPhysicalPaths) {
        string[] filePaths = Directory.GetFiles(sourceRootPath, "*", SearchOption.AllDirectories);
        for (int fileIndex = 0; fileIndex < filePaths.Length; fileIndex++) {
            string filePath = filePaths[fileIndex];
            string logicalRelativePath = Path.GetRelativePath(sourceRootPath, filePath).Replace('\\', '/');
            string physicalRelativePath = Ps2DiscPathResolver.ResolveDiscRelativePath(logicalRelativePath).Replace('/', '\\');
            logicalToPhysicalPaths[logicalRelativePath] = "\\" + physicalRelativePath.Replace('/', '\\') + ";1";
        }
    }

    /// <summary>
    /// Recursively copies one staged package tree into the PS2 disc root using PS2-safe physical filenames.
    /// </summary>
    /// <param name="sourceRootPath">Source package root to copy.</param>
    /// <param name="discRootPath">Destination disc root that receives the copied files.</param>
    static void CopyPackageFiles(
        string sourceRootPath,
        string discRootPath) {
        string[] filePaths = Directory.GetFiles(sourceRootPath, "*", SearchOption.AllDirectories);
        for (int fileIndex = 0; fileIndex < filePaths.Length; fileIndex++) {
            string filePath = filePaths[fileIndex];
            string logicalRelativePath = Path.GetRelativePath(sourceRootPath, filePath).Replace('\\', '/');
            string physicalRelativePath = Ps2DiscPathResolver.ResolveDiscRelativePath(logicalRelativePath).Replace('/', '\\');
            string destinationFilePath = Path.Combine(discRootPath, physicalRelativePath);
            string destinationDirectoryPath = Path.GetDirectoryName(destinationFilePath)
                ?? throw new InvalidOperationException($"Could not resolve destination directory for '{destinationFilePath}'.");
            Directory.CreateDirectory(destinationDirectoryPath);
            File.Copy(filePath, destinationFilePath, true);
        }
    }
}
