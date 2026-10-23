namespace helengine.ps2.builder;

/// <summary>Validates runtime-relative texture destinations before an editor cook can modify the staging tree.</summary>
internal static class Ps2TextureCookPath {
    /// <summary>Rejects roots, traversal, streams and Windows aliases while retaining the authored runtime path spelling.</summary>
    public static string Normalize(string path) {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("PS2 cooked output path is required.");
        string normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(path) || normalized.StartsWith('/')) throw new InvalidOperationException("PS2 cooked output must be runtime-relative.");
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments) {
            string stem = segment.Split('.')[0];
            bool device = stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] >= '1' && stem[3] <= '9');
            bool invalidPortableCharacter = segment.Any(character => character < 32 || "<>:\"|?*".Contains(character));
            if (segment == "." || segment == ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || invalidPortableCharacter || device || segment.EndsWith('.') || segment.EndsWith(' ')) throw new InvalidOperationException($"Unsafe PS2 cooked output path '{path}'.");
        }
        if (segments.Length == 0) throw new InvalidOperationException("PS2 cooked output path is empty.");
        return string.Join('/', segments);
    }

    /// <summary>Resolves only contained file destinations and rejects links or file/directory namespace conflicts.</summary>
    public static string ResolveDestination(string root, string normalizedPath) {
        string fullRoot = ResolvePhysicalRoot(root);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string destination = Path.GetFullPath(Path.Combine(fullRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison)) throw new InvalidOperationException("PS2 cooked output escaped the staging root.");
        string current = destination;
        while (!string.IsNullOrEmpty(current)) {
            bool exists = File.Exists(current) || Directory.Exists(current);
            if (exists && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException($"PS2 cooked output crosses a filesystem link at '{current}'.");
            if (current == destination && Directory.Exists(current)) throw new InvalidOperationException("PS2 cooked file destination is already a directory.");
            if (current != destination && File.Exists(current)) throw new InvalidOperationException("PS2 cooked output directory is already a file.");
            if (current.Equals(fullRoot, comparison)) break;
            current = Path.GetDirectoryName(current);
        }
        return destination;
    }

    /// <summary>Canonicalizes the caller-authorized staging root, including platform aliases such as macOS /tmp.</summary>
    static string ResolvePhysicalRoot(string root) {
        string fullPath = Path.GetFullPath(root);
        string volumeRoot = Path.GetPathRoot(fullPath);
        string current = volumeRoot;
        foreach (string segment in fullPath[volumeRoot.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)) {
            current = Path.Combine(current, segment);
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) {
                FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                FileSystemInfo target = entry.ResolveLinkTarget(returnFinalTarget: true)
                    ?? throw new InvalidOperationException($"PS2 staging root link could not be resolved at '{current}'.");
                current = target.FullName;
            }
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    /// <summary>Checks exact and parent/child collisions so independent work items cannot overwrite each other's files.</summary>
    public static void Reserve(HashSet<string> paths, string relativePath) {
        foreach (string existing in paths) {
            if (relativePath.Equals(existing, StringComparison.OrdinalIgnoreCase) || relativePath.StartsWith(existing + "/", StringComparison.OrdinalIgnoreCase) || existing.StartsWith(relativePath + "/", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"PS2 cooked path '{relativePath}' conflicts with '{existing}'.");
            }
        }
        paths.Add(relativePath);
    }
}
