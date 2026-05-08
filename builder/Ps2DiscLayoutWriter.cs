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
    /// Writes the staged disc layout for the supplied workspace.
    /// </summary>
    /// <param name="workspace">Prepared PS2 build workspace.</param>
    public void Write(Ps2BuildWorkspace workspace) {
        if (workspace == null) {
            throw new ArgumentNullException(nameof(workspace));
        }
        if (!File.Exists(workspace.NativeExecutablePath)) {
            throw new FileNotFoundException("Native PS2 executable is required before disc staging.", workspace.NativeExecutablePath);
        }

        string stagedCookedRootPath = Path.Combine(workspace.StagingRootPath, "cooked");
        if (!Directory.Exists(stagedCookedRootPath)) {
            throw new DirectoryNotFoundException($"Staged cooked root '{stagedCookedRootPath}' was not found.");
        }

        Directory.CreateDirectory(workspace.DiscRootPath);
        string stagedDiscCookedRootPath = Path.Combine(workspace.DiscRootPath, "cooked");
        if (!Directory.Exists(stagedDiscCookedRootPath)) {
            CopyDirectory(stagedCookedRootPath, stagedDiscCookedRootPath);
        }

        File.Copy(workspace.NativeExecutablePath, workspace.DiscExecutablePath, true);
        File.WriteAllText(workspace.DiscBootConfigPath, BootConfigContents);
    }

    /// <summary>
    /// Recursively copies one directory tree into another directory.
    /// </summary>
    /// <param name="sourcePath">Source directory to copy.</param>
    /// <param name="destinationPath">Destination directory that receives the copied files.</param>
    static void CopyDirectory(string sourcePath, string destinationPath) {
        Directory.CreateDirectory(destinationPath);
        string[] filePaths = Directory.GetFiles(sourcePath);
        for (int fileIndex = 0; fileIndex < filePaths.Length; fileIndex++) {
            string filePath = filePaths[fileIndex];
            string destinationFilePath = Path.Combine(destinationPath, Path.GetFileName(filePath));
            File.Copy(filePath, destinationFilePath, true);
        }

        string[] directoryPaths = Directory.GetDirectories(sourcePath);
        for (int directoryIndex = 0; directoryIndex < directoryPaths.Length; directoryIndex++) {
            string directoryPath = directoryPaths[directoryIndex];
            string destinationDirectoryPath = Path.Combine(destinationPath, Path.GetFileName(directoryPath));
            CopyDirectory(directoryPath, destinationDirectoryPath);
        }
    }
}
