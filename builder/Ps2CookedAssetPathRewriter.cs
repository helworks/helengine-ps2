using helengine.files;

namespace helengine.ps2.builder;

/// <summary>
/// Rewrites staged packaged PS2 assets so file-backed runtime references use physical disc paths before native compilation.
/// </summary>
public sealed class Ps2CookedAssetPathRewriter {
    /// <summary>
    /// Stable component type id used by packaged mesh payloads.
    /// </summary>
    const string MeshComponentTypeId = "helengine.MeshComponent";

    /// <summary>
    /// Stable component type id used by packaged text payloads.
    /// </summary>
    const string TextComponentTypeId = "helengine.TextComponent";

    /// <summary>
    /// Stable component type id used by packaged FPS payloads.
    /// </summary>
    const string FPSComponentTypeId = "helengine.FPSComponent";

    /// <summary>
    /// Rewrites staged packaged scene and PS2 material assets to use physical disc paths.
    /// </summary>
    /// <param name="stagingRootPath">Staged package root that contains cooked artifacts.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    public void Rewrite(string stagingRootPath, IReadOnlyDictionary<string, string> logicalToPhysicalPaths) {
        if (string.IsNullOrWhiteSpace(stagingRootPath)) {
            throw new ArgumentException("Staging root path must be provided.", nameof(stagingRootPath));
        }
        if (logicalToPhysicalPaths == null) {
            throw new ArgumentNullException(nameof(logicalToPhysicalPaths));
        }
        if (!Directory.Exists(stagingRootPath)) {
            throw new DirectoryNotFoundException($"Staging root '{stagingRootPath}' was not found.");
        }

        string[] filePaths = Directory.GetFiles(stagingRootPath, "*.hasset", SearchOption.AllDirectories);
        for (int fileIndex = 0; fileIndex < filePaths.Length; fileIndex++) {
            string filePath = filePaths[fileIndex];
            string logicalRelativePath = Path.GetRelativePath(stagingRootPath, filePath).Replace('\\', '/');
            if (!ShouldRewriteStagedAsset(logicalRelativePath)) {
                continue;
            }

            RewriteStagedAsset(filePath, logicalToPhysicalPaths);
        }
    }

    /// <summary>
    /// Returns whether one staged packaged asset should be rewritten before PS2 native compilation.
    /// </summary>
    /// <param name="logicalRelativePath">Logical staged path relative to the staged package root.</param>
    /// <returns>True when the asset embeds file-backed runtime paths that must become physical disc paths; otherwise false.</returns>
    static bool ShouldRewriteStagedAsset(string logicalRelativePath) {
        if (string.IsNullOrWhiteSpace(logicalRelativePath)) {
            return false;
        }

        return logicalRelativePath.StartsWith("cooked/scenes/", StringComparison.OrdinalIgnoreCase)
            || logicalRelativePath.Contains("/materials/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Rewrites one staged packaged asset when its payload embeds file-backed runtime paths.
    /// </summary>
    /// <param name="filePath">Absolute staged packaged asset path.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    void RewriteStagedAsset(string filePath, IReadOnlyDictionary<string, string> logicalToPhysicalPaths) {
        Asset asset;
        bool changed = false;
        using (FileStream readStream = File.OpenRead(filePath)) {
            asset = AssetSerializer.Deserialize(readStream);
            if (asset is SceneAsset sceneAsset) {
                changed = RewriteSceneAsset(sceneAsset, logicalToPhysicalPaths);
            } else if (asset is Ps2MaterialAsset ps2MaterialAsset) {
                changed = RewritePs2MaterialAsset(ps2MaterialAsset, logicalToPhysicalPaths);
            }
        }

        if (!changed) {
            return;
        }

        File.WriteAllBytes(filePath, helengine.files.AssetSerializer.SerializeToBytes(asset));
    }

    /// <summary>
    /// Rewrites packaged scene references embedded in one staged scene asset.
    /// </summary>
    /// <param name="sceneAsset">Packaged scene asset to rewrite.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    /// <returns>True when any embedded reference changed; otherwise false.</returns>
    bool RewriteSceneAsset(SceneAsset sceneAsset, IReadOnlyDictionary<string, string> logicalToPhysicalPaths) {
        bool changed = RewriteSceneAssetReferences(sceneAsset.AssetReferences, logicalToPhysicalPaths);
        SceneEntityAsset[] rootEntities = sceneAsset.RootEntities ?? Array.Empty<SceneEntityAsset>();
        for (int entityIndex = 0; entityIndex < rootEntities.Length; entityIndex++) {
            if (RewriteSceneEntity(rootEntities[entityIndex], logicalToPhysicalPaths)) {
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Rewrites every packaged file-backed reference embedded in one scene entity subtree.
    /// </summary>
    /// <param name="entityAsset">Scene entity subtree to rewrite.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    /// <returns>True when any embedded reference changed; otherwise false.</returns>
    bool RewriteSceneEntity(SceneEntityAsset entityAsset, IReadOnlyDictionary<string, string> logicalToPhysicalPaths) {
        bool changed = false;

        SceneComponentAssetRecord[] componentRecords = entityAsset.Components ?? Array.Empty<SceneComponentAssetRecord>();
        for (int componentIndex = 0; componentIndex < componentRecords.Length; componentIndex++) {
            SceneComponentAssetRecord rewrittenRecord = RewriteComponentRecord(componentRecords[componentIndex], logicalToPhysicalPaths, out bool recordChanged);
            if (recordChanged) {
                componentRecords[componentIndex] = rewrittenRecord;
                changed = true;
            }
        }

        SceneEntityAsset[] childEntities = entityAsset.Children ?? Array.Empty<SceneEntityAsset>();
        for (int childIndex = 0; childIndex < childEntities.Length; childIndex++) {
            if (RewriteSceneEntity(childEntities[childIndex], logicalToPhysicalPaths)) {
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Rewrites one packaged component record when its payload embeds file-backed scene references.
    /// </summary>
    /// <param name="record">Packaged component record to rewrite.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    /// <param name="changed">Receives whether the payload changed.</param>
    /// <returns>Rewritten component record.</returns>
    SceneComponentAssetRecord RewriteComponentRecord(
        SceneComponentAssetRecord record,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        out bool changed) {
        if (record == null) {
            throw new ArgumentNullException(nameof(record));
        }

        changed = false;
        if (string.Equals(record.ComponentTypeId, MeshComponentTypeId, StringComparison.OrdinalIgnoreCase)) {
            return RewriteMeshComponentRecord(record, logicalToPhysicalPaths, out changed);
        } else if (string.Equals(record.ComponentTypeId, TextComponentTypeId, StringComparison.OrdinalIgnoreCase)) {
            return RewriteTextComponentRecord(record, logicalToPhysicalPaths, out changed);
        } else if (string.Equals(record.ComponentTypeId, FPSComponentTypeId, StringComparison.OrdinalIgnoreCase)) {
            return RewriteFpsComponentRecord(record, logicalToPhysicalPaths, out changed);
        }

        return record;
    }

    /// <summary>
    /// Rewrites packaged mesh model and material references to physical disc paths.
    /// </summary>
    /// <param name="record">Packaged mesh component record.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    /// <param name="changed">Receives whether the payload changed.</param>
    /// <returns>Rewritten mesh component record.</returns>
    SceneComponentAssetRecord RewriteMeshComponentRecord(
        SceneComponentAssetRecord record,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        out bool changed) {
        using MemoryStream readStream = new(record.Payload ?? Array.Empty<byte>(), false);
        using EngineBinaryReader reader = EngineBinaryReader.Create(readStream, EngineBinaryEndianness.LittleEndian);
        byte version = reader.ReadByte();
        if (version != 1) {
            throw new InvalidOperationException($"Unsupported mesh component payload version '{version}'.");
        }

        SceneAssetReference modelReference = ReadOptionalReference(reader);
        SceneAssetReference materialReference = ReadOptionalReference(reader);
        byte[] remainingBytes = ReadRemainingBytes(readStream);

        changed = false;
        if (RewriteReference(modelReference, logicalToPhysicalPaths)) {
            changed = true;
        }
        if (RewriteReference(materialReference, logicalToPhysicalPaths)) {
            changed = true;
        }
        if (!changed) {
            return record;
        }

        using MemoryStream writeStream = new();
        using EngineBinaryWriter writer = EngineBinaryWriter.Create(writeStream, EngineBinaryEndianness.LittleEndian);
        writer.WriteByte(version);
        WriteOptionalReference(writer, modelReference);
        WriteOptionalReference(writer, materialReference);
        writeStream.Write(remainingBytes, 0, remainingBytes.Length);
        return new SceneComponentAssetRecord {
            ComponentTypeId = record.ComponentTypeId,
            ComponentIndex = record.ComponentIndex,
            Payload = writeStream.ToArray()
        };
    }

    /// <summary>
    /// Rewrites packaged text font references to physical disc paths.
    /// </summary>
    /// <param name="record">Packaged text component record.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    /// <param name="changed">Receives whether the payload changed.</param>
    /// <returns>Rewritten text component record.</returns>
    SceneComponentAssetRecord RewriteTextComponentRecord(
        SceneComponentAssetRecord record,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        out bool changed) {
        using MemoryStream readStream = new(record.Payload ?? Array.Empty<byte>(), false);
        using EngineBinaryReader reader = EngineBinaryReader.Create(readStream, EngineBinaryEndianness.LittleEndian);
        byte version = reader.ReadByte();
        if (version != 1) {
            throw new InvalidOperationException($"Unsupported text component payload version '{version}'.");
        }

        SceneAssetReference fontReference = ReadOptionalReference(reader);
        byte[] remainingBytes = ReadRemainingBytes(readStream);

        changed = RewriteReference(fontReference, logicalToPhysicalPaths);
        if (!changed) {
            return record;
        }

        using MemoryStream writeStream = new();
        using EngineBinaryWriter writer = EngineBinaryWriter.Create(writeStream, EngineBinaryEndianness.LittleEndian);
        writer.WriteByte(version);
        WriteOptionalReference(writer, fontReference);
        writeStream.Write(remainingBytes, 0, remainingBytes.Length);
        return new SceneComponentAssetRecord {
            ComponentTypeId = record.ComponentTypeId,
            ComponentIndex = record.ComponentIndex,
            Payload = writeStream.ToArray()
        };
    }

    /// <summary>
    /// Rewrites packaged FPS overlay font references to physical disc paths.
    /// </summary>
    /// <param name="record">Packaged FPS component record.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    /// <param name="changed">Receives whether the payload changed.</param>
    /// <returns>Rewritten FPS component record.</returns>
    SceneComponentAssetRecord RewriteFpsComponentRecord(
        SceneComponentAssetRecord record,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        out bool changed) {
        using MemoryStream readStream = new(record.Payload ?? Array.Empty<byte>(), false);
        using EngineBinaryReader reader = EngineBinaryReader.Create(readStream, EngineBinaryEndianness.LittleEndian);
        byte version = reader.ReadByte();
        if (version != 1 && version != 2) {
            throw new InvalidOperationException($"Unsupported FPS component payload version '{version}'.");
        }
        if (version == 1) {
            changed = false;
            return record;
        }

        SceneAssetReference fontReference = ReadOptionalReference(reader);
        byte[] remainingBytes = ReadRemainingBytes(readStream);

        changed = RewriteReference(fontReference, logicalToPhysicalPaths);
        if (!changed) {
            return record;
        }

        using MemoryStream writeStream = new();
        using EngineBinaryWriter writer = EngineBinaryWriter.Create(writeStream, EngineBinaryEndianness.LittleEndian);
        writer.WriteByte(version);
        WriteOptionalReference(writer, fontReference);
        writeStream.Write(remainingBytes, 0, remainingBytes.Length);
        return new SceneComponentAssetRecord {
            ComponentTypeId = record.ComponentTypeId,
            ComponentIndex = record.ComponentIndex,
            Payload = writeStream.ToArray()
        };
    }

    /// <summary>
    /// Rewrites one PS2 cooked material texture path to its physical disc path.
    /// </summary>
    /// <param name="materialAsset">PS2 material asset to rewrite.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    /// <returns>True when the material texture path changed; otherwise false.</returns>
    bool RewritePs2MaterialAsset(Ps2MaterialAsset materialAsset, IReadOnlyDictionary<string, string> logicalToPhysicalPaths) {
        if (materialAsset == null) {
            throw new ArgumentNullException(nameof(materialAsset));
        }
        if (string.IsNullOrWhiteSpace(materialAsset.TextureRelativePath)) {
            return false;
        }

        if (!TryResolvePhysicalRuntimePath(materialAsset.TextureRelativePath, logicalToPhysicalPaths, out string physicalRuntimePath)) {
            throw new InvalidOperationException($"The staged PS2 material texture path '{materialAsset.TextureRelativePath}' did not resolve to a physical disc path.");
        }

        materialAsset.TextureRelativePath = physicalRuntimePath;
        return true;
    }

    /// <summary>
    /// Rewrites one scene-asset reference array to physical disc paths.
    /// </summary>
    /// <param name="references">Scene-asset references to rewrite.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    /// <returns>True when any reference changed; otherwise false.</returns>
    bool RewriteSceneAssetReferences(SceneAssetReference[] references, IReadOnlyDictionary<string, string> logicalToPhysicalPaths) {
        bool changed = false;
        if (references == null) {
            return false;
        }

        for (int referenceIndex = 0; referenceIndex < references.Length; referenceIndex++) {
            if (RewriteReference(references[referenceIndex], logicalToPhysicalPaths)) {
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Rewrites one packaged file-backed scene reference to a physical PS2 runtime path.
    /// </summary>
    /// <param name="reference">Scene reference to rewrite.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    /// <returns>True when the reference changed; otherwise false.</returns>
    bool RewriteReference(SceneAssetReference reference, IReadOnlyDictionary<string, string> logicalToPhysicalPaths) {
        if (reference == null) {
            return false;
        }
        if (reference.SourceKind != SceneAssetReferenceSourceKind.FileSystem) {
            return false;
        }
        if (string.IsNullOrWhiteSpace(reference.RelativePath)) {
            throw new InvalidOperationException("Packaged file-backed scene references must include a relative path.");
        }

        if (!TryResolvePhysicalRuntimePath(reference.RelativePath, logicalToPhysicalPaths, out string physicalRuntimePath)) {
            throw new InvalidOperationException($"The staged PS2 asset path '{reference.RelativePath}' did not resolve to a physical disc path.");
        }

        if (string.Equals(reference.RelativePath, physicalRuntimePath, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        reference.RelativePath = physicalRuntimePath;
        return true;
    }

    /// <summary>
    /// Resolves one packaged logical runtime path to its physical PS2 runtime path.
    /// </summary>
    /// <param name="assetPath">Packaged logical path or already-physical runtime path.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    /// <param name="physicalRuntimePath">Receives the physical PS2 runtime path when available.</param>
    /// <returns>True when the path resolved; otherwise false.</returns>
    bool TryResolvePhysicalRuntimePath(
        string assetPath,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        out string physicalRuntimePath) {
        if (IsPhysicalRuntimePath(assetPath)) {
            physicalRuntimePath = NormalizePhysicalRuntimePath(assetPath);
            return true;
        }

        string normalizedLogicalPath = assetPath.Replace('\\', '/');
        if (!logicalToPhysicalPaths.TryGetValue(normalizedLogicalPath, out string physicalDiscPath) || string.IsNullOrWhiteSpace(physicalDiscPath)) {
            physicalRuntimePath = string.Empty;
            return false;
        }

        physicalRuntimePath = BuildRuntimePhysicalPath(physicalDiscPath);
        return true;
    }

    /// <summary>
    /// Returns whether one runtime path already addresses a concrete PS2 disc location.
    /// </summary>
    /// <param name="assetPath">Runtime path to inspect.</param>
    /// <returns>True when the path already targets a physical PS2 disc location; otherwise false.</returns>
    static bool IsPhysicalRuntimePath(string assetPath) {
        if (string.IsNullOrWhiteSpace(assetPath)) {
            return false;
        }

        return assetPath.StartsWith("cdrom0:\\", StringComparison.OrdinalIgnoreCase)
            || assetPath.StartsWith("\\", StringComparison.Ordinal);
    }

    /// <summary>
    /// Normalizes one already-physical PS2 runtime path into the rooted `cdrom0:` form.
    /// </summary>
    /// <param name="assetPath">Physical PS2 runtime path to normalize.</param>
    /// <returns>Normalized rooted `cdrom0:` runtime path.</returns>
    static string NormalizePhysicalRuntimePath(string assetPath) {
        if (assetPath.StartsWith("cdrom0:\\", StringComparison.OrdinalIgnoreCase)) {
            return assetPath;
        }

        return BuildRuntimePhysicalPath(assetPath);
    }

    /// <summary>
    /// Builds one rooted PS2 runtime path from a physical disc-relative path.
    /// </summary>
    /// <param name="physicalDiscPath">Physical disc path stored in the staged PS2 path map.</param>
    /// <returns>Rooted PS2 runtime path.</returns>
    static string BuildRuntimePhysicalPath(string physicalDiscPath) {
        if (string.IsNullOrWhiteSpace(physicalDiscPath)) {
            throw new ArgumentException("Physical disc path must be provided.", nameof(physicalDiscPath));
        }
        if (physicalDiscPath.StartsWith("cdrom0:\\", StringComparison.OrdinalIgnoreCase)) {
            return physicalDiscPath;
        }

        string normalizedPhysicalPath = physicalDiscPath.Replace('/', '\\');
        if (!normalizedPhysicalPath.StartsWith("\\", StringComparison.Ordinal)) {
            normalizedPhysicalPath = "\\" + normalizedPhysicalPath.TrimStart('\\');
        }

        return "cdrom0:" + normalizedPhysicalPath;
    }

    /// <summary>
    /// Reads one optional scene reference from a packaged component payload.
    /// </summary>
    /// <param name="reader">Reader positioned at the optional reference payload.</param>
    /// <returns>Decoded scene reference when present; otherwise null.</returns>
    static SceneAssetReference ReadOptionalReference(EngineBinaryReader reader) {
        if (reader.ReadByte() == 0) {
            return null;
        }

        return new SceneAssetReference {
            SourceKind = (SceneAssetReferenceSourceKind)reader.ReadInt32(),
            RelativePath = reader.ReadString(),
            ProviderId = reader.ReadString(),
            AssetId = reader.ReadString()
        };
    }

    /// <summary>
    /// Writes one optional scene reference into a packaged component payload.
    /// </summary>
    /// <param name="writer">Writer that receives the optional reference payload.</param>
    /// <param name="reference">Scene reference to write.</param>
    static void WriteOptionalReference(EngineBinaryWriter writer, SceneAssetReference reference) {
        if (reference == null) {
            writer.WriteByte(0);
            return;
        }

        writer.WriteByte(1);
        writer.WriteInt32((int)reference.SourceKind);
        writer.WriteString(reference.RelativePath ?? string.Empty);
        writer.WriteString(reference.ProviderId ?? string.Empty);
        writer.WriteString(reference.AssetId ?? string.Empty);
    }

    /// <summary>
    /// Reads every remaining byte from the current stream position.
    /// </summary>
    /// <param name="stream">Source stream positioned after the rewritten reference payloads.</param>
    /// <returns>Remaining raw payload bytes.</returns>
    static byte[] ReadRemainingBytes(Stream stream) {
        long remainingLength = stream.Length - stream.Position;
        if (remainingLength <= 0) {
            return Array.Empty<byte>();
        }

        byte[] bytes = new byte[(int)remainingLength];
        int readCount = stream.Read(bytes, 0, bytes.Length);
        if (readCount != bytes.Length) {
            throw new InvalidOperationException("Could not read the remaining component payload bytes.");
        }

        return bytes;
    }
}

