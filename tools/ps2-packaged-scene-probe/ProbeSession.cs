namespace helengine.ps2.packagedsceneprobe {
    /// <summary>
    /// Loads one packaged PS2 startup scene through the shared runtime scene loader and prints diagnostic trace state.
    /// </summary>
    public sealed class ProbeSession {
        /// <summary>
        /// Gets the export root passed on the command line.
        /// </summary>
        public string ExportRootPath { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the optional explicit scene path passed on the command line.
        /// </summary>
        public string ScenePath { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the probe execution mode selected on the command line.
        /// </summary>
        public string Mode { get; private set; } = "scene-load";

        /// <summary>
        /// Runs one packaged scene probe session.
        /// </summary>
        /// <param name="args">Command-line arguments for the probe session.</param>
        /// <returns>Zero when the scene loads successfully; otherwise one.</returns>
        public int Run(string[] args) {
            ParseArguments(args);
            string scenePath = ResolveScenePath();
            Console.WriteLine("[ps2-scene-probe] export-root=" + ExportRootPath);
            Console.WriteLine("[ps2-scene-probe] scene-path=" + scenePath);

            SceneAsset sceneAsset = LoadSceneAsset(scenePath);
            PrintSceneSummary(sceneAsset);

            CoreInitializationOptions options = new CoreInitializationOptions {
                ContentRootPath = Path.Combine(ExportRootPath, "disc"),
                UpdateOrderLayers = 1,
                RenderOrderLayers3D = 1,
                UpdateListInitialCapacity = 4,
                RenderList2DInitialCapacity = 4,
                RenderList3DInitialCapacity = 4
            };

            Core core = new Core(options);
            ProbeRenderManager3D renderManager3D = new ProbeRenderManager3D();
            ProbeRenderManager2D renderManager2D = new ProbeRenderManager2D();
            ProbeInputBackend inputBackend = new ProbeInputBackend();
            PlatformInfo platformInfo = new PlatformInfo("ps2-packaged-scene-probe", "1.0.0");
            RuntimeSceneLoadService sceneLoadService = null;

            try {
                renderManager3D.AddWindow(IntPtr.Zero, 640, 448);
                core.Initialize(renderManager3D, renderManager2D, inputBackend, platformInfo, options);
                Console.WriteLine("[ps2-scene-probe] core initialized");
                ProbeDefaultFont(core);
                if (string.Equals(Mode, "font-only", StringComparison.OrdinalIgnoreCase)) {
                    Console.WriteLine("[ps2-scene-probe] font-only complete");
                    return 0;
                }

                sceneLoadService = new RuntimeSceneLoadService(core.SceneAssetReferenceResolver, core.SceneRuntimeComponentRegistry);
                IReadOnlyList<Entity> rootEntities = sceneLoadService.Load(sceneAsset);
                Console.WriteLine("[ps2-scene-probe] scene load complete root-entities=" + rootEntities.Count);
                return 0;
            } catch (Exception exception) {
                PrintFailureTrace(core, sceneLoadService, exception);
                return 1;
            } finally {
                core.Dispose();
            }
        }

        /// <summary>
        /// Attempts one direct packaged-font deserialize so packaged font failures can be isolated from scene-reference path issues.
        /// </summary>
        /// <param name="core">Initialized runtime core.</param>
        public void ProbeDefaultFont(Core core) {
            if (core == null) {
                throw new ArgumentNullException(nameof(core));
            }

            string fontPath = Path.Combine(ExportRootPath, "disc", "COOKED", "FONTS", "DEFAULT.HEF");
            Console.WriteLine("[ps2-scene-probe] direct-font-path=" + fontPath);
            using FileStream stream = File.OpenRead(fontPath);
            FontAsset fontAsset = FontAssetBinarySerializer.Deserialize(stream);
            Console.WriteLine("[ps2-scene-probe] direct font deserialize complete line-height=" + fontAsset.LineHeight + " chars=" + (fontAsset.Characters?.Count ?? 0));
        }

        /// <summary>
        /// Parses command-line arguments for the probe session.
        /// </summary>
        /// <param name="args">Command-line arguments to parse.</param>
        public void ParseArguments(string[] args) {
            if (args == null) {
                throw new ArgumentNullException(nameof(args));
            }

            for (int argumentIndex = 0; argumentIndex < args.Length; argumentIndex++) {
                string argument = args[argumentIndex];
                if (string.Equals(argument, "--export-root", StringComparison.OrdinalIgnoreCase)) {
                    argumentIndex++;
                    if (argumentIndex >= args.Length) {
                        throw new InvalidOperationException("One export root path value is required.");
                    }

                    ExportRootPath = args[argumentIndex];
                } else if (string.Equals(argument, "--scene-path", StringComparison.OrdinalIgnoreCase)) {
                    argumentIndex++;
                    if (argumentIndex >= args.Length) {
                        throw new InvalidOperationException("One scene path value is required.");
                    }

                    ScenePath = args[argumentIndex];
                } else if (string.Equals(argument, "--mode", StringComparison.OrdinalIgnoreCase)) {
                    argumentIndex++;
                    if (argumentIndex >= args.Length) {
                        throw new InvalidOperationException("One mode value is required.");
                    }

                    Mode = args[argumentIndex];
                } else {
                    throw new InvalidOperationException("Unsupported probe argument '" + argument + "'.");
                }
            }

            if (string.IsNullOrWhiteSpace(ExportRootPath)) {
                throw new InvalidOperationException("One export root path is required.");
            }
            if (!string.Equals(Mode, "scene-load", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Mode, "font-only", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException("Probe mode must be either scene-load or font-only.");
            }
        }

        /// <summary>
        /// Resolves the scene path that should be loaded for this probe session.
        /// </summary>
        /// <returns>Absolute file path to the packaged scene asset.</returns>
        public string ResolveScenePath() {
            if (!string.IsNullOrWhiteSpace(ScenePath)) {
                return Path.GetFullPath(ScenePath);
            }

            string scenesDirectoryPath = Path.Combine(ExportRootPath, "disc", "COOKED", "SCENES");
            string[] candidateScenePaths = Directory.GetFiles(scenesDirectoryPath, "*.HAS", SearchOption.TopDirectoryOnly);
            if (candidateScenePaths.Length == 0) {
                throw new InvalidOperationException("No packaged scene assets were found in the export output.");
            }
            if (candidateScenePaths.Length > 1) {
                throw new InvalidOperationException("More than one packaged scene asset was found. Pass --scene-path explicitly.");
            }

            return Path.GetFullPath(candidateScenePaths[0]);
        }

        /// <summary>
        /// Deserializes one packaged scene asset from disk.
        /// </summary>
        /// <param name="scenePath">Absolute path to the packaged scene asset.</param>
        /// <returns>Deserialized scene asset.</returns>
        public SceneAsset LoadSceneAsset(string scenePath) {
            if (string.IsNullOrWhiteSpace(scenePath)) {
                throw new ArgumentException("Scene path must be provided.", nameof(scenePath));
            }

            using FileStream stream = File.OpenRead(scenePath);
            Asset asset = AssetSerializer.Deserialize(stream);
            SceneAsset sceneAsset = asset as SceneAsset;
            if (sceneAsset == null) {
                throw new InvalidOperationException("The packaged scene asset did not deserialize as one SceneAsset.");
            }

            return sceneAsset;
        }

        /// <summary>
        /// Prints a compact summary of the packaged scene structure before runtime materialization.
        /// </summary>
        /// <param name="sceneAsset">Scene asset to summarize.</param>
        public void PrintSceneSummary(SceneAsset sceneAsset) {
            if (sceneAsset == null) {
                throw new ArgumentNullException(nameof(sceneAsset));
            }

            Console.WriteLine("[ps2-scene-probe] scene roots=" + (sceneAsset.RootEntities?.Length ?? 0));
            Console.WriteLine("[ps2-scene-probe] scene root refs=" + (sceneAsset.AssetReferences?.Length ?? 0));
            if (sceneAsset.RootEntities == null) {
                return;
            }

            for (int rootIndex = 0; rootIndex < sceneAsset.RootEntities.Length; rootIndex++) {
                PrintEntitySummary(sceneAsset.RootEntities[rootIndex], "root[" + rootIndex + "]");
            }
        }

        /// <summary>
        /// Prints a recursive summary for one serialized entity.
        /// </summary>
        /// <param name="entityAsset">Serialized entity to summarize.</param>
        /// <param name="label">Human-readable path label for the entity.</param>
        public void PrintEntitySummary(SceneEntityAsset entityAsset, string label) {
            if (entityAsset == null) {
                Console.WriteLine("[ps2-scene-probe] " + label + " <null>");
                return;
            }

            int componentCount = entityAsset.Components?.Length ?? 0;
            int childCount = entityAsset.Children?.Length ?? 0;
            Console.WriteLine("[ps2-scene-probe] " + label + " components=" + componentCount + " children=" + childCount);
            if (entityAsset.Components != null) {
                for (int componentIndex = 0; componentIndex < entityAsset.Components.Length; componentIndex++) {
                    SceneComponentAssetRecord componentRecord = entityAsset.Components[componentIndex];
                    string componentTypeId = componentRecord != null ? componentRecord.ComponentTypeId : "<null>";
                    int payloadLength = componentRecord?.Payload?.Length ?? 0;
                    Console.WriteLine("[ps2-scene-probe] " + label + ".component[" + componentIndex + "] type=" + componentTypeId + " payload=" + payloadLength);
                }
            }

            if (entityAsset.Children == null) {
                return;
            }

            for (int childIndex = 0; childIndex < entityAsset.Children.Length; childIndex++) {
                PrintEntitySummary(entityAsset.Children[childIndex], label + ".child[" + childIndex + "]");
            }
        }

        /// <summary>
        /// Prints the runtime scene-load trace when one packaged scene load fails.
        /// </summary>
        /// <param name="core">Initialized core that attempted to load the scene.</param>
        /// <param name="exception">Exception raised by the runtime scene loader.</param>
        public void PrintFailureTrace(Core core, RuntimeSceneLoadService sceneLoadService, Exception exception) {
            if (core == null) {
                throw new ArgumentNullException(nameof(core));
            }
            if (exception == null) {
                throw new ArgumentNullException(nameof(exception));
            }

            Console.WriteLine("[ps2-scene-probe] failure=" + exception.GetType().FullName);
            Console.WriteLine("[ps2-scene-probe] message=" + exception.Message);
            Console.WriteLine("[ps2-scene-probe] trace-stage=" + (sceneLoadService != null ? sceneLoadService.LastTraceStage : "scene-load-service-not-created"));
            Console.WriteLine("[ps2-scene-probe] trace-root-index=" + (sceneLoadService != null ? sceneLoadService.LastTraceRootEntityIndex : -1));
            Console.WriteLine("[ps2-scene-probe] trace-depth=" + (sceneLoadService != null ? sceneLoadService.LastTraceEntityDepth : -1));
            Console.WriteLine("[ps2-scene-probe] trace-component=" + (sceneLoadService != null ? sceneLoadService.LastTraceComponentTypeId : string.Empty));
            Console.WriteLine("[ps2-scene-probe] text-stage=" + (sceneLoadService != null ? sceneLoadService.LastTextLoadStage : string.Empty));
            Console.WriteLine("[ps2-scene-probe] text-font-relative-path=" + (sceneLoadService != null ? sceneLoadService.LastTextFontRelativePath : string.Empty));
            Console.WriteLine("[ps2-scene-probe] font-stage=" + (sceneLoadService != null ? sceneLoadService.LastFontDeserializeStage : string.Empty));
            Console.WriteLine(exception.ToString());
        }
    }
}
