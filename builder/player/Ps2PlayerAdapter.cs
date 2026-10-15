using helengine.baseplatform.Player;

namespace helengine.ps2.builder;

/// <summary>
/// Lets the desktop C# player run PS2-staged cooked content: registers PS2-aware texture and model processors,
/// translates PS2 cooked materials into platform-neutral descriptions and exposes the generic-model aliases the
/// PS2 disc layout also answers to. The player names this type through the PS2 platform's Play capability.
/// </summary>
public sealed class Ps2PlayerAdapter : IPlatformPlayerAdapter {
    /// <summary>
    /// Disc-path suffix ISO9660 version marker appended to every staged physical path.
    /// </summary>
    const string DiscVersionSuffix = ";1";

    /// <summary>
    /// Absolute PS2 staging root the adapter was initialized with; null until <see cref="Initialize"/> runs.
    /// </summary>
    string ContentRootPath;

    /// <summary>
    /// Logical staged paths mapped to their rooted physical disc paths (for example <c>\COOKED\X.HAS;1</c>).
    /// </summary>
    Dictionary<string, string> LogicalToPhysical;

    /// <summary>
    /// Disc-relative physical paths (no leading separator, no version suffix, backslash separators) mapped back to the
    /// logical staged file name, compared case-insensitively. Generic <c>.hasset</c> model aliases are excluded so each
    /// physical path resolves to the file actually staged.
    /// </summary>
    Dictionary<string, string> PhysicalToLogical;

    /// <summary>
    /// Initializes an adapter that is bound to a staging root later through <see cref="Initialize"/>.
    /// </summary>
    public Ps2PlayerAdapter() {
    }

    /// <summary>
    /// Binds the adapter to the PS2 staging root and builds the logical/physical path maps used for material
    /// texture resolution and content aliases.
    /// </summary>
    /// <param name="contentRootPath">Absolute PS2 staging root (<c>ps2-staging</c> under the builder working root).</param>
    public void Initialize(string contentRootPath) {
        if (string.IsNullOrWhiteSpace(contentRootPath)) {
            throw new ArgumentException("PS2 content root path must be provided.", nameof(contentRootPath));
        }

        Dictionary<string, string> logicalToPhysical = Ps2DiscLayoutWriter.BuildLogicalToPhysicalPathMap(contentRootPath);
        Dictionary<string, string> physicalToLogical = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> entry in logicalToPhysical) {
            if (IsGenericModelAlias(logicalToPhysical, entry.Key, entry.Value)) {
                continue;
            }

            string physicalKey = NormalizePhysicalPath(StripDiscAdornments(entry.Value));
            if (physicalToLogical.TryGetValue(physicalKey, out string existingLogical)
                && !string.Equals(existingLogical, entry.Key, StringComparison.Ordinal)) {
                throw new InvalidOperationException(
                    $"PS2 staged files '{existingLogical}' and '{entry.Key}' resolve to the same disc path '{physicalKey}'.");
            }

            physicalToLogical[physicalKey] = entry.Key;
        }

        ContentRootPath = contentRootPath;
        LogicalToPhysical = logicalToPhysical;
        PhysicalToLogical = physicalToLogical;
    }

    /// <summary>
    /// Renders Play at the selected PS2 video mode's framebuffer size, as the GS does before the PCRTC magnifies it, and
    /// reports the mode's pixel aspect so the player window shows that framebuffer at the display aspect gsKit's
    /// magnification gives it on a TV (4:3, or 3:2 and 22:15 for the 320- and 256-wide modes).
    /// </summary>
    /// <param name="playSettings">Resolved Play settings; must carry the PS2 video mode.</param>
    /// <returns>The mode's framebuffer size and the pixel aspect that shows it at its display aspect.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no video mode was resolved.</exception>
    public PlayerRenderSize ApplyPlaySettings(PlayerPlaySettings playSettings) {
        if (playSettings == null) {
            throw new ArgumentNullException(nameof(playSettings));
        } else if (!playSettings.HasVideoMode) {
            throw new InvalidOperationException("PS2 Play needs the resolved PS2 video mode; the editor sent none.");
        }

        return new PlayerRenderSize(playSettings.VideoMode.Width, playSettings.VideoMode.Height, playSettings.VideoMode.PixelAspect);
    }

    /// <summary>
    /// Registers the PS2-aware texture and model processors under the runtime processor ids the player loads by.
    /// </summary>
    /// <param name="contentManager">Content manager owned by the player.</param>
    public void RegisterContentProcessors(ContentManager contentManager) {
        if (contentManager == null) {
            throw new ArgumentNullException(nameof(contentManager));
        }

        contentManager.RegisterProcessor<TextureAsset>(RuntimeContentProcessorIds.TextureAsset, new Ps2PlayerTextureContentProcessor());
        contentManager.RegisterProcessor<ModelAsset>(RuntimeContentProcessorIds.ModelAsset, new Ps2PlayerModelContentProcessor());
    }

    /// <summary>
    /// Decodes one PS2 cooked material, translating its disc texture path back to the logical staged texture path.
    /// </summary>
    /// <param name="cookedMaterial">Stream positioned at the start of the PS2 cooked material payload.</param>
    /// <param name="materialRelativePath">Content-root-relative material path, used in diagnostics.</param>
    /// <returns>The platform-neutral material description.</returns>
    /// <exception cref="NotSupportedException">Thrown when the payload is not a PS2-owned material.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the referenced texture is not in the staging tree.</exception>
    public PlayerMaterialDescription DecodeMaterial(Stream cookedMaterial, string materialRelativePath) {
        if (cookedMaterial == null) {
            throw new ArgumentNullException(nameof(cookedMaterial));
        } else if (string.IsNullOrWhiteSpace(materialRelativePath)) {
            throw new ArgumentException("Material relative path must be provided.", nameof(materialRelativePath));
        }

        EnsureInitialized();
        if (cookedMaterial.CanSeek) {
            return DecodeSeekableMaterial(cookedMaterial, materialRelativePath);
        }

        using MemoryStream seekableMaterial = Ps2PlayerHeaderProbe.CopyToSeekableStream(cookedMaterial);
        return DecodeSeekableMaterial(seekableMaterial, materialRelativePath);
    }

    /// <summary>
    /// Builds aliases from every staged PS2 model's generic <c>.hasset</c> path, which scenes reference, to the staged
    /// <c>.phm</c> payload.
    /// </summary>
    /// <returns>Generic model path mapped to the staged PS2 model path, compared ordinally.</returns>
    public IReadOnlyDictionary<string, string> BuildContentAliases() {
        EnsureInitialized();
        Dictionary<string, string> aliases = new(StringComparer.Ordinal);
        foreach (string logicalPath in LogicalToPhysical.Keys) {
            if (!logicalPath.EndsWith(".phm", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            aliases[Path.ChangeExtension(logicalPath, ".hasset")] = logicalPath;
        }

        return aliases;
    }

    /// <summary>
    /// Decodes one seekable PS2 material payload into the platform-neutral description.
    /// </summary>
    /// <param name="cookedMaterial">Seekable stream positioned at the start of the payload.</param>
    /// <param name="materialRelativePath">Content-root-relative material path, used in diagnostics.</param>
    /// <returns>The platform-neutral material description.</returns>
    PlayerMaterialDescription DecodeSeekableMaterial(Stream cookedMaterial, string materialRelativePath) {
        if (!Ps2PlayerHeaderProbe.IsPs2Asset(cookedMaterial)) {
            throw new NotSupportedException($"Material '{materialRelativePath}' is not a PS2-owned cooked material; PS2 staging only contains PS2 materials.");
        }

        Asset asset = Ps2AssetSerializer.Deserialize(cookedMaterial);
        if (asset is not Ps2MaterialAsset material) {
            throw new InvalidOperationException($"Material '{materialRelativePath}' deserialized as '{asset.GetType().Name}' instead of a PS2 material.");
        }

        return new PlayerMaterialDescription(
            material.BaseColorR,
            material.BaseColorG,
            material.BaseColorB,
            material.BaseColorA,
            ResolveTextureLogicalPath(material.TextureRelativePath, materialRelativePath),
            MapRenderClass(material.RenderClass, materialRelativePath),
            material.DoubleSided);
    }

    /// <summary>
    /// Resolves a material's disc-relative texture path back to the logical staged texture path.
    /// </summary>
    /// <param name="textureDiscPath">Disc-relative texture path stored in the cooked material, or empty when untextured.</param>
    /// <param name="materialRelativePath">Material path, used in diagnostics.</param>
    /// <returns>Logical staged texture path, or <see cref="string.Empty"/> when the material is untextured.</returns>
    string ResolveTextureLogicalPath(string textureDiscPath, string materialRelativePath) {
        if (string.IsNullOrEmpty(textureDiscPath)) {
            return string.Empty;
        }

        if (!PhysicalToLogical.TryGetValue(NormalizePhysicalPath(textureDiscPath), out string logicalPath)) {
            throw new InvalidOperationException(
                $"Material '{materialRelativePath}' references disc texture '{textureDiscPath}' that is not in the PS2 staging tree.");
        }

        return logicalPath;
    }

    /// <summary>
    /// Maps the PS2 render bucket to the player's render class by member name.
    /// </summary>
    /// <param name="renderClass">PS2 render bucket stored in the cooked material.</param>
    /// <param name="materialRelativePath">Material path, used in diagnostics.</param>
    /// <returns>The matching player render class.</returns>
    static PlayerMaterialRenderClass MapRenderClass(Ps2RenderClass renderClass, string materialRelativePath) {
        switch (renderClass) {
            case Ps2RenderClass.Opaque:
                return PlayerMaterialRenderClass.Opaque;
            case Ps2RenderClass.AlphaTest:
                return PlayerMaterialRenderClass.AlphaTest;
            case Ps2RenderClass.Transparent:
                return PlayerMaterialRenderClass.Transparent;
            default:
                throw new InvalidOperationException($"Material '{materialRelativePath}' uses unknown PS2 render class '{renderClass}'.");
        }
    }

    /// <summary>
    /// Reports whether one forward-map entry is the generic <c>.hasset</c> alias the disc layout adds for a staged
    /// <c>.phm</c> model rather than a staged file of its own.
    /// </summary>
    /// <param name="logicalToPhysical">Forward logical-to-physical map.</param>
    /// <param name="logicalPath">Logical path of the entry.</param>
    /// <param name="physicalPath">Physical path of the entry.</param>
    /// <returns>True when the entry aliases a staged <c>.phm</c> model.</returns>
    static bool IsGenericModelAlias(Dictionary<string, string> logicalToPhysical, string logicalPath, string physicalPath) {
        if (!logicalPath.EndsWith(".hasset", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        string modelLogicalPath = Path.ChangeExtension(logicalPath, ".phm");
        return logicalToPhysical.TryGetValue(modelLogicalPath, out string modelPhysicalPath)
            && string.Equals(modelPhysicalPath, physicalPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Removes the leading root separator and the trailing ISO9660 version suffix from one rooted disc path.
    /// </summary>
    /// <param name="rootedDiscPath">Rooted physical disc path such as <c>\COOKED\X.HAS;1</c>.</param>
    /// <returns>Disc-relative path such as <c>COOKED\X.HAS</c>.</returns>
    static string StripDiscAdornments(string rootedDiscPath) {
        string discPath = rootedDiscPath;
        if (discPath.StartsWith('\\')) {
            discPath = discPath.Substring(1);
        }
        if (discPath.EndsWith(DiscVersionSuffix, StringComparison.Ordinal)) {
            discPath = discPath.Substring(0, discPath.Length - DiscVersionSuffix.Length);
        }

        return discPath;
    }

    /// <summary>
    /// Normalizes one disc-relative path to backslash separators so cooked material paths and staged paths compare equal.
    /// </summary>
    /// <param name="discPath">Disc-relative path with either separator.</param>
    /// <returns>Disc-relative path using backslash separators.</returns>
    static string NormalizePhysicalPath(string discPath) {
        return discPath.Replace('/', '\\');
    }

    /// <summary>
    /// Fails when a member that needs the staging maps runs before <see cref="Initialize"/>.
    /// </summary>
    void EnsureInitialized() {
        if (ContentRootPath == null) {
            throw new InvalidOperationException("Ps2PlayerAdapter.Initialize must be called before decoding content.");
        }
    }
}
