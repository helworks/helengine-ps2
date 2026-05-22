using helengine.baseplatform.Manifest;
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
    /// Stable component type id used by packaged sprite payloads.
    /// </summary>
    const string SpriteComponentTypeId = "helengine.SpriteComponent";

    /// <summary>
    /// Stable component type id used by packaged FPS payloads.
    /// </summary>
    const string FPSComponentTypeId = "helengine.FPSComponent";

    /// <summary>
    /// Stable tagged field name used by sprite payloads for their texture asset reference.
    /// </summary>
    const string SpriteTextureReferenceFieldName = "TextureReference";

    /// <summary>
    /// Current tagged component payload container version used by editor-authored scene component payloads.
    /// </summary>
    const byte EditorTaggedSceneComponentPayloadVersion = 1;

    /// <summary>
    /// Rewrites staged packaged scene and PS2 material assets to use physical disc paths.
    /// </summary>
    /// <param name="stagingRootPath">Staged package root that contains cooked artifacts.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    public void Rewrite(
        string stagingRootPath,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        IReadOnlyList<PlatformCookWorkItem> platformCookWorkItems) {
        if (string.IsNullOrWhiteSpace(stagingRootPath)) {
            throw new ArgumentException("Staging root path must be provided.", nameof(stagingRootPath));
        }
        if (logicalToPhysicalPaths == null) {
            throw new ArgumentNullException(nameof(logicalToPhysicalPaths));
        }
        if (platformCookWorkItems == null) {
            throw new ArgumentNullException(nameof(platformCookWorkItems));
        }
        if (!Directory.Exists(stagingRootPath)) {
            throw new DirectoryNotFoundException($"Staging root '{stagingRootPath}' was not found.");
        }

        File.WriteAllText(DebugLogPath, string.Empty);

        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths = BuildRawMaterialLogicalToCookedLogicalPathMap(stagingRootPath, platformCookWorkItems);
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
    /// Builds a logical source-to-cooked lookup so runtime scene references can be redirected onto PS2-cooked payloads.
    /// </summary>
    /// <param name="stagingRootPath">Staged package root that contains cooked and raw packaged assets.</param>
    /// <param name="platformCookWorkItems">Builder-owned cook work items emitted by the editor for this build.</param>
    /// <returns>Logical source asset paths mapped to logical cooked runtime paths.</returns>
    static IReadOnlyDictionary<string, string> BuildRawMaterialLogicalToCookedLogicalPathMap(
        string stagingRootPath,
        IReadOnlyList<PlatformCookWorkItem> platformCookWorkItems) {
        if (string.IsNullOrWhiteSpace(stagingRootPath)) {
            throw new ArgumentException("Staging root path must be provided.", nameof(stagingRootPath));
        } else if (platformCookWorkItems == null) {
            throw new ArgumentNullException(nameof(platformCookWorkItems));
        }

        Dictionary<string, string> rawToCookedLogicalPaths = new(StringComparer.OrdinalIgnoreCase);
        for (int workItemIndex = 0; workItemIndex < platformCookWorkItems.Count; workItemIndex++) {
            PlatformCookWorkItem workItem = platformCookWorkItems[workItemIndex];
            if (workItem == null
                || string.IsNullOrWhiteSpace(workItem.SourceAssetPath)
                || string.IsNullOrWhiteSpace(workItem.OutputRelativePath)
                || !TryResolveSourceLogicalPath(workItem.SourceAssetPath, out string sourceLogicalPath)) {
                continue;
            }

            rawToCookedLogicalPaths[sourceLogicalPath] = NormalizeLogicalPath(workItem.OutputRelativePath);
        }

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
            Asset asset = DeserializeAsset(stream);
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
    /// Deserializes one staged asset payload using either the generic helengine serializer or the PS2-owned cooked serializer.
    /// </summary>
    /// <param name="stream">Source stream containing a staged asset payload.</param>
    /// <returns>Deserialized asset instance.</returns>
    static Asset DeserializeAsset(Stream stream) {
        if (stream == null) {
            throw new ArgumentNullException(nameof(stream));
        }

        EngineBinaryHeader header = EngineBinaryHeaderSerializer.Read(stream);
        if (header.FormatId == EditorAssetBinarySerializer.FormatId) {
            return EditorAssetBinarySerializer.Deserialize(stream, header);
        }
        if (header.FormatId == Ps2AssetBinarySerializer.FormatId) {
            return Ps2AssetBinarySerializer.Deserialize(stream, header);
        }

        throw new InvalidOperationException($"Unsupported staged asset format id '{header.FormatId}'.");
    }

    /// <summary>
    /// Serializes one rewritten staged asset using the serializer that owns its runtime format.
    /// </summary>
    /// <param name="asset">Asset instance to serialize.</param>
    /// <returns>Serialized asset payload bytes.</returns>
    static byte[] SerializeAsset(Asset asset) {
        if (asset == null) {
            throw new ArgumentNullException(nameof(asset));
        }

        if (asset is Ps2MaterialAsset || asset is Ps2TextureAsset || asset is Ps2ModelAsset) {
            return Ps2AssetSerializer.SerializeToBytes(asset);
        }

        return helengine.files.AssetSerializer.SerializeToBytes(asset);
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
            asset = DeserializeAsset(readStream);
            if (asset is SceneAsset sceneAsset) {
                changed = RewriteSceneAsset(sceneAsset, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths);
            } else if (asset is Ps2MaterialAsset ps2MaterialAsset) {
                changed = RewritePs2MaterialAsset(ps2MaterialAsset, logicalToPhysicalPaths, rawMaterialPhysicalToCookedPhysicalPaths);
            }
        }

        if (!changed) {
            return;
        }

        File.WriteAllBytes(filePath, SerializeAsset(asset));
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
        } else if (string.Equals(record.ComponentTypeId, SpriteComponentTypeId, StringComparison.OrdinalIgnoreCase)) {
            return RewriteSpriteComponentRecord(record, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths, out changed);
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
    /// Rewrites packaged sprite texture references to physical disc paths.
    /// </summary>
    /// <param name="record">Packaged sprite component record.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    /// <param name="changed">Receives whether the payload changed.</param>
    /// <returns>Rewritten sprite component record.</returns>
    SceneComponentAssetRecord RewriteSpriteComponentRecord(
        SceneComponentAssetRecord record,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialPhysicalToCookedPhysicalPaths,
        out bool changed) {
        if (TryRewriteTaggedSpriteComponentRecord(record, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths, out SceneComponentAssetRecord rewrittenTaggedRecord, out changed)) {
            return rewrittenTaggedRecord;
        }

        return RewriteCookedRuntimeSpriteComponentRecord(record, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths, out changed);
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
    /// Tries to rewrite one tagged editor sprite payload while preserving every unrelated field byte-for-byte.
    /// </summary>
    /// <param name="record">Packaged sprite component record.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    /// <param name="rewrittenRecord">Rewritten sprite component record when the payload used the tagged editor format.</param>
    /// <param name="changed">Receives whether the payload changed.</param>
    /// <returns>True when the payload used the tagged editor format; otherwise false.</returns>
    bool TryRewriteTaggedSpriteComponentRecord(
        SceneComponentAssetRecord record,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialPhysicalToCookedPhysicalPaths,
        out SceneComponentAssetRecord rewrittenRecord,
        out bool changed) {
        if (!TryRewriteTaggedOptionalReferenceFieldPayload(
            record.Payload ?? Array.Empty<byte>(),
            SpriteTextureReferenceFieldName,
            logicalToPhysicalPaths,
            rawMaterialLogicalToCookedLogicalPaths,
            rawMaterialPhysicalToCookedPhysicalPaths,
            out byte[] rewrittenPayload,
            out changed)) {
            rewrittenRecord = record;
            return false;
        }

        if (!changed) {
            rewrittenRecord = record;
            return true;
        }

        rewrittenRecord = new SceneComponentAssetRecord {
            ComponentTypeId = record.ComponentTypeId,
            ComponentIndex = record.ComponentIndex,
            Payload = rewrittenPayload
        };
        return true;
    }

    /// <summary>
    /// Rewrites one cooked-runtime sprite payload when the scene packager already emitted the compact runtime layout.
    /// </summary>
    /// <param name="record">Packaged sprite component record.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    /// <param name="changed">Receives whether the payload changed.</param>
    /// <returns>Rewritten sprite component record.</returns>
    SceneComponentAssetRecord RewriteCookedRuntimeSpriteComponentRecord(
        SceneComponentAssetRecord record,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialPhysicalToCookedPhysicalPaths,
        out bool changed) {
        using MemoryStream readStream = new(record.Payload ?? Array.Empty<byte>(), false);
        using EngineBinaryReader reader = EngineBinaryReader.Create(readStream, EngineBinaryEndianness.LittleEndian);
        byte version = reader.ReadByte();
        if (version != 1) {
            throw new InvalidOperationException($"Unsupported sprite component payload version '{version}'.");
        }

        SceneAssetReference textureReference = ReadOptionalReference(reader);
        if (textureReference == null) {
            throw new InvalidOperationException("SpriteComponent requires a texture asset reference before serialization.");
        }

        byte[] remainingBytes = ReadRemainingBytes(readStream);
        changed = RewriteReference(textureReference, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths);
        if (!changed) {
            return record;
        }

        using MemoryStream writeStream = new();
        using EngineBinaryWriter writer = EngineBinaryWriter.Create(writeStream, EngineBinaryEndianness.LittleEndian);
        writer.WriteByte(version);
        WriteOptionalReference(writer, textureReference);
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

        string normalizedLogicalPath = NormalizeLogicalPath(assetPath);
        if (rawMaterialLogicalToCookedLogicalPaths != null
            && rawMaterialLogicalToCookedLogicalPaths.TryGetValue(normalizedLogicalPath, out string cookedMaterialLogicalPath)
            && !string.IsNullOrWhiteSpace(cookedMaterialLogicalPath)
            && logicalToPhysicalPaths.TryGetValue(cookedMaterialLogicalPath, out string cookedMaterialPhysicalDiscPath)
            && !string.IsNullOrWhiteSpace(cookedMaterialPhysicalDiscPath)) {
            physicalRuntimePath = BuildRuntimePhysicalPath(cookedMaterialPhysicalDiscPath);
            return true;
        }

        string fallbackCookedLogicalPath = "cooked/" + normalizedLogicalPath;
        if (logicalToPhysicalPaths.TryGetValue(fallbackCookedLogicalPath, out string fallbackCookedPhysicalDiscPath)
            && !string.IsNullOrWhiteSpace(fallbackCookedPhysicalDiscPath)) {
            physicalRuntimePath = BuildRuntimePhysicalPath(fallbackCookedPhysicalDiscPath);
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
    /// Tries to convert one builder-owned source asset path into the logical packaged asset path used by runtime scene references.
    /// </summary>
    /// <param name="sourceAssetPath">Absolute or relative source asset path from one platform cook work item.</param>
    /// <param name="logicalPath">Receives the logical packaged asset path when the source path belongs to the project assets tree.</param>
    /// <returns>True when the source path resolved into a packaged logical asset path; otherwise false.</returns>
    static bool TryResolveSourceLogicalPath(string sourceAssetPath, out string logicalPath) {
        logicalPath = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceAssetPath)) {
            return false;
        }

        string normalizedSourcePath = sourceAssetPath.Replace('\\', '/');
        const string AssetsMarker = "/assets/";
        int assetsIndex = normalizedSourcePath.LastIndexOf(AssetsMarker, StringComparison.OrdinalIgnoreCase);
        if (assetsIndex >= 0) {
            logicalPath = NormalizeLogicalPath(normalizedSourcePath[(assetsIndex + AssetsMarker.Length)..]);
            return !string.IsNullOrWhiteSpace(logicalPath);
        }

        if (normalizedSourcePath.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) {
            logicalPath = NormalizeLogicalPath(normalizedSourcePath["assets/".Length..]);
            return !string.IsNullOrWhiteSpace(logicalPath);
        }

        if (Path.IsPathRooted(sourceAssetPath)) {
            return false;
        }

        logicalPath = NormalizeLogicalPath(normalizedSourcePath);
        return !string.IsNullOrWhiteSpace(logicalPath);
    }

    /// <summary>
    /// Normalizes one logical packaged asset path into slash-separated relative form.
    /// </summary>
    /// <param name="logicalPath">Logical packaged asset path to normalize.</param>
    /// <returns>Normalized slash-separated relative path.</returns>
    static string NormalizeLogicalPath(string logicalPath) {
        if (string.IsNullOrWhiteSpace(logicalPath)) {
            return string.Empty;
        }

        return logicalPath.Replace('\\', '/').TrimStart('/');
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

        string trimmedPath = NormalizeLogicalPath(logicalPath);
        bool isSourceMaterialDocument = trimmedPath.StartsWith("materials/", StringComparison.OrdinalIgnoreCase)
            && trimmedPath.EndsWith(SourceMaterialExtension, StringComparison.OrdinalIgnoreCase);
        bool isPackagedMaterialAsset = trimmedPath.StartsWith("materials/", StringComparison.OrdinalIgnoreCase)
            && trimmedPath.EndsWith(CookedMaterialExtension, StringComparison.OrdinalIgnoreCase);
        if (!isSourceMaterialDocument && !isPackagedMaterialAsset) {
            return false;
        }

        if (trimmedPath.StartsWith("cooked/", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (isSourceMaterialDocument) {
            cookedLogicalPath = "cooked/" + Path.ChangeExtension(trimmedPath, CookedMaterialExtension).Replace('\\', '/');
        } else {
            cookedLogicalPath = "cooked/" + trimmedPath;
        }

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

    /// <summary>
    /// Tries to rewrite one named optional-reference field inside the tolerant tagged editor component payload format.
    /// </summary>
    /// <param name="payload">Serialized tagged component payload.</param>
    /// <param name="fieldName">Stable optional-reference field name to rewrite.</param>
    /// <param name="logicalToPhysicalPaths">Logical cooked-path mappings resolved for the staged PS2 disc layout.</param>
    /// <param name="rewrittenPayload">Receives the rewritten payload when the tagged format was recognized.</param>
    /// <param name="changed">Receives whether the target field changed.</param>
    /// <returns>True when the payload used the tagged editor format; otherwise false.</returns>
    bool TryRewriteTaggedOptionalReferenceFieldPayload(
        byte[] payload,
        string fieldName,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialLogicalToCookedLogicalPaths,
        IReadOnlyDictionary<string, string> rawMaterialPhysicalToCookedPhysicalPaths,
        out byte[] rewrittenPayload,
        out bool changed) {
        rewrittenPayload = Array.Empty<byte>();
        changed = false;
        if (payload == null) {
            throw new ArgumentNullException(nameof(payload));
        }
        if (string.IsNullOrWhiteSpace(fieldName)) {
            throw new ArgumentException("Field name must be provided.", nameof(fieldName));
        }

        try {
            using MemoryStream readStream = new(payload, false);
            using EngineBinaryReader reader = EngineBinaryReader.Create(readStream, EngineBinaryEndianness.LittleEndian);
            byte version = reader.ReadByte();
            if (version != EditorTaggedSceneComponentPayloadVersion) {
                return false;
            }

            int fieldCount = reader.ReadInt32();
            if (fieldCount < 0) {
                throw new InvalidOperationException("Editor tagged scene component payload field counts cannot be negative.");
            }

            List<string> fieldNames = new(fieldCount);
            List<byte[]> fieldPayloads = new(fieldCount);
            bool foundField = false;
            for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++) {
                string currentFieldName = reader.ReadString();
                if (string.IsNullOrWhiteSpace(currentFieldName)) {
                    throw new InvalidOperationException("Editor tagged scene component payload fields must define a name.");
                }

                byte[] fieldPayload = reader.ReadByteArray() ?? Array.Empty<byte>();
                if (string.Equals(currentFieldName, fieldName, StringComparison.Ordinal)) {
                    foundField = true;
                    using MemoryStream fieldReadStream = new(fieldPayload, false);
                    using EngineBinaryReader fieldReader = EngineBinaryReader.Create(fieldReadStream, EngineBinaryEndianness.LittleEndian);
                    SceneAssetReference fieldReference = ReadOptionalReference(fieldReader);
                    if (fieldReference == null) {
                        throw new InvalidOperationException($"Tagged scene component field '{fieldName}' requires a texture asset reference.");
                    }

                    if (RewriteReference(fieldReference, logicalToPhysicalPaths, rawMaterialLogicalToCookedLogicalPaths, rawMaterialPhysicalToCookedPhysicalPaths)) {
                        changed = true;
                        fieldPayload = SerializeOptionalReferenceFieldPayload(fieldReference);
                    }
                }

                fieldNames.Add(currentFieldName);
                fieldPayloads.Add(fieldPayload);
            }

            if (!foundField) {
                throw new InvalidOperationException($"Tagged scene component payload did not contain required field '{fieldName}'.");
            }

            rewrittenPayload = changed
                ? BuildTaggedComponentPayload(fieldNames, fieldPayloads)
                : payload;
            return true;
        } catch (Exception ex) when (ex is InvalidOperationException || ex is EndOfStreamException) {
            return false;
        }
    }

    /// <summary>
    /// Serializes one optional scene reference into the raw payload bytes used by a tagged component field.
    /// </summary>
    /// <param name="reference">Scene reference to serialize.</param>
    /// <returns>Raw field payload bytes.</returns>
    static byte[] SerializeOptionalReferenceFieldPayload(SceneAssetReference reference) {
        using MemoryStream stream = new();
        using EngineBinaryWriter writer = EngineBinaryWriter.Create(stream, EngineBinaryEndianness.LittleEndian);
        WriteOptionalReference(writer, reference);
        return stream.ToArray();
    }

    /// <summary>
    /// Rebuilds one tagged component payload from pre-serialized field payload bytes.
    /// </summary>
    /// <param name="fieldNames">Stable field names in serialized order.</param>
    /// <param name="fieldPayloads">Raw field payload bytes aligned with <paramref name="fieldNames"/>.</param>
    /// <returns>Tagged component payload bytes.</returns>
    static byte[] BuildTaggedComponentPayload(IReadOnlyList<string> fieldNames, IReadOnlyList<byte[]> fieldPayloads) {
        if (fieldNames == null) {
            throw new ArgumentNullException(nameof(fieldNames));
        }
        if (fieldPayloads == null) {
            throw new ArgumentNullException(nameof(fieldPayloads));
        }
        if (fieldNames.Count != fieldPayloads.Count) {
            throw new InvalidOperationException("Tagged component field names and payload counts must match.");
        }

        using MemoryStream stream = new();
        using EngineBinaryWriter writer = EngineBinaryWriter.Create(stream, EngineBinaryEndianness.LittleEndian);
        writer.WriteByte(EditorTaggedSceneComponentPayloadVersion);
        writer.WriteInt32(fieldNames.Count);
        for (int fieldIndex = 0; fieldIndex < fieldNames.Count; fieldIndex++) {
            writer.WriteString(fieldNames[fieldIndex]);
            writer.WriteByteArray(fieldPayloads[fieldIndex] ?? Array.Empty<byte>());
        }

        return stream.ToArray();
    }
}

