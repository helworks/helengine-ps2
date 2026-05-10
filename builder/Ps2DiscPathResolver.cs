namespace helengine.ps2.builder;

/// <summary>
/// Resolves deterministic PS2-safe physical disc paths from logical cooked runtime paths.
/// </summary>
public static class Ps2DiscPathResolver {
    /// <summary>
    /// Resolves one logical cooked relative path into a PS2-safe physical disc relative path.
    /// </summary>
    /// <param name="logicalRelativePath">Logical cooked relative path that identifies one staged asset.</param>
    /// <returns>PS2-safe physical relative path using uppercase 8.3-compatible segments.</returns>
    public static string ResolveDiscRelativePath(string logicalRelativePath) {
        if (string.IsNullOrWhiteSpace(logicalRelativePath)) {
            throw new ArgumentException("Logical relative path must be provided.", nameof(logicalRelativePath));
        }

        string normalizedPath = logicalRelativePath.Replace('\\', '/');
        string[] segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] resolvedSegments = new string[segments.Length];
        for (int index = 0; index < segments.Length; index++) {
            bool isLastSegment = index == segments.Length - 1;
            resolvedSegments[index] = isLastSegment
                ? ResolveFileName(segments[index])
                : ResolveComponentToken(segments[index], 8);
        }

        return string.Join(Path.DirectorySeparatorChar, resolvedSegments);
    }

    /// <summary>
    /// Resolves one directory or file stem token into an uppercase PS2-safe component token.
    /// </summary>
    /// <param name="token">Logical directory name or file stem token.</param>
    /// <param name="maxLength">Maximum physical token length.</param>
    /// <returns>Sanitized uppercase token that fits the supplied length budget.</returns>
    static string ResolveComponentToken(string token, int maxLength) {
        string sanitizedToken = SanitizeToken(token);
        if (sanitizedToken.Length <= maxLength) {
            return sanitizedToken;
        }

        int hashLength = Math.Min(6, maxLength - 1);
        int prefixLength = maxLength - hashLength;
        return sanitizedToken.Substring(0, prefixLength) + ComputeHash(token).Substring(0, hashLength);
    }

    /// <summary>
    /// Resolves one logical file name into an uppercase PS2-safe 8.3 filename.
    /// </summary>
    /// <param name="fileName">Logical file name including its extension.</param>
    /// <returns>Sanitized PS2-safe file name.</returns>
    static string ResolveFileName(string fileName) {
        int extensionSeparatorIndex = fileName.LastIndexOf('.');
        if (extensionSeparatorIndex <= 0) {
            return ResolveComponentToken(fileName, 8);
        }

        string stem = fileName[..extensionSeparatorIndex];
        string extension = fileName[(extensionSeparatorIndex + 1)..];
        string resolvedExtension = ResolveExtension(extension);
        if (string.IsNullOrWhiteSpace(resolvedExtension)) {
            return ResolveComponentToken(stem, 8);
        }

        return ResolveComponentToken(stem, 8) + "." + resolvedExtension;
    }

    /// <summary>
    /// Resolves one logical file extension into an uppercase PS2-safe 3-character extension.
    /// </summary>
    /// <param name="extension">Logical extension without the leading dot.</param>
    /// <returns>Sanitized PS2-safe extension.</returns>
    static string ResolveExtension(string extension) {
        string sanitizedExtension = SanitizeToken(extension.TrimStart('.'));
        if (string.IsNullOrWhiteSpace(sanitizedExtension)) {
            return string.Empty;
        }
        if (sanitizedExtension.Length <= 3) {
            return sanitizedExtension;
        }

        return sanitizedExtension[..3];
    }

    /// <summary>
    /// Sanitizes one logical token into uppercase PS2-safe characters.
    /// </summary>
    /// <param name="token">Logical token to sanitize.</param>
    /// <returns>Uppercase token containing only letters, digits, or underscores.</returns>
    static string SanitizeToken(string token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return "X";
        }

        char[] characters = token.ToUpperInvariant().ToCharArray();
        for (int index = 0; index < characters.Length; index++) {
            char currentCharacter = characters[index];
            bool allowedCharacter = currentCharacter is >= 'A' and <= 'Z'
                || currentCharacter is >= '0' and <= '9'
                || currentCharacter == '_';
            if (!allowedCharacter) {
                characters[index] = '_';
            }
        }

        return new string(characters);
    }

    /// <summary>
    /// Computes one stable uppercase hexadecimal hash used to shorten oversized PS2 path tokens.
    /// </summary>
    /// <param name="token">Logical token that contributes to the hash.</param>
    /// <returns>Stable uppercase hexadecimal hash.</returns>
    static string ComputeHash(string token) {
        uint hash = 2166136261u;
        for (int index = 0; index < token.Length; index++) {
            hash ^= token[index];
            hash *= 16777619u;
        }

        return hash.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
    }
}
