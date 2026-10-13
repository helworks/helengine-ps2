using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace helengine.ps2.builder;

/// <summary>
/// Remembers, per staged output, a fingerprint of the inputs that produced it so repeated builds into the same
/// working root (editor Play) can skip re-cooking and re-writing outputs whose inputs did not change.
/// The fingerprints live in one JSON file at the root of the PS2 staging tree and are only persisted after a
/// staging pass completed successfully.
/// </summary>
internal sealed class Ps2CookFingerprintStore {
    /// <summary>
    /// Version of the PS2 cook output format folded into every fingerprint; bump it whenever a cooker change
    /// must invalidate every previously staged output.
    /// </summary>
    /// <remarks>
    /// Fingerprints only cover cook inputs, not cook code. Bump this whenever the output of the PS2 cookers
    /// (<see cref="Ps2PackedMeshCooker"/>, <see cref="Ps2RuntimeTextureCooker"/>, the PS2 model/texture serializers) or of
    /// the editor importers they call changes, e.g. the helengine.editor GdiFontImporter used to build atlases from raw
    /// .ttf/.otf sources. Otherwise editor Play keeps serving outputs cooked by the old code from its persistent working root.
    /// </remarks>
    public const string CookerVersion = "1";

    /// <summary>
    /// File name of the fingerprint store inside the staging root; the disc layout writer excludes it from packaging.
    /// </summary>
    public const string FileName = ".helengine-cook-fingerprints.json";

    /// <summary>
    /// Size of the read buffer used while hashing source files.
    /// </summary>
    const int HashBufferSize = 81920;

    /// <summary>
    /// Absolute path of the JSON file backing this store.
    /// </summary>
    readonly string StoreFilePath;

    /// <summary>
    /// Fingerprints keyed by normalized output path relative to the staging root.
    /// </summary>
    readonly Dictionary<string, string> Fingerprints;

    /// <summary>
    /// Opens the fingerprint store of one staging root, loading the previously saved fingerprints when the file exists.
    /// </summary>
    /// <param name="stagingRootPath">Absolute PS2 staging root that holds the staged outputs and the store file.</param>
    public Ps2CookFingerprintStore(string stagingRootPath) {
        if (string.IsNullOrWhiteSpace(stagingRootPath)) {
            throw new ArgumentException("Staging root path must be provided for the PS2 cook fingerprint store.", nameof(stagingRootPath));
        }

        StoreFilePath = Path.Combine(Path.GetFullPath(stagingRootPath), FileName);
        Fingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(StoreFilePath)) {
            return;
        }

        Dictionary<string, string> savedFingerprints;
        try {
            savedFingerprints = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(StoreFilePath));
        } catch (JsonException exception) {
            throw new InvalidOperationException(
                $"PS2 cook fingerprint store '{StoreFilePath}' is corrupt. Delete the file so the next build re-cooks every staged output.",
                exception);
        }
        if (savedFingerprints == null) {
            throw new InvalidOperationException($"PS2 cook fingerprint store '{StoreFilePath}' did not contain a fingerprint map.");
        }

        foreach (KeyValuePair<string, string> entry in savedFingerprints) {
            Fingerprints[entry.Key] = entry.Value;
        }
    }

    /// <summary>
    /// Returns whether one staged output can be reused: its recorded fingerprint must equal the current one and the
    /// staged file must still exist.
    /// </summary>
    /// <param name="outputRelativePath">Normalized output path relative to the staging root.</param>
    /// <param name="fingerprint">Fingerprint of the current cook inputs.</param>
    /// <param name="destinationPath">Absolute path of the staged output file.</param>
    /// <returns>True when the staged output already reflects the current inputs; otherwise false.</returns>
    public bool IsUpToDate(string outputRelativePath, string fingerprint, string destinationPath) {
        if (string.IsNullOrWhiteSpace(outputRelativePath)) {
            throw new ArgumentException("Output relative path must be provided.", nameof(outputRelativePath));
        } else if (string.IsNullOrWhiteSpace(fingerprint)) {
            throw new ArgumentException("Fingerprint must be provided.", nameof(fingerprint));
        } else if (string.IsNullOrWhiteSpace(destinationPath)) {
            throw new ArgumentException("Destination path must be provided.", nameof(destinationPath));
        }

        return Fingerprints.TryGetValue(outputRelativePath, out string recordedFingerprint)
            && string.Equals(recordedFingerprint, fingerprint, StringComparison.Ordinal)
            && File.Exists(destinationPath);
    }

    /// <summary>
    /// Records the fingerprint of the inputs that just produced one staged output.
    /// </summary>
    /// <param name="outputRelativePath">Normalized output path relative to the staging root.</param>
    /// <param name="fingerprint">Fingerprint of the inputs used for the write.</param>
    public void Record(string outputRelativePath, string fingerprint) {
        if (string.IsNullOrWhiteSpace(outputRelativePath)) {
            throw new ArgumentException("Output relative path must be provided.", nameof(outputRelativePath));
        } else if (string.IsNullOrWhiteSpace(fingerprint)) {
            throw new ArgumentException("Fingerprint must be provided.", nameof(fingerprint));
        }

        Fingerprints[outputRelativePath] = fingerprint;
    }

    /// <summary>
    /// Drops the recorded fingerprint of one output that is about to be overwritten. Callers forget every output they
    /// will write and <see cref="Save"/> before the first write, so a run interrupted mid-write (exception, cancellation,
    /// truncated file) can never leave a fingerprint describing content the staged file no longer holds.
    /// </summary>
    /// <param name="outputRelativePath">Normalized output path relative to the staging root.</param>
    public void Forget(string outputRelativePath) {
        if (string.IsNullOrWhiteSpace(outputRelativePath)) {
            throw new ArgumentException("Output relative path must be provided.", nameof(outputRelativePath));
        }

        Fingerprints.Remove(outputRelativePath);
    }

    /// <summary>
    /// Writes every known fingerprint to the store file. Called once to persist invalidations before outputs are
    /// overwritten, and once more after the whole staging pass succeeded to persist the new fingerprints.
    /// </summary>
    public void Save() {
        Directory.CreateDirectory(Path.GetDirectoryName(StoreFilePath));
        File.WriteAllText(StoreFilePath, JsonSerializer.Serialize(Fingerprints));
    }

    /// <summary>
    /// Returns whether one file found while walking a staging tree is the fingerprint store itself.
    /// </summary>
    /// <param name="stagingRootPath">Staging root being walked.</param>
    /// <param name="filePath">File found under the staging root.</param>
    /// <returns>True when the file is this store's JSON file; otherwise false.</returns>
    public static bool IsStoreFile(string stagingRootPath, string filePath) {
        if (string.IsNullOrWhiteSpace(stagingRootPath)) {
            throw new ArgumentException("Staging root path must be provided.", nameof(stagingRootPath));
        } else if (string.IsNullOrWhiteSpace(filePath)) {
            throw new ArgumentException("File path must be provided.", nameof(filePath));
        }

        string storeFilePath = Path.Combine(Path.GetFullPath(stagingRootPath), FileName);
        return string.Equals(Path.GetFullPath(filePath), storeFilePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fingerprints one staged artifact from its source file bytes, its artifact kind and its logical artifact id
    /// (the id is embedded into PS2 model payloads whose source model carries none).
    /// </summary>
    /// <param name="sourcePath">Absolute path of the source artifact file.</param>
    /// <param name="artifactKind">Manifest artifact kind, which selects how the artifact is staged.</param>
    /// <param name="logicalArtifactId">Manifest logical artifact id.</param>
    /// <returns>Lowercase hex SHA-256 fingerprint.</returns>
    public static string ComputeSourceFileFingerprint(string sourcePath, string artifactKind, string logicalArtifactId) {
        if (string.IsNullOrWhiteSpace(sourcePath)) {
            throw new ArgumentException("Source path must be provided.", nameof(sourcePath));
        } else if (artifactKind == null) {
            throw new ArgumentNullException(nameof(artifactKind));
        } else if (logicalArtifactId == null) {
            throw new ArgumentNullException(nameof(logicalArtifactId));
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (FileStream sourceStream = File.OpenRead(sourcePath)) {
            byte[] buffer = new byte[HashBufferSize];
            int readCount;
            while ((readCount = sourceStream.Read(buffer, 0, buffer.Length)) > 0) {
                hash.AppendData(buffer, 0, readCount);
            }
        }
        AppendSeparatedText(hash, artifactKind);
        AppendSeparatedText(hash, logicalArtifactId);
        AppendSeparatedText(hash, CookerVersion);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// Fingerprints one texture work item from the editor-computed source content and settings hashes plus the
    /// output logical id embedded into the cooked texture, without re-reading the source file.
    /// </summary>
    /// <param name="sourceContentHash">Editor-computed hash of the texture source file contents.</param>
    /// <param name="settingsHash">Editor-computed hash of the serialized PS2 texture settings.</param>
    /// <param name="outputLogicalArtifactId">Logical id written into the cooked texture payload.</param>
    /// <returns>Lowercase hex SHA-256 fingerprint.</returns>
    public static string ComputeWorkItemFingerprint(string sourceContentHash, string settingsHash, string outputLogicalArtifactId) {
        if (string.IsNullOrWhiteSpace(sourceContentHash)) {
            throw new ArgumentException("Source content hash must be provided.", nameof(sourceContentHash));
        } else if (string.IsNullOrWhiteSpace(settingsHash)) {
            throw new ArgumentException("Settings hash must be provided.", nameof(settingsHash));
        } else if (string.IsNullOrWhiteSpace(outputLogicalArtifactId)) {
            throw new ArgumentException("Output logical artifact id must be provided.", nameof(outputLogicalArtifactId));
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendSeparatedText(hash, sourceContentHash);
        AppendSeparatedText(hash, settingsHash);
        AppendSeparatedText(hash, outputLogicalArtifactId);
        AppendSeparatedText(hash, CookerVersion);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// Appends one text input followed by a zero byte so adjacent inputs cannot run together into the same byte stream.
    /// </summary>
    /// <param name="hash">Hash being accumulated.</param>
    /// <param name="value">Text input to append.</param>
    static void AppendSeparatedText(IncrementalHash hash, string value) {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }
}
