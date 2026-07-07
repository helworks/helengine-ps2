using System.Diagnostics;
using System.Text;

namespace helengine.ps2.builder;

/// <summary>
/// Builds the native PS2 ELF and packages the staged ISO through the Docker-based toolchain.
/// </summary>
public sealed class Ps2NativeBuildExecutor : IPs2NativeBuildExecutor {
    /// <summary>
    /// Docker image tag used for the PS2 toolchain container.
    /// </summary>
    const string DockerImageTag = "helengine-ps2";

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
        ValidateGeneratedCoreSources(workspace.GeneratedCoreRootPath);

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
    /// Validates the generated-core files whose runtime contracts must already match the PS2 native build expectations.
    /// </summary>
    /// <param name="generatedCoreRootPath">Absolute generated-core root produced by the editor build graph.</param>
    static void ValidateGeneratedCoreSources(string generatedCoreRootPath) {
        if (string.IsNullOrWhiteSpace(generatedCoreRootPath)) {
            throw new ArgumentException("Generated core root path must be provided.", nameof(generatedCoreRootPath));
        }

        if (!Directory.Exists(generatedCoreRootPath)) {
            return;
        }

        ValidateGeneratedCoreFile(generatedCoreRootPath, "RuntimeContentManagerConfiguration.cpp");
        ValidateGeneratedCoreFile(generatedCoreRootPath, "RuntimeSceneAssetReferenceResolver.cpp");
        ValidateGeneratedCoreFile(generatedCoreRootPath, "RenderManager3D.hpp");
        ValidateGeneratedCoreFile(generatedCoreRootPath, "RenderManager3D.cpp");
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
        if (generatedUsesGenericCookedMaterialContract && !nativeOverridesGenericCookedMaterialContract) {
            throw new InvalidOperationException(
                "Generated core expects the generic cooked-platform-material RenderManager3D contract, but the PS2 renderer sources do not implement it. " +
                "This would pair a newer generated runtime with an older PS2 renderer layout and can dispatch material loading into the wrong virtual slot at startup.");
        }

        if (generatedUsesPs2CookedMaterialContract && !nativeOverridesPs2CookedMaterialContract) {
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
    /// Validates one generated-core source file against the remaining PS2 runtime contract expectations.
    /// </summary>
    /// <param name="generatedCoreRootPath">Absolute generated-core root produced by the editor build graph.</param>
    /// <param name="fileName">Generated source file name to validate.</param>
    static void ValidateGeneratedCoreFile(string generatedCoreRootPath, string fileName) {
        string sourcePath = Path.Combine(generatedCoreRootPath, fileName);
        if (!File.Exists(sourcePath)) {
            return;
        }

        string contents = File.ReadAllText(sourcePath);
        ValidateGeneratedCoreSource(fileName, contents);
    }

    /// <summary>
    /// Validates one generated-core source buffer against the remaining PS2 runtime contract expectations.
    /// </summary>
    /// <param name="fileName">Generated source file name.</param>
    /// <param name="contents">Generated source contents.</param>
    static void ValidateGeneratedCoreSource(string fileName, string contents) {
        if (string.IsNullOrWhiteSpace(fileName)) {
            throw new ArgumentException("Generated source file name must be provided.", nameof(fileName));
        }
        if (contents == null) {
            throw new ArgumentNullException(nameof(contents));
        }

        if (string.Equals(fileName, "RuntimeContentManagerConfiguration.cpp", StringComparison.OrdinalIgnoreCase)) {
            ValidateRuntimeContentManagerConfigurationSource(contents);
            return;
        }

        if (string.Equals(fileName, "RuntimeSceneAssetReferenceResolver.cpp", StringComparison.OrdinalIgnoreCase)) {
            ValidateRuntimeSceneAssetReferenceResolverSource(contents);
            return;
        }

        if (string.Equals(fileName, "RenderManager3D.hpp", StringComparison.OrdinalIgnoreCase)) {
            ValidateRenderManager3DHeaderSource(contents);
            return;
        }

        if (string.Equals(fileName, "RenderManager3D.cpp", StringComparison.OrdinalIgnoreCase)) {
            ValidateRenderManager3DSource(contents);
        }
    }

    /// <summary>
    /// Validates that generated runtime content-manager registration already uses the cooked-platform material contract required by PS2 exports.
    /// </summary>
    /// <param name="contents">Generated runtime content-manager source.</param>
    static void ValidateRuntimeContentManagerConfigurationSource(string contents) {
        if (contents.Contains("RegisterProcessorIfMissing<PlatformMaterialAsset*>", StringComparison.Ordinal)) {
            return;
        }

        if (contents.Contains("RegisterProcessorIfMissing<MaterialAsset*>", StringComparison.Ordinal)) {
            throw new InvalidOperationException("PS2 generated runtime content manager should already register cooked platform materials through PlatformMaterialAsset.");
        }

        throw new InvalidOperationException("PS2 generated runtime content manager should expose the cooked platform material registration contract.");
    }

    /// <summary>
    /// Validates that generated scene material resolution already consumes cooked platform-owned material assets.
    /// </summary>
    /// <param name="contents">Generated runtime scene asset resolver source.</param>
    static void ValidateRuntimeSceneAssetReferenceResolverSource(string contents) {
        if (contents.Contains("BuildMaterialFromCooked(generatedFullPath)", StringComparison.Ordinal)
            && contents.Contains("BuildMaterialFromCooked(fullPath)", StringComparison.Ordinal)) {
            return;
        }

        if (contents.Contains("BuildMaterialFromCooked(generatedFullPath, this->AssetContentManager->get_ContentStreamSource())", StringComparison.Ordinal)
            && contents.Contains("BuildMaterialFromCooked(fullPath, this->AssetContentManager->get_ContentStreamSource())", StringComparison.Ordinal)) {
            return;
        }

        if (contents.Contains("BuildMaterialFromCooked(materialAsset)", StringComparison.Ordinal)
            && contents.Contains("PlatformMaterialAsset *materialAsset", StringComparison.Ordinal)) {
            return;
        }

        const string modernRawMaterialBlock = "::MaterialAsset *materialAsset = this->AssetContentManager->Load<MaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);\n::ShaderAsset *shaderAsset = this->AssetContentManager->Load<ShaderAsset*>(this->ResolveShaderPackagePath(materialAsset->ShaderAssetId), RuntimeContentProcessorIds::ShaderAsset);\n::RuntimeMaterial *runtimeMaterial = Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromRaw(materialAsset, shaderAsset);\nthis->TrackOwnedMaterial(runtimeMaterial);\nthis->ApplyMaterialDiffuseTexture(runtimeMaterial, materialAsset, fullPath);\nreturn runtimeMaterial;}";
        if (contents.Contains(modernRawMaterialBlock, StringComparison.Ordinal)) {
            throw new InvalidOperationException("PS2 generated runtime scene asset resolver should already resolve cooked platform-owned materials.");
        }

        const string legacyRawMaterialBlock = "::MaterialAsset *materialAsset = this->AssetContentManager->Load<MaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);\nauto __releaseMaterialAssetGuard = he_cpp_make_scope_exit([&]() {\nReleaseTransientMaterialAsset(materialAsset);\n});\n::ShaderAsset *shaderAsset = this->AssetContentManager->Load<ShaderAsset*>(this->ResolveShaderPackagePath(materialAsset->ShaderAssetId), RuntimeContentProcessorIds::ShaderAsset);\nauto __releaseShaderAssetGuard = he_cpp_make_scope_exit([&]() {\nReleaseTransientShaderAsset(shaderAsset);\n});\n::RuntimeMaterial *runtimeMaterial = Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromRaw(materialAsset, shaderAsset);\nthis->TrackOwnedMaterial(runtimeMaterial);\nthis->ApplyMaterialDiffuseTexture(runtimeMaterial, materialAsset, fullPath);\nreturn runtimeMaterial;}";
        if (contents.Contains(legacyRawMaterialBlock, StringComparison.Ordinal)) {
            throw new InvalidOperationException("PS2 generated runtime scene asset resolver should already resolve cooked platform-owned materials.");
        }

        throw new InvalidOperationException("PS2 generated runtime scene asset resolver should already resolve materials through the cooked platform material contract.");
    }

    /// <summary>
    /// Validates that the generated render-manager header already exposes the cooked-platform material seam.
    /// </summary>
    /// <param name="contents">Generated render-manager header source.</param>
    static void ValidateRenderManager3DHeaderSource(string contents) {
        if (contents.Contains("virtual ::RuntimeMaterial* BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset);", StringComparison.Ordinal)) {
            return;
        }

        if (contents.Contains("virtual ::RuntimeMaterial* BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset);", StringComparison.Ordinal)) {
            throw new InvalidOperationException("PS2 generated RenderManager3D.hpp should already expose the cooked platform material contract.");
        }

        throw new InvalidOperationException("PS2 generated RenderManager3D.hpp should expose the cooked platform material contract.");
    }

    /// <summary>
    /// Validates that the generated render-manager source already provides the cooked-platform default implementation.
    /// </summary>
    /// <param name="contents">Generated render-manager source.</param>
    static void ValidateRenderManager3DSource(string contents) {
        if (contents.Contains("BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset)", StringComparison.Ordinal)) {
            return;
        }

        if (contents.Contains("::RuntimeMaterial* RenderManager3D::BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset)", StringComparison.Ordinal)) {
            throw new InvalidOperationException("PS2 generated RenderManager3D.cpp should already expose the cooked platform material default implementation.");
        }

        throw new InvalidOperationException("PS2 generated RenderManager3D.cpp should expose the cooked platform material default implementation.");
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
        StringBuilder standardOutputBuilder = new();
        StringBuilder standardErrorBuilder = new();
        TaskCompletionSource<bool> standardOutputCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> standardErrorCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, eventArgs) => {
            if (eventArgs.Data == null) {
                standardOutputCompletionSource.TrySetResult(true);
                return;
            }

            standardOutputBuilder.AppendLine(eventArgs.Data);
        };
        process.ErrorDataReceived += (_, eventArgs) => {
            if (eventArgs.Data == null) {
                standardErrorCompletionSource.TrySetResult(true);
                return;
            }

            standardErrorBuilder.AppendLine(eventArgs.Data);
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try {
            while (!process.HasExited) {
                cancellationToken.ThrowIfCancellationRequested();
                process.WaitForExit(100);
            }
        } catch {
            try {
                if (!process.HasExited) {
                    process.Kill(entireProcessTree: true);
                }
            } catch {
            }

            throw;
        }

        process.WaitForExit();
        Task.WaitAll(standardOutputCompletionSource.Task, standardErrorCompletionSource.Task);
        string standardOutput = standardOutputBuilder.ToString();
        string standardError = standardErrorBuilder.ToString();
        if (process.ExitCode != 0) {
            throw new InvalidOperationException(
                $"Process '{fileName}' exited with code {process.ExitCode}.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        }
    }
}
