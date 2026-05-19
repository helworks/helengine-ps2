using System.Diagnostics;

namespace helengine.ps2.builder;

/// <summary>
/// Builds the native PS2 ELF and packages the staged ISO through the Docker-based toolchain.
/// </summary>
public sealed class Ps2NativeBuildExecutor : IPs2NativeBuildExecutor {
    /// <summary>
    /// Docker image tag used for the PS2 toolchain container.
    /// </summary>
    const string DockerImageTag = "helengine-ps2";
    static readonly string DebugLogPath = Path.Combine(AppContext.BaseDirectory, "ps2-native-build-executor.log");

    /// <summary>
    /// Builds one PS2 ELF for the prepared workspace.
    /// </summary>
    /// <param name="workspace">Prepared PS2 build workspace.</param>
    /// <param name="cancellationToken">Cancellation token used to stop the native build cooperatively.</param>
    public void Build(Ps2BuildWorkspace workspace, CancellationToken cancellationToken) {
        if (workspace == null) {
            throw new ArgumentNullException(nameof(workspace));
        }

        ValidateRenderManager3DContractPairing(workspace.RepositoryRootPath, workspace.GeneratedCoreRootPath);
        NormalizeGeneratedCoreSources(workspace.GeneratedCoreRootPath);

        RunProcess(
            "docker",
            [
                "build",
                "-t",
                DockerImageTag,
                workspace.RepositoryRootPath
            ],
            workspace.RepositoryRootPath,
            cancellationToken);

        RunProcess(
            "docker",
            [
                "run",
                "--rm",
                "-v",
                $"{workspace.RepositoryRootPath}:/workspace",
                "-v",
                $"{workspace.GeneratedCoreRootPath}:/generated-core",
                "-w",
                "/workspace",
                "-e",
                "HELENGINE_CORE_CPP_ROOT=/generated-core",
                DockerImageTag,
                "make"
            ],
            workspace.RepositoryRootPath,
            cancellationToken);
    }

    /// <summary>
    /// Applies the minimal generated-core source normalization required for the current engine API before the PS2 Docker toolchain compiles the translated runtime.
    /// </summary>
    /// <param name="generatedCoreRootPath">Absolute generated-core root produced by the editor build graph.</param>
    static void NormalizeGeneratedCoreSources(string generatedCoreRootPath) {
        if (string.IsNullOrWhiteSpace(generatedCoreRootPath)) {
            throw new ArgumentException("Generated core root path must be provided.", nameof(generatedCoreRootPath));
        }

        if (!Directory.Exists(generatedCoreRootPath)) {
            return;
        }

        File.WriteAllText(DebugLogPath, $"generatedCoreRootPath={generatedCoreRootPath}{Environment.NewLine}");

        NormalizeGeneratedCoreFile(generatedCoreRootPath, "RuntimeContentManagerConfiguration.cpp");
        NormalizeGeneratedCoreFile(generatedCoreRootPath, "RuntimeSceneAssetReferenceResolver.cpp");
        NormalizeGeneratedCoreFile(generatedCoreRootPath, "RenderManager3D.hpp");
        NormalizeGeneratedCoreFile(generatedCoreRootPath, "RenderManager3D.cpp");
        NormalizeGeneratedCoreFile(generatedCoreRootPath, "ScrollComponent.cpp");
        NormalizeGeneratedCoreFile(generatedCoreRootPath, "FontAsset.cpp");
        NormalizeGeneratedCoreFile(generatedCoreRootPath, "AmbientLightComponent.cpp");
        NormalizeGeneratedCoreFile(generatedCoreRootPath, "DirectionalLightComponent.cpp");
        NormalizeGeneratedCoreFile(generatedCoreRootPath, "LightComponent.cpp");
        NormalizeGeneratedCoreFile(generatedCoreRootPath, "PointLightComponent.cpp");
        NormalizeGeneratedCoreFile(generatedCoreRootPath, "SpotLightComponent.cpp");
        NormalizeGeneratedCoreFile(generatedCoreRootPath, "FPSComponent.hpp");
        NormalizeGeneratedCoreFile(generatedCoreRootPath, "FPSComponent.cpp");
        NormalizeGeneratedCoreFile(generatedCoreRootPath, Path.Combine("system", "io", "file-stream.hpp"));
        NormalizeGeneratedCoreFile(generatedCoreRootPath, Path.Combine("system", "io", "file-stream.cpp"));
        NormalizeGeneratedCoreFile(generatedCoreRootPath, "Core.cpp");
        NormalizeGeneratedCoreFile(generatedCoreRootPath, "SceneManager.cpp");
        NormalizeGeneratedCoreFile(generatedCoreRootPath, Path.Combine("runtime", "runtime_graphics_renderer_manifest.cpp"));
    }

    /// <summary>
    /// Fails the PS2 native build when generated core and renderer sources disagree about the render-manager material contract.
    /// </summary>
    /// <param name="repositoryRootPath">Absolute PS2 repository root that provides the native renderer sources.</param>
    /// <param name="generatedCoreRootPath">Absolute generated-core root produced by the editor build graph.</param>
    static void ValidateRenderManager3DContractPairing(string repositoryRootPath, string generatedCoreRootPath) {
        if (string.IsNullOrWhiteSpace(repositoryRootPath)) {
            throw new ArgumentException("Repository root path must be provided.", nameof(repositoryRootPath));
        }
        if (string.IsNullOrWhiteSpace(generatedCoreRootPath)) {
            throw new ArgumentException("Generated core root path must be provided.", nameof(generatedCoreRootPath));
        }
        if (!Directory.Exists(generatedCoreRootPath)) {
            throw new InvalidOperationException("Generated core root path is missing, so the PS2 native contract cannot be validated.");
        }

        string generatedRenderManagerHeaderPath = Path.Combine(generatedCoreRootPath, "RenderManager3D.hpp");
        string nativeRenderManagerHeaderPath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.hpp");
        if (!File.Exists(generatedRenderManagerHeaderPath)) {
            throw new InvalidOperationException("Generated RenderManager3D.hpp is missing, so the PS2 native contract cannot be validated.");
        }
        if (!File.Exists(nativeRenderManagerHeaderPath)) {
            throw new InvalidOperationException("PS2 renderer header is missing, so the PS2 native contract cannot be validated.");
        }

        string generatedRenderManagerHeader = File.ReadAllText(generatedRenderManagerHeaderPath);
        string nativeRenderManagerHeader = File.ReadAllText(nativeRenderManagerHeaderPath);
        bool generatedUsesGenericCookedMaterialContract = generatedRenderManagerHeader.Contains(
            "virtual ::RuntimeMaterial* BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset);",
            StringComparison.Ordinal);
        bool generatedUsesPs2CookedMaterialContract = generatedRenderManagerHeader.Contains(
            "virtual ::RuntimeMaterial* BuildMaterialFromCooked(::Ps2MaterialAsset* materialAsset);",
            StringComparison.Ordinal);
        bool generatedUsesRawMaterialContract = generatedRenderManagerHeader.Contains(
            "virtual ::RuntimeMaterial* BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset);",
            StringComparison.Ordinal);
        bool nativeOverridesGenericCookedMaterialContract = nativeRenderManagerHeader.Contains(
            "::RuntimeMaterial* BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset) override;",
            StringComparison.Ordinal);
        bool nativeOverridesPs2CookedMaterialContract = nativeRenderManagerHeader.Contains(
            "::RuntimeMaterial* BuildMaterialFromCooked(::Ps2MaterialAsset* materialAsset) override;",
            StringComparison.Ordinal);
        bool nativeOverridesRawMaterialContract = nativeRenderManagerHeader.Contains(
            "::RuntimeMaterial* BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset) override;",
            StringComparison.Ordinal);
        if (generatedUsesGenericCookedMaterialContract && (!nativeOverridesGenericCookedMaterialContract || !nativeOverridesRawMaterialContract)) {
            throw new InvalidOperationException(
                "Generated core expects the generic cooked-platform-material RenderManager3D contract, but the PS2 renderer sources do not implement it. " +
                "This would pair a newer generated runtime with an older PS2 renderer layout and can dispatch material loading into the wrong virtual slot at startup.");
        }

        if (generatedUsesPs2CookedMaterialContract && (!nativeOverridesPs2CookedMaterialContract || !nativeOverridesRawMaterialContract)) {
            throw new InvalidOperationException(
                "Generated core still emits the PS2-specific cooked-material RenderManager3D contract, but the PS2 renderer sources do not implement that current runtime layout. " +
                "This would pair a generated runtime with the wrong native override and can dispatch material loading into the wrong virtual slot at startup.");
        }

        if (generatedUsesRawMaterialContract && !nativeOverridesRawMaterialContract) {
            throw new InvalidOperationException(
                "Generated core expects the raw-material RenderManager3D contract, but the PS2 renderer sources do not override BuildMaterialFromRaw. " +
                "This would pair the current generated runtime with an incompatible PS2 renderer layout.");
        }

        if (!generatedUsesGenericCookedMaterialContract && !generatedUsesPs2CookedMaterialContract && !generatedUsesRawMaterialContract) {
            throw new InvalidOperationException(
                "Generated RenderManager3D.hpp does not expose any recognized material contract. " +
                "Inspect the current helengine generated-core output before exporting PS2.");
        }
    }

    /// <summary>
    /// Applies one PS2-specific compatibility normalization to a single generated-core source file.
    /// </summary>
    /// <param name="generatedCoreRootPath">Absolute generated-core root produced by the editor build graph.</param>
    /// <param name="fileName">Generated source file name to normalize.</param>
    static void NormalizeGeneratedCoreFile(string generatedCoreRootPath, string fileName) {
        string sourcePath = Path.Combine(generatedCoreRootPath, fileName);
        if (!File.Exists(sourcePath)) {
            return;
        }

        string contents = File.ReadAllText(sourcePath);
        string updatedContents = NormalizeGeneratedCoreSource(fileName, contents);
        if (!string.Equals(contents, updatedContents, StringComparison.Ordinal)) {
            File.WriteAllText(sourcePath, updatedContents);
        }
    }

    /// <summary>
    /// Applies the remaining PS2 compatibility normalizations to one generated-core source buffer.
    /// </summary>
    /// <param name="fileName">Generated source file name.</param>
    /// <param name="contents">Generated source contents.</param>
    /// <returns>Normalized generated source contents.</returns>
    static string NormalizeGeneratedCoreSource(string fileName, string contents) {
        if (string.IsNullOrWhiteSpace(fileName)) {
            throw new ArgumentException("Generated source file name must be provided.", nameof(fileName));
        }
        if (contents == null) {
            throw new ArgumentNullException(nameof(contents));
        }

        string normalizedContents = contents.Replace("\r\n", "\n", StringComparison.Ordinal);
        normalizedContents = normalizedContents.Replace("LightType::hpp", "LightType.hpp", StringComparison.Ordinal);
        if (string.Equals(fileName, "ScrollComponent.cpp", StringComparison.OrdinalIgnoreCase)) {
            return normalizedContents.Replace("SizeValue(new int2())", "SizeValue(int2())", StringComparison.Ordinal);
        }

        if (string.Equals(fileName, "FontAsset.cpp", StringComparison.OrdinalIgnoreCase)) {
            normalizedContents = normalizedContents.Replace(
                "entry.get_Value()",
                "entry.second",
                StringComparison.Ordinal);
            normalizedContents = normalizedContents.Replace(
                "entry.get_Key()",
                "entry.first",
                StringComparison.Ordinal);
            return normalizedContents;
        }

        if (string.Equals(fileName, "FPSComponent.hpp", StringComparison.OrdinalIgnoreCase)) {
            return NormalizeFpsComponentHeaderSource(normalizedContents);
        }

        if (string.Equals(fileName, "FPSComponent.cpp", StringComparison.OrdinalIgnoreCase)) {
            return NormalizeFpsComponentSource(normalizedContents);
        }

        if (string.Equals(fileName, Path.Combine("system", "io", "file-stream.cpp"), StringComparison.OrdinalIgnoreCase)) {
            normalizedContents = normalizedContents.Replace(
                "        memoryBuffer = ReadPs2DiscFile(resolvedPs2ReadPath);\n        usesMemoryBuffer = true;\n        length = memoryBuffer.size();\n        return;\n",
                "        memoryBuffer = ReadPs2DiscFile(resolvedPs2ReadPath);\n        ownsMemoryBuffer = true;\n        writable = false;\n        length = memoryBuffer.size();\n        return;\n",
                StringComparison.Ordinal);
            return normalizedContents;
        }

        if (string.Equals(fileName, "RuntimeContentManagerConfiguration.cpp", StringComparison.OrdinalIgnoreCase)) {
            return NormalizeRuntimeContentManagerConfigurationSource(normalizedContents);
        }

        if (string.Equals(fileName, "RuntimeSceneAssetReferenceResolver.cpp", StringComparison.OrdinalIgnoreCase)) {
            return NormalizeRuntimeSceneAssetReferenceResolverSource(normalizedContents);
        }

        if (string.Equals(fileName, "RenderManager3D.hpp", StringComparison.OrdinalIgnoreCase)) {
            return NormalizeRenderManager3DHeaderSource(normalizedContents);
        }

        if (string.Equals(fileName, "RenderManager3D.cpp", StringComparison.OrdinalIgnoreCase)) {
            return NormalizeRenderManager3DSource(normalizedContents);
        }

        if (string.Equals(fileName, "Core.cpp", StringComparison.OrdinalIgnoreCase)) {
            normalizedContents = normalizedContents.Replace(
                "Console::WriteLine(std::string(\"[SceneTrace] \") + message + std::string(\" sceneManager=null\"));",
                "Console::WriteLine(String::GetCString(std::string(\"[SceneTrace] \") + message + std::string(\" sceneManager=null\")));",
                StringComparison.Ordinal);
            normalizedContents = normalizedContents.Replace(
                "Console::WriteLine(std::string(\"[SceneTrace] \") + message + std::string(\" loadedSceneCount=\") + std::to_string(loadedScenes->get_Count()) + std::string(\" primarySceneId=\") + primarySceneId);",
                "Console::WriteLine(String::GetCString(std::string(\"[SceneTrace] \") + message + std::string(\" loadedSceneCount=\") + std::to_string(loadedScenes->get_Count()) + std::string(\" primarySceneId=\") + primarySceneId));",
                StringComparison.Ordinal);
            return normalizedContents;
        }

        if (string.Equals(fileName, "SceneManager.cpp", StringComparison.OrdinalIgnoreCase)) {
            normalizedContents = normalizedContents.Replace(
                "Console::WriteLine(std::string(\"[SceneTrace] \") + message + std::string(\" loadedSceneCount=\") + std::to_string(LoadedSceneRecords->get_Count()) + std::string(\" primarySceneId=\") + primarySceneId);",
                "Console::WriteLine(String::GetCString(std::string(\"[SceneTrace] \") + message + std::string(\" loadedSceneCount=\") + std::to_string(LoadedSceneRecords->get_Count()) + std::string(\" primarySceneId=\") + primarySceneId));",
                StringComparison.Ordinal);
            normalizedContents = normalizedContents.Replace(
                "DeleteGeneratedArray(",
                "DeleteGeneratedArray_SceneManager(",
                StringComparison.Ordinal);
            return normalizedContents;
        }

        if (string.Equals(fileName, Path.Combine("runtime", "runtime_graphics_renderer_manifest.cpp"), StringComparison.OrdinalIgnoreCase)) {
            return NormalizeRuntimeGraphicsRendererManifestSource(normalizedContents);
        }

        return normalizedContents;
    }

    /// <summary>
    /// Normalizes the generated FPS component header so PS2 exports declare the private helper used by the generated source.
    /// </summary>
    /// <param name="contents">Generated FPS component header source.</param>
    /// <returns>Normalized FPS component header source.</returns>
    static string NormalizeFpsComponentHeaderSource(string contents) {
        const string declaration = "    std::string FormatOverlaySecondaryLine(std::string baseRenderText);\n";
        if (contents.Contains(declaration, StringComparison.Ordinal)) {
            return contents;
        }

        return ReplaceRequired(
            contents,
            "    std::string FormatRenderFpsText(double renderFps, double drawMilliseconds);\n",
            "    std::string FormatRenderFpsText(double renderFps, double drawMilliseconds);\n\n" + declaration,
            "PS2 generated FPSComponent.hpp should declare FormatOverlaySecondaryLine.");
    }

    /// <summary>
    /// Normalizes the generated FPS component source so PS2 exports call the private overlay helper through the instance.
    /// </summary>
    /// <param name="contents">Generated FPS component source.</param>
    /// <returns>Normalized FPS component source.</returns>
    static string NormalizeFpsComponentSource(string contents) {
        return contents.Replace(
            "this->RenderTextComponent->set_Text(FormatOverlaySecondaryLine(this->RenderFpsText));",
            "this->RenderTextComponent->set_Text(this->FormatOverlaySecondaryLine(this->RenderFpsText));",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Normalizes generated runtime content-manager registration so PS2 exports use the cooked-platform material contract.
    /// </summary>
    /// <param name="contents">Generated runtime content-manager source.</param>
    /// <returns>Normalized runtime content-manager source.</returns>
    static string NormalizeRuntimeContentManagerConfigurationSource(string contents) {
        if (contents.Contains("RegisterProcessorIfMissing<PlatformMaterialAsset*>", StringComparison.Ordinal)) {
            return contents;
        }

        string normalizedContents = contents;
        if (!normalizedContents.Contains("#include \"PlatformMaterialAsset.hpp\"", StringComparison.Ordinal)) {
            normalizedContents = normalizedContents.Replace(
                "#include \"MaterialAsset.hpp\"\n",
                "#include \"MaterialAsset.hpp\"\n#include \"PlatformMaterialAsset.hpp\"\n",
                StringComparison.Ordinal);
        }

        return ReplaceRequired(
            normalizedContents,
            "RegisterProcessorIfMissing<MaterialAsset*>(contentManager, RuntimeContentProcessorIds::MaterialAsset, new ::AssetContentProcessor_1<::MaterialAsset*>(), new Array<std::string>({ MaterialAssetExtension }));",
            "RegisterProcessorIfMissing<PlatformMaterialAsset*>(contentManager, RuntimeContentProcessorIds::MaterialAsset, new ::AssetContentProcessor_1<::PlatformMaterialAsset*>(), new Array<std::string>({ MaterialAssetExtension }));",
            "PS2 generated runtime content manager should register cooked platform materials through PlatformMaterialAsset.");
    }

    /// <summary>
    /// Normalizes generated scene material resolution so PS2 exports consume cooked platform-owned material assets.
    /// </summary>
    /// <param name="contents">Generated runtime scene asset resolver source.</param>
    /// <returns>Normalized runtime scene asset resolver source.</returns>
    static string NormalizeRuntimeSceneAssetReferenceResolverSource(string contents) {
        if (contents.Contains("BuildMaterialFromCooked(materialAsset)", StringComparison.Ordinal)
            && contents.Contains("PlatformMaterialAsset *materialAsset", StringComparison.Ordinal)) {
            return contents;
        }

        string normalizedContents = contents;
        if (!normalizedContents.Contains("#include \"PlatformMaterialAsset.hpp\"", StringComparison.Ordinal)) {
            normalizedContents = normalizedContents.Replace(
                "#include \"MaterialAsset.hpp\"\n",
                "#include \"MaterialAsset.hpp\"\n#include \"PlatformMaterialAsset.hpp\"\n",
                StringComparison.Ordinal);
        }

        const string modernRawMaterialBlock = "::MaterialAsset *materialAsset = this->AssetContentManager->Load<MaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);\n::ShaderAsset *shaderAsset = this->AssetContentManager->Load<ShaderAsset*>(this->ResolveShaderPackagePath(materialAsset->ShaderAssetId), RuntimeContentProcessorIds::ShaderAsset);\n::RuntimeMaterial *runtimeMaterial = Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromRaw(materialAsset, shaderAsset);\nthis->TrackOwnedMaterial(runtimeMaterial);\nthis->ApplyMaterialDiffuseTexture(runtimeMaterial, materialAsset, fullPath);\nreturn runtimeMaterial;}";
        if (normalizedContents.Contains(modernRawMaterialBlock, StringComparison.Ordinal)) {
            return normalizedContents.Replace(
                modernRawMaterialBlock,
                "::PlatformMaterialAsset *materialAsset = this->AssetContentManager->Load<PlatformMaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);\n::RuntimeMaterial *runtimeMaterial = Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromCooked(materialAsset);\nthis->TrackOwnedMaterial(runtimeMaterial);\nreturn runtimeMaterial;}",
                StringComparison.Ordinal);
        }

        const string legacyRawMaterialBlock = "::MaterialAsset *materialAsset = this->AssetContentManager->Load<MaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);\nauto __releaseMaterialAssetGuard = he_cpp_make_scope_exit([&]() {\nReleaseTransientMaterialAsset(materialAsset);\n});\n::ShaderAsset *shaderAsset = this->AssetContentManager->Load<ShaderAsset*>(this->ResolveShaderPackagePath(materialAsset->ShaderAssetId), RuntimeContentProcessorIds::ShaderAsset);\nauto __releaseShaderAssetGuard = he_cpp_make_scope_exit([&]() {\nReleaseTransientShaderAsset(shaderAsset);\n});\n::RuntimeMaterial *runtimeMaterial = Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromRaw(materialAsset, shaderAsset);\nthis->TrackOwnedMaterial(runtimeMaterial);\nthis->ApplyMaterialDiffuseTexture(runtimeMaterial, materialAsset, fullPath);\nreturn runtimeMaterial;}";
        if (normalizedContents.Contains(legacyRawMaterialBlock, StringComparison.Ordinal)) {
            return normalizedContents.Replace(
                legacyRawMaterialBlock,
                "::PlatformMaterialAsset *materialAsset = this->AssetContentManager->Load<PlatformMaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);\n::RuntimeMaterial *runtimeMaterial = Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromCooked(materialAsset);\nthis->TrackOwnedMaterial(runtimeMaterial);\nreturn runtimeMaterial;}",
                StringComparison.Ordinal);
        }

        throw new InvalidOperationException("PS2 generated runtime scene asset resolver should resolve materials through the cooked platform material contract.");
    }

    /// <summary>
    /// Normalizes the generated render-manager header so PS2 exports expose the cooked-platform material seam.
    /// </summary>
    /// <param name="contents">Generated render-manager header source.</param>
    /// <returns>Normalized render-manager header source.</returns>
    static string NormalizeRenderManager3DHeaderSource(string contents) {
        if (contents.Contains("virtual ::RuntimeMaterial* BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset);", StringComparison.Ordinal)) {
            return contents;
        }

        string normalizedContents = contents;
        if (!normalizedContents.Contains("class PlatformMaterialAsset;", StringComparison.Ordinal)) {
            normalizedContents = normalizedContents.Replace(
                "class MaterialAsset;\n",
                "class MaterialAsset;\nclass PlatformMaterialAsset;\n",
                StringComparison.Ordinal);
        }

        return ReplaceRequired(
            normalizedContents,
            "    virtual ::RuntimeMaterial* BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset);\n",
            "    virtual ::RuntimeMaterial* BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset);\n\n    virtual ::RuntimeMaterial* BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset);\n",
            "PS2 generated RenderManager3D.hpp should expose the cooked platform material contract.");
    }

    /// <summary>
    /// Normalizes the generated render-manager source so PS2 exports provide the cooked-platform default implementation.
    /// </summary>
    /// <param name="contents">Generated render-manager source.</param>
    /// <returns>Normalized render-manager source.</returns>
    static string NormalizeRenderManager3DSource(string contents) {
        if (contents.Contains("BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset)", StringComparison.Ordinal)) {
            return contents;
        }

        string cookedImplementation = """
::RuntimeMaterial* RenderManager3D::BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset)
{
throw new NotSupportedException("This renderer does not support platform-owned cooked material creation.");
}

""";
        return ReplaceRequired(
            contents,
            "::RuntimeMaterial* RenderManager3D::BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset)\n",
            cookedImplementation + "::RuntimeMaterial* RenderManager3D::BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset)\n",
            "PS2 generated RenderManager3D.cpp should expose the cooked platform material default implementation.");
    }

    /// <summary>
    /// Normalizes the generated runtime graphics manifest so PS2 exports stay on the simplest supported renderer path.
    /// </summary>
    /// <param name="contents">Generated runtime graphics manifest source.</param>
    /// <returns>Normalized generated source contents.</returns>
    static string NormalizeRuntimeGraphicsRendererManifestSource(string contents) {
        string normalizedContents = ReplaceRequired(
            contents,
            "    true,\n",
            "    false,\n",
            "PS2 generated runtime graphics manifest should disable HDR.");
        normalizedContents = ReplaceRequired(
            normalizedContents,
            "    HERuntimePostProcessTier::High,\n",
            "    HERuntimePostProcessTier::Disabled,\n",
            "PS2 generated runtime graphics manifest should disable post-processing.");
        return normalizedContents;
    }

    /// <summary>
    /// Replaces one required generated-source fragment and throws when the expected source shape is absent.
    /// </summary>
    /// <param name="contents">Generated source contents to rewrite.</param>
    /// <param name="oldValue">Exact generated fragment that must exist.</param>
    /// <param name="newValue">Replacement fragment written when the expected fragment exists.</param>
    /// <param name="failureMessage">Detailed failure message describing the missing generated-source contract.</param>
    /// <returns>Rewritten generated source contents.</returns>
    static string ReplaceRequired(string contents, string oldValue, string newValue, string failureMessage) {
        if (!contents.Contains(oldValue, StringComparison.Ordinal)) {
            throw new InvalidOperationException(failureMessage);
        }

        return contents.Replace(oldValue, newValue, StringComparison.Ordinal);
    }

    /// <summary>
    /// Packages the staged PS2 disc layout into one bootable ISO image.
    /// </summary>
    /// <param name="workspace">Prepared PS2 build workspace.</param>
    /// <param name="cancellationToken">Cancellation token used to stop the packaging cooperatively.</param>
    public void PackageIso(Ps2BuildWorkspace workspace, CancellationToken cancellationToken) {
        if (workspace == null) {
            throw new ArgumentNullException(nameof(workspace));
        }

        Directory.CreateDirectory(workspace.OutputRootPath);
        RunProcess(
            "docker",
            CreatePackageIsoArguments(workspace),
            workspace.RepositoryRootPath,
            cancellationToken);
    }

    /// <summary>
    /// Builds the Docker command line used to package one staged PS2 disc layout into a bootable ISO image.
    /// </summary>
    /// <param name="workspace">Prepared PS2 build workspace.</param>
    /// <returns>Ordered arguments for the Docker invocation.</returns>
    public static IReadOnlyList<string> CreatePackageIsoArguments(Ps2BuildWorkspace workspace) {
        if (workspace == null) {
            throw new ArgumentNullException(nameof(workspace));
        }

        return
        [
            "run",
            "--rm",
            "-v",
            $"{workspace.OutputRootPath}:/export",
            "-w",
            "/export",
            DockerImageTag,
            "xorriso",
            "-as",
            "mkisofs",
            "-iso-level",
            "2",
            "-V",
            "HELENGINE_PS2",
            "-o",
            "/export/game.iso",
            "/export/disc"
        ];
    }

    /// <summary>
    /// Runs one child process and throws when the exit code is non-zero.
    /// </summary>
    /// <param name="fileName">Executable name to start.</param>
    /// <param name="arguments">Arguments passed to the executable.</param>
    /// <param name="workingDirectory">Current working directory for the child process.</param>
    /// <param name="cancellationToken">Cancellation token used to stop the wait loop cooperatively.</param>
    static void RunProcess(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken) {
        ProcessStartInfo startInfo = new() {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        for (int index = 0; index < arguments.Count; index++) {
            startInfo.ArgumentList.Add(arguments[index]);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
        while (!process.HasExited) {
            cancellationToken.ThrowIfCancellationRequested();
            process.WaitForExit(100);
        }

        process.WaitForExit();
        Task.WaitAll(standardOutputTask, standardErrorTask);
        string standardOutput = standardOutputTask.Result;
        string standardError = standardErrorTask.Result;
        if (process.ExitCode != 0) {
            throw new InvalidOperationException(
                $"Process '{fileName}' exited with code {process.ExitCode}.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        }
    }
}
