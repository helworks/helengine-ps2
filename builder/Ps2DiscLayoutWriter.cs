using helengine.baseplatform.Definitions;

namespace helengine.ps2.builder;

/// <summary>
/// Writes the staged PS2 disc layout consumed by ISO packaging.
/// </summary>
public sealed class Ps2DiscLayoutWriter {
    /// <summary>
    /// Builds the SYSTEM.CNF contents for one video mode: the boot ELF, the disc version and the video region, which
    /// tells the BIOS and emulators whether the disc is NTSC or PAL. The runtime still programs the GS mode itself from
    /// the video-mode boot record.
    /// </summary>
    /// <param name="videoMode">The project's PS2 video mode.</param>
    /// <returns>The SYSTEM.CNF text, with CRLF line ends.</returns>
    /// <exception cref="ArgumentNullException">Thrown when no video mode is given.</exception>
    public static string BuildBootConfigContents(PlatformVideoMode videoMode) {
        if (videoMode == null) {
            throw new ArgumentNullException(nameof(videoMode));
        }

        return "BOOT2 = cdrom0:\\" + Ps2BuildWorkspace.DiscExecutableFileName + ";1\r\n"
            + "VER = 1.00\r\n"
            + "VMODE = " + (Ps2VideoModes.IsPal(videoMode) ? "PAL" : "NTSC") + "\r\n";
    }

    /// <summary>
    /// Builds the logical cooked-path to physical PS2 disc-path mapping for one staged workspace without requiring the native ELF.
    /// </summary>
    /// <param name="workspace">Prepared PS2 build workspace.</param>
    /// <returns>Logical cooked paths mapped to their staged physical disc paths.</returns>
    public IReadOnlyDictionary<string, string> BuildLogicalToPhysicalPathMap(Ps2BuildWorkspace workspace) {
        if (workspace == null) {
            throw new ArgumentNullException(nameof(workspace));
        }

        return BuildLogicalToPhysicalPathMap(workspace.StagingRootPath);
    }

    /// <summary>
    /// Builds the logical cooked-path to physical PS2 disc-path mapping for one staging root without requiring a build
    /// workspace, so the desktop player can resolve staged content the same way the disc layout does.
    /// </summary>
    /// <param name="stagingRootPath">Absolute PS2 staging root that contains the staged <c>cooked</c> tree.</param>
    /// <returns>Logical cooked paths mapped to their staged physical disc paths, compared case-insensitively.</returns>
    internal static Dictionary<string, string> BuildLogicalToPhysicalPathMap(string stagingRootPath) {
        if (string.IsNullOrWhiteSpace(stagingRootPath)) {
            throw new ArgumentException("Staging root path must be provided.", nameof(stagingRootPath));
        }

        string stagedCookedRootPath = Path.Combine(stagingRootPath, "cooked");
        if (!Directory.Exists(stagedCookedRootPath)) {
            throw new DirectoryNotFoundException($"Staged cooked root '{stagedCookedRootPath}' was not found.");
        }

        Dictionary<string, string> logicalToPhysicalPaths = new(StringComparer.OrdinalIgnoreCase);
        CollectPackageFileMappings(stagingRootPath, logicalToPhysicalPaths);
        return logicalToPhysicalPaths;
    }

    /// <summary>
    /// Writes the staged disc layout for the supplied workspace.
    /// </summary>
    /// <param name="workspace">Prepared PS2 build workspace.</param>
    /// <param name="videoMode">The project's PS2 video mode, named in SYSTEM.CNF.</param>
    /// <returns>Logical cooked paths mapped to their staged physical disc paths.</returns>
    public IReadOnlyDictionary<string, string> Write(Ps2BuildWorkspace workspace, PlatformVideoMode videoMode) {
        if (workspace == null) {
            throw new ArgumentNullException(nameof(workspace));
        }
        if (videoMode == null) {
            throw new ArgumentNullException(nameof(videoMode));
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
        File.WriteAllText(workspace.DiscBootConfigPath, BuildBootConfigContents(videoMode));
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
            if (Ps2CookFingerprintStore.IsStoreFile(sourceRootPath, filePath)) {
                continue;
            }
            string logicalRelativePath = Path.GetRelativePath(sourceRootPath, filePath).Replace('\\', '/');
            string physicalRelativePath = Ps2DiscPathResolver.ResolveDiscRelativePath(logicalRelativePath).Replace('/', '\\');
            logicalToPhysicalPaths[logicalRelativePath] = "\\" + physicalRelativePath.Replace('/', '\\') + ";1";
            if (TryBuildGenericModelAliasLogicalPath(logicalRelativePath, out string genericModelAliasLogicalPath)) {
                logicalToPhysicalPaths[genericModelAliasLogicalPath] = "\\" + physicalRelativePath.Replace('/', '\\') + ";1";
            }
        }
    }

    /// <summary>
    /// Builds the generic cooked-model logical path alias that should resolve to one PS2-owned cooked model payload.
    /// </summary>
    /// <param name="logicalRelativePath">Staged logical path stored on disk.</param>
    /// <param name="genericModelAliasLogicalPath">Receives the generic cooked-model alias when the staged file is a PS2-owned model payload.</param>
    /// <returns>True when the staged file should also answer to the generic cooked-model logical path; otherwise false.</returns>
    static bool TryBuildGenericModelAliasLogicalPath(string logicalRelativePath, out string genericModelAliasLogicalPath) {
        genericModelAliasLogicalPath = string.Empty;
        if (string.IsNullOrWhiteSpace(logicalRelativePath)
            || !logicalRelativePath.EndsWith(".phm", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        genericModelAliasLogicalPath = Path.ChangeExtension(logicalRelativePath.Replace('\\', '/'), ".hasset").Replace('\\', '/');
        return !string.IsNullOrWhiteSpace(genericModelAliasLogicalPath);
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
            if (Ps2CookFingerprintStore.IsStoreFile(sourceRootPath, filePath)) {
                continue;
            }
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
