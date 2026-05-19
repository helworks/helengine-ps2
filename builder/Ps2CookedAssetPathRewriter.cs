using helengine.files;

namespace helengine.ps2.builder;

/// <summary>
/// Rewrites staged packaged PS2 assets so file-backed runtime references use physical disc paths before native compilation.
/// </summary>
public sealed class Ps2CookedAssetPathRewriter {
    static readonly string DebugLogPath = Path.Combine(
        AppContext.BaseDirectory,
        "ps2-cooked-asset-path-rewriter.log");
    /// <summary>
    /// Source material extension used by authored project assets before platform cooking.
    /// </summary>
    const string SourceMaterialExtension = ".helmat";

    /// <summary>
    /// Cooked material extension emitted by the platform cook pipeline.
    /// </summary>
    const string CookedMaterialExtension = ".hasset";

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

        File.WriteAllText(DebugLogPath, string.Empty);

        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths = BuildRawMaterialLogicalToCookedLogicalPathMap(stagingRootPath);
        IReadOnlyDictionary<string, string> rawMaterialPhysicalToCookedPhysicalPaths = BuildRawMaterialPhysicalToCookedPhysicalPathMap(
            logicalToPhysicalPaths,
            rawMaterialLogicalToCookedLogicalPaths);
        string[] filePaths = Directory.GetFiles(stagingRootPath, "*.hasset", SearchOption.AllDirectories);
        for (int fileIndex = 0; fileIndex < filePaths.Length; fileIndex++) {
            string filePath = filePaths[fileIndex];
            if (!IsSerializedAssetRecord(filePath)) {
                continue;
            }

            string logicalRelativePath = Path.GetRelativePath(stagingRootPath, filePath).Replace('\\', '/');
            if (!ShouldRewriteStagedAsset(logicalRelativePath)) {
                continue;
            }

            File.AppendAllText(DebugLogPath, $"rewrite={logicalRelativePath}{Environment.NewLine}");

            RewriteStagedAsset(filePath, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths);
        }
    }

    /// <summary>
    /// Builds a logical raw-material to cooked-material lookup keyed by asset id so runtime scene references can be redirected onto PS2-cooked payloads.
    /// </summary>
    /// <param name="stagingRootPath">Staged package root that contains cooked and raw packaged assets.</param>
    /// <returns>Logical raw material paths mapped to logical cooked material paths for the same asset id.</returns>
    static IReadOnlyDictionary<string, string> BuildRawMaterialLogicalToCookedLogicalPathMap(string stagingRootPath) {
        Dictionary<string, string> cookedMaterialLogicalPathsByAssetId = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> rawMaterialAssetIdsByLogicalPath = new(StringComparer.OrdinalIgnoreCase);

        string[] filePaths = Directory.GetFiles(stagingRootPath, "*.hasset", SearchOption.AllDirectories);
        for (int fileIndex = 0; fileIndex < filePaths.Length; fileIndex++) {
            string filePath = filePaths[fileIndex];
            if (!IsSerializedAssetRecord(filePath)) {
                continue;
            }

            string logicalRelativePath = Path.GetRelativePath(stagingRootPath, filePath).Replace('\\', '/');
            using FileStream stream = File.OpenRead(filePath);
            Asset asset = AssetSerializer.Deserialize(stream);
            if (asset is Ps2MaterialAsset ps2MaterialAsset) {
                if (!string.IsNullOrWhiteSpace(ps2MaterialAsset.Id)) {
                    cookedMaterialLogicalPathsByAssetId[ps2MaterialAsset.Id] = logicalRelativePath;
                }
            } else if (asset is MaterialAsset materialAsset && !logicalRelativePath.StartsWith("cooked/", StringComparison.OrdinalIgnoreCase)) {
                if (!string.IsNullOrWhiteSpace(materialAsset.Id)) {
                    rawMaterialAssetIdsByLogicalPath[logicalRelativePath] = materialAsset.Id;
                }
            }
        }

        Dictionary<string, string> rawToCookedLogicalPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string rawLogicalPath, string assetId) in rawMaterialAssetIdsByLogicalPath) {
            if (cookedMaterialLogicalPathsByAssetId.TryGetValue(assetId, out string cookedLogicalPath)
                && !string.IsNullOrWhiteSpace(cookedLogicalPath)) {
                rawToCookedLogicalPaths[rawLogicalPath] = cookedLogicalPath;
            }
        }

        for (int fileIndex = 0; fileIndex < filePaths.Length; fileIndex++) {
            string filePath = filePaths[fileIndex];
            string logicalRelativePath = Path.GetRelativePath(stagingRootPath, filePath).Replace('\\', '/');
            if (logicalRelativePath.StartsWith("cooked/", StringComparison.OrdinalIgnoreCase)
                || rawToCookedLogicalPaths.ContainsKey(logicalRelativePath)
                || !TryBuildMaterialAssetIdFromLogicalPath(logicalRelativePath, out string inferredAssetId)) {
                continue;
            }

            if (cookedMaterialLogicalPathsByAssetId.TryGetValue(inferredAssetId, out string cookedLogicalPath)
                && !string.IsNullOrWhiteSpace(cookedLogicalPath)) {
                rawToCookedLogicalPaths[logicalRelativePath] = cookedLogicalPath;
            }
        }

        return rawToCookedLogicalPaths;
    }

    /// <summary>
    /// Builds a raw-material physical-runtime to cooked-material physical-runtime lookup so already-physical raw scene references can still be redirected onto PS2 cooked payloads.
    /// </summary>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    /// <param name="rawMaterialLogicalToCookedLogicalPaths">Logical raw-material to cooked-material mappings keyed by asset id.</param>
    /// <returns>Physical raw runtime material paths mapped to physical cooked runtime material paths.</returns>
    static IReadOnlyDictionary<string, string> BuildRawMaterialPhysicalToCookedPhysicalPathMap(
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths) {
        if (logicalToPhysicalPaths == null) {
            throw new ArgumentNullException(nameof(logicalToPhysicalPaths));
        } else if (rawMaterialLogicalToCookedLogicalPaths == null) {
            throw new ArgumentNullException(nameof(rawMaterialLogicalToCookedLogicalPaths));
        }

        Dictionary<string, string> rawToCookedPhysicalPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string rawLogicalPath, string cookedLogicalPath) in rawMaterialLogicalToCookedLogicalPaths) {
            if (string.IsNullOrWhiteSpace(rawLogicalPath) || string.IsNullOrWhiteSpace(cookedLogicalPath)) {
                continue;
            }
            if (!logicalToPhysicalPaths.TryGetValue(rawLogicalPath, out string rawPhysicalDiscPath)
                || string.IsNullOrWhiteSpace(rawPhysicalDiscPath)
                || !logicalToPhysicalPaths.TryGetValue(cookedLogicalPath, out string cookedPhysicalDiscPath)
                || string.IsNullOrWhiteSpace(cookedPhysicalDiscPath)) {
                continue;
            }

            rawToCookedPhysicalPaths[BuildRuntimePhysicalPath(rawPhysicalDiscPath)] = BuildRuntimePhysicalPath(cookedPhysicalDiscPath);
        }

        foreach ((string logicalPath, string physicalDiscPath) in logicalToPhysicalPaths) {
            if (string.IsNullOrWhiteSpace(logicalPath)
                || string.IsNullOrWhiteSpace(physicalDiscPath)
                || logicalPath.StartsWith("cooked/", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            string cookedLogicalPath = "cooked/" + logicalPath.Replace('\\', '/').TrimStart('/');
            if (!logicalToPhysicalPaths.TryGetValue(cookedLogicalPath, out string cookedPhysicalDiscPath)
                || string.IsNullOrWhiteSpace(cookedPhysicalDiscPath)) {
                continue;
            }

            string rawRuntimePath = BuildRuntimePhysicalPath(physicalDiscPath);
            if (!rawToCookedPhysicalPaths.ContainsKey(rawRuntimePath)) {
                rawToCookedPhysicalPaths[rawRuntimePath] = BuildRuntimePhysicalPath(cookedPhysicalDiscPath);
            }
        }

        return rawToCookedPhysicalPaths;
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
            || logicalRelativePath.Contains("/materials/", StringComparison.OrdinalIgnoreCase)
            || logicalRelativePath.Contains("/mat/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns whether one staged HELE file contains a serialized asset payload rather than editor-side metadata.
    /// </summary>
    /// <param name="filePath">Absolute staged file path to inspect.</param>
    /// <returns>True when the file stores an asset record; otherwise false.</returns>
    static bool IsSerializedAssetRecord(string filePath) {
        if (string.IsNullOrWhiteSpace(filePath)) {
            throw new ArgumentException("File path must be provided.", nameof(filePath));
        }

        using FileStream stream = File.OpenRead(filePath);
        EngineBinaryHeader header = EngineBinaryHeaderSerializer.Read(stream);
        return header.RecordKind == (ushort)EditorBinaryRecordKind.Asset;
    }

    /// <summary>
    /// Rewrites one staged packaged asset when its payload embeds file-backed runtime paths.
    /// </summary>
    /// <param name="filePath">Absolute staged packaged asset path.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    void RewriteStagedAsset(
        string filePath,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialPhysicalToCookedPhysicalPaths) {
        Asset asset;
        bool changed = false;
        using (FileStream readStream = File.OpenRead(filePath)) {
            asset = AssetSerializer.Deserialize(readStream);
            if (asset is SceneAsset sceneAsset) {
                changed = RewriteSceneAsset(sceneAsset, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths);
            } else if (asset is Ps2MaterialAsset ps2MaterialAsset) {
                changed = RewritePs2MaterialAsset(ps2MaterialAsset, logicalToPhysicalPaths, rawMaterialPhysicalToCookedPhysicalPaths);
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
    bool RewriteSceneAsset(
        SceneAsset sceneAsset,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialPhysicalToCookedPhysicalPaths) {
        bool changed = RewriteSceneAssetReferences(sceneAsset.AssetReferences, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths);
        SceneEntityAsset[] rootEntities = sceneAsset.RootEntities ?? Array.Empty<SceneEntityAsset>();
        for (int entityIndex = 0; entityIndex < rootEntities.Length; entityIndex++) {
            if (RewriteSceneEntity(rootEntities[entityIndex], logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths)) {
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
    bool RewriteSceneEntity(
        SceneEntityAsset entityAsset,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialPhysicalToCookedPhysicalPaths) {
        bool changed = false;

        SceneComponentAssetRecord[] componentRecords = entityAsset.Components ?? Array.Empty<SceneComponentAssetRecord>();
        for (int componentIndex = 0; componentIndex < componentRecords.Length; componentIndex++) {
            SceneComponentAssetRecord rewrittenRecord = RewriteComponentRecord(componentRecords[componentIndex], logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths, out bool recordChanged);
            if (recordChanged) {
                componentRecords[componentIndex] = rewrittenRecord;
                changed = true;
            }
        }

        SceneEntityPlatformComponentOverrideAsset[] platformComponentOverrides = entityAsset.PlatformComponentOverrides ?? Array.Empty<SceneEntityPlatformComponentOverrideAsset>();
        for (int overrideIndex = 0; overrideIndex < platformComponentOverrides.Length; overrideIndex++) {
            if (RewritePlatformComponentOverride(platformComponentOverrides[overrideIndex], logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths)) {
                changed = true;
            }
        }

        SceneEntityAsset[] childEntities = entityAsset.Children ?? Array.Empty<SceneEntityAsset>();
        for (int childIndex = 0; childIndex < childEntities.Length; childIndex++) {
            if (RewriteSceneEntity(childEntities[childIndex], logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths)) {
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Rewrites every file-backed reference embedded in one platform-specific component-override set.
    /// </summary>
    /// <param name="componentOverride">Platform-specific component override set to rewrite.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    /// <returns>True when any nested component payload changed; otherwise false.</returns>
    bool RewritePlatformComponentOverride(
        SceneEntityPlatformComponentOverrideAsset componentOverride,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialPhysicalToCookedPhysicalPaths) {
        if (componentOverride == null) {
            return false;
        }

        bool changed = false;
        SceneEntityPlatformAddedComponentAsset[] addedComponents = componentOverride.AddedComponents ?? Array.Empty<SceneEntityPlatformAddedComponentAsset>();
        for (int componentIndex = 0; componentIndex < addedComponents.Length; componentIndex++) {
            SceneEntityPlatformAddedComponentAsset addedComponent = addedComponents[componentIndex];
            if (addedComponent?.Component == null) {
                continue;
            }

            SceneComponentAssetRecord rewrittenRecord = RewriteComponentRecord(addedComponent.Component, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths, out bool recordChanged);
            if (recordChanged) {
                addedComponent.Component = rewrittenRecord;
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
        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialPhysicalToCookedPhysicalPaths,
        out bool changed) {
        if (record == null) {
            throw new ArgumentNullException(nameof(record));
        }

        changed = false;
        if (string.Equals(record.ComponentTypeId, MeshComponentTypeId, StringComparison.OrdinalIgnoreCase)) {
            return RewriteMeshComponentRecord(record, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths, out changed);
        } else if (string.Equals(record.ComponentTypeId, TextComponentTypeId, StringComparison.OrdinalIgnoreCase)) {
            return RewriteTextComponentRecord(record, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths, out changed);
        } else if (string.Equals(record.ComponentTypeId, FPSComponentTypeId, StringComparison.OrdinalIgnoreCase)) {
            return RewriteFpsComponentRecord(record, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths, out changed);
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
        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialPhysicalToCookedPhysicalPaths,
        out bool changed) {
        using MemoryStream readStream = new(record.Payload ?? Array.Empty<byte>(), false);
        using EngineBinaryReader reader = EngineBinaryReader.Create(readStream, EngineBinaryEndianness.LittleEndian);
        MeshComponentScenePayloadSerializer.Read(
            reader,
            out SceneAssetReference modelReference,
            out SceneAssetReference[] materialReferences,
            out byte renderOrder3D);

        changed = false;
        if (RewriteReference(modelReference, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths)) {
            changed = true;
        }
        if (RewriteSceneAssetReferences(materialReferences, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths)) {
            changed = true;
        }
        if (!changed) {
            return record;
        }

        using MemoryStream writeStream = new();
        using EngineBinaryWriter writer = EngineBinaryWriter.Create(writeStream, EngineBinaryEndianness.LittleEndian);
        MeshComponentScenePayloadSerializer.Write(writer, modelReference, materialReferences, renderOrder3D);
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
        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialPhysicalToCookedPhysicalPaths,
        out bool changed) {
        using MemoryStream readStream = new(record.Payload ?? Array.Empty<byte>(), false);
        using EngineBinaryReader reader = EngineBinaryReader.Create(readStream, EngineBinaryEndianness.LittleEndian);
        byte version = reader.ReadByte();
        if (version != 1) {
            throw new InvalidOperationException($"Unsupported text component payload version '{version}'.");
        }

        SceneAssetReference fontReference = ReadOptionalReference(reader);
        byte[] remainingBytes = ReadRemainingBytes(readStream);

        changed = RewriteReference(fontReference, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths);
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
        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialPhysicalToCookedPhysicalPaths,
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

        changed = RewriteReference(fontReference, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths);
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
    bool RewritePs2MaterialAsset(
        Ps2MaterialAsset materialAsset,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialPhysicalToCookedPhysicalPaths) {
        if (materialAsset == null) {
            throw new ArgumentNullException(nameof(materialAsset));
        }
        if (string.IsNullOrWhiteSpace(materialAsset.TextureRelativePath)) {
            return false;
        }

        if (!TryResolvePhysicalRuntimePath(materialAsset.TextureRelativePath, logicalToPhysicalPaths, null, rawMaterialPhysicalToCookedPhysicalPaths, out string physicalRuntimePath)) {
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
    bool RewriteSceneAssetReferences(
        SceneAssetReference[] references,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialPhysicalToCookedPhysicalPaths) {
        bool changed = false;
        if (references == null) {
            return false;
        }

        for (int referenceIndex = 0; referenceIndex < references.Length; referenceIndex++) {
            if (RewriteReference(references[referenceIndex], logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths)) {
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
    bool RewriteReference(
        SceneAssetReference reference,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialPhysicalToCookedPhysicalPaths) {
        if (reference == null) {
            return false;
        }
        if (reference.SourceKind != SceneAssetReferenceSourceKind.FileSystem
            && reference.SourceKind != SceneAssetReferenceSourceKind.Generated) {
            return false;
        }
        if (string.IsNullOrWhiteSpace(reference.RelativePath)) {
            throw new InvalidOperationException("Packaged file-backed scene references must include a relative path.");
        }

        if (!TryResolvePhysicalRuntimePath(reference.RelativePath, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths, out string physicalRuntimePath)) {
            throw new InvalidOperationException($"The staged PS2 asset path '{reference.RelativePath}' did not resolve to a physical disc path.");
        }

        if (physicalRuntimePath.StartsWith("cdrom0:\\MA", StringComparison.OrdinalIgnoreCase)) {
            File.AppendAllText(
                DebugLogPath,
                $"source={reference.RelativePath}{Environment.NewLine}resolved={physicalRuntimePath}{Environment.NewLine}{Environment.NewLine}");
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
        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialPhysicalToCookedPhysicalPaths,
        out string physicalRuntimePath) {
        if (IsPhysicalRuntimePath(assetPath)) {
            physicalRuntimePath = NormalizePhysicalRuntimePath(assetPath);
            if (rawMaterialPhysicalToCookedPhysicalPaths != null
                && rawMaterialPhysicalToCookedPhysicalPaths.TryGetValue(physicalRuntimePath, out string cookedPhysicalRuntimePath)
                && !string.IsNullOrWhiteSpace(cookedPhysicalRuntimePath)) {
                physicalRuntimePath = cookedPhysicalRuntimePath;
            }
            return true;
        }

        string normalizedLogicalPath = assetPath.Replace('\\', '/');
        if (rawMaterialLogicalToCookedLogicalPaths != null
            && rawMaterialLogicalToCookedLogicalPaths.TryGetValue(normalizedLogicalPath, out string cookedMaterialLogicalPath)
            && !string.IsNullOrWhiteSpace(cookedMaterialLogicalPath)
            && logicalToPhysicalPaths.TryGetValue(cookedMaterialLogicalPath, out string cookedMaterialPhysicalDiscPath)
            && !string.IsNullOrWhiteSpace(cookedMaterialPhysicalDiscPath)) {
            physicalRuntimePath = BuildRuntimePhysicalPath(cookedMaterialPhysicalDiscPath);
            return true;
        }

        if (TryBuildCookedMaterialLogicalPath(normalizedLogicalPath, out string fallbackCookedMaterialLogicalPath)
            && logicalToPhysicalPaths.TryGetValue(fallbackCookedMaterialLogicalPath, out string fallbackCookedMaterialPhysicalDiscPath)
            && !string.IsNullOrWhiteSpace(fallbackCookedMaterialPhysicalDiscPath)) {
            physicalRuntimePath = BuildRuntimePhysicalPath(fallbackCookedMaterialPhysicalDiscPath);
            return true;
        }

        if (!logicalToPhysicalPaths.TryGetValue(normalizedLogicalPath, out string physicalDiscPath) || string.IsNullOrWhiteSpace(physicalDiscPath)) {
            physicalRuntimePath = string.Empty;
            return false;
        }

        physicalRuntimePath = BuildRuntimePhysicalPath(physicalDiscPath);
        return true;
    }

    /// <summary>
    /// Builds the cooked logical runtime path for one authored material asset path when the scene still points at the uncooked source file.
    /// </summary>
    /// <param name="logicalPath">Logical authored runtime path to inspect.</param>
    /// <param name="cookedLogicalPath">Receives the cooked logical path when the source path represents an authored material asset.</param>
    /// <returns>True when the supplied path maps to a cooked material artifact; otherwise false.</returns>
    static bool TryBuildCookedMaterialLogicalPath(string logicalPath, out string cookedLogicalPath) {
        cookedLogicalPath = string.Empty;
        if (string.IsNullOrWhiteSpace(logicalPath)) {
            return false;
        }

        if (!logicalPath.EndsWith(SourceMaterialExtension, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        string trimmedPath = logicalPath.TrimStart('/');
        if (trimmedPath.StartsWith("cooked/", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        cookedLogicalPath = "cooked/" + Path.ChangeExtension(trimmedPath, CookedMaterialExtension).Replace('\\', '/');
        return true;
    }

    /// <summary>
    /// Builds the stable material asset id implied by one authored material document path.
    /// </summary>
    /// <param name="logicalPath">Logical authored material document path.</param>
    /// <param name="assetId">Receives the inferred stable material asset id when the path targets an authored material document.</param>
    /// <returns>True when a stable material asset id could be inferred; otherwise false.</returns>
    static bool TryBuildMaterialAssetIdFromLogicalPath(string logicalPath, out string assetId) {
        assetId = string.Empty;
        if (string.IsNullOrWhiteSpace(logicalPath)
            || !logicalPath.EndsWith(CookedMaterialExtension, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        string trimmedPath = logicalPath.TrimStart('/');
        if (!trimmedPath.StartsWith("materials/", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        assetId = Path.ChangeExtension(trimmedPath, null)?.Replace('\\', '.').Replace('/', '.') ?? string.Empty;
        return !string.IsNullOrWhiteSpace(assetId);
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

