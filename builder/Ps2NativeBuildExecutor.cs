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

        string scrollComponentPath = Path.Combine(generatedCoreRootPath, "ScrollComponent.cpp");
        if (!File.Exists(scrollComponentPath)) {
            scrollComponentPath = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(scrollComponentPath)) {
            string contents = File.ReadAllText(scrollComponentPath);
            string updatedContents = contents.Replace("SizeValue(new int2())", "SizeValue(int2())", StringComparison.Ordinal);
            if (!string.Equals(contents, updatedContents, StringComparison.Ordinal)) {
                File.WriteAllText(scrollComponentPath, updatedContents);
            }
        }

        string resolverPath = Path.Combine(generatedCoreRootPath, "RuntimeSceneAssetReferenceResolver.cpp");
        File.AppendAllText(DebugLogPath, $"resolverPath={resolverPath} exists={File.Exists(resolverPath)}{Environment.NewLine}");
        if (File.Exists(resolverPath)) {
            string resolverContents = File.ReadAllText(resolverPath);
            string resolverUpdatedContents = resolverContents.Replace(
                "const std::string fullPath = Path::GetFullPath(Path::Combine(this->ContentRootPath, reference->get_RelativePath()));\nconst std::string contentRootPrefix = this->EnsureTrailingDirectorySeparator(this->ContentRootPath);\n    if (!String::StartsWith(fullPath, contentRootPrefix, StringComparison::OrdinalIgnoreCase))\n    {\nthrow new InvalidOperationException(\"Packaged scene asset reference path must stay inside the content root.\");\n    }\nreturn fullPath;}",
                "if (Path::IsPathRooted(reference->get_RelativePath()))\n    {\nreturn Path::GetFullPath(reference->get_RelativePath());\n    }\nconst std::string fullPath = Path::GetFullPath(Path::Combine(this->ContentRootPath, reference->get_RelativePath()));\nconst std::string contentRootPrefix = this->EnsureTrailingDirectorySeparator(this->ContentRootPath);\n    if (!String::StartsWith(fullPath, contentRootPrefix, StringComparison::OrdinalIgnoreCase))\n    {\nthrow new InvalidOperationException(\"Packaged scene asset reference path must stay inside the content root.\");\n    }\nreturn fullPath;}",
                StringComparison.Ordinal);
            resolverUpdatedContents = resolverUpdatedContents.Replace(
                "#include \"runtime/native_string.hpp\"",
                "#include \"runtime/native_string.hpp\"\n#include \"Logger.hpp\"\n#include <cstdio>",
                StringComparison.Ordinal);
            resolverUpdatedContents = resolverUpdatedContents.Replace(
                "const std::string fullPath = this->ResolveFileBackedAssetPath(reference);\n::Ps2MaterialAsset *materialAsset = this->AssetContentManager->Load<Ps2MaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);\nreturn Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromCooked(materialAsset);}",
                "const std::string fullPath = this->ResolveFileBackedAssetPath(reference);\nstd::printf(\"[generated-core] ResolveMaterial %s\\n\", fullPath.c_str());\nstd::fflush(stdout);\nLogger::WriteLine(std::string(\"ResolveMaterial path=\") + fullPath);\n::Ps2MaterialAsset *materialAsset = this->AssetContentManager->Load<Ps2MaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);\nLogger::WriteLine(std::string(\"ResolveMaterial asset loaded path=\") + fullPath);\n::RuntimeMaterial* runtimeMaterial = Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromCooked(materialAsset);\nLogger::WriteLine(std::string(\"ResolveMaterial runtime material built path=\") + fullPath);\nreturn runtimeMaterial;}",
                StringComparison.Ordinal);
            resolverUpdatedContents = resolverUpdatedContents.Replace(
                "const std::string fullPath = this->ResolveFileBackedAssetPath(reference);\n::MaterialAsset *materialAsset = this->AssetContentManager->Load<MaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);\n::ShaderAsset *shaderAsset = this->AssetContentManager->Load<ShaderAsset*>(this->ResolveShaderPackagePath(materialAsset->ShaderAssetId), RuntimeContentProcessorIds::ShaderAsset);\n::RuntimeMaterial *runtimeMaterial = Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromRaw(materialAsset, shaderAsset);\nthis->ApplyMaterialDiffuseTexture(runtimeMaterial, materialAsset, fullPath);\nreturn runtimeMaterial;}",
                "const std::string fullPath = this->ResolveFileBackedAssetPath(reference);\nstd::printf(\"[generated-core] ResolveMaterial %s\\n\", fullPath.c_str());\nstd::fflush(stdout);\nLogger::WriteLine(std::string(\"ResolveMaterial path=\") + fullPath);\n::Ps2MaterialAsset *materialAsset = this->AssetContentManager->Load<Ps2MaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);\nLogger::WriteLine(std::string(\"ResolveMaterial asset loaded path=\") + fullPath);\n::RuntimeMaterial* runtimeMaterial = Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromCooked(materialAsset);\nLogger::WriteLine(std::string(\"ResolveMaterial runtime material built path=\") + fullPath);\nreturn runtimeMaterial;}",
                StringComparison.Ordinal);
            bool resolverChanged = !string.Equals(resolverContents, resolverUpdatedContents, StringComparison.Ordinal);
            bool containsMaterialAssetLoad = resolverContents.Contains(
                "Load<MaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset)",
                StringComparison.Ordinal);
            File.AppendAllText(
                DebugLogPath,
                $"resolverChanged={resolverChanged} containsMaterialAssetLoad={containsMaterialAssetLoad}{Environment.NewLine}");
            if (resolverChanged) {
                File.WriteAllText(resolverPath, resolverUpdatedContents);
            }
        }

        string runtimeContentManagerConfigurationPath = Path.Combine(generatedCoreRootPath, "RuntimeContentManagerConfiguration.cpp");
        File.AppendAllText(DebugLogPath, $"runtimeContentManagerConfigurationPath={runtimeContentManagerConfigurationPath} exists={File.Exists(runtimeContentManagerConfigurationPath)}{Environment.NewLine}");
        if (File.Exists(runtimeContentManagerConfigurationPath)) {
            string configurationContents = File.ReadAllText(runtimeContentManagerConfigurationPath);
            string updatedConfigurationContents = configurationContents.Replace(
                "#include \"MaterialAsset.hpp\"",
                "#include \"MaterialAsset.hpp\"\n#include \"Ps2MaterialAsset.hpp\"",
                StringComparison.Ordinal);
            updatedConfigurationContents = updatedConfigurationContents.Replace(
                "RegisterProcessorIfMissing<MaterialAsset*>(contentManager, RuntimeContentProcessorIds::MaterialAsset, new ::AssetContentProcessor_1<::MaterialAsset*>(), new Array<std::string>({ MaterialAssetExtension }));",
                "RegisterProcessorIfMissing<Ps2MaterialAsset*>(contentManager, RuntimeContentProcessorIds::MaterialAsset, new ::AssetContentProcessor_1<::Ps2MaterialAsset*>(), new Array<std::string>({ MaterialAssetExtension }));",
                StringComparison.Ordinal);
            bool runtimeContentChanged = !string.Equals(configurationContents, updatedConfigurationContents, StringComparison.Ordinal);
            bool containsRegisterProcessorIfMissingMaterial = configurationContents.Contains(
                "RegisterProcessorIfMissing<MaterialAsset*>(contentManager, RuntimeContentProcessorIds::MaterialAsset",
                StringComparison.Ordinal);
            File.AppendAllText(
                DebugLogPath,
                $"runtimeContentChanged={runtimeContentChanged} containsRegisterProcessorIfMissingMaterial={containsRegisterProcessorIfMissingMaterial}{Environment.NewLine}");
            if (runtimeContentChanged) {
                File.WriteAllText(runtimeContentManagerConfigurationPath, updatedConfigurationContents);
            }
        }

        string coreSourcePath = Path.Combine(generatedCoreRootPath, "Core.cpp");
        if (File.Exists(coreSourcePath)) {
            string coreContents = File.ReadAllText(coreSourcePath).Replace("\r\n", "\n", StringComparison.Ordinal);
            string updatedCoreContents = coreContents.Replace(
                "Console::WriteLine(std::string(\"[SceneTrace] \") + message + std::string(\" sceneManager=null\"));",
                "Console::WriteLine(String::GetCString(std::string(\"[SceneTrace] \") + message + std::string(\" sceneManager=null\")));",
                StringComparison.Ordinal);
            updatedCoreContents = updatedCoreContents.Replace(
                "Console::WriteLine(std::string(\"[SceneTrace] \") + message + std::string(\" loadedSceneCount=\") + std::to_string(loadedScenes->get_Count()) + std::string(\" primarySceneId=\") + primarySceneId);",
                "Console::WriteLine(String::GetCString(std::string(\"[SceneTrace] \") + message + std::string(\" loadedSceneCount=\") + std::to_string(loadedScenes->get_Count()) + std::string(\" primarySceneId=\") + primarySceneId));",
                StringComparison.Ordinal);
            if (!string.Equals(coreContents, updatedCoreContents, StringComparison.Ordinal)) {
                File.WriteAllText(coreSourcePath, updatedCoreContents);
            }
        }

        string sceneManagerSourcePath = Path.Combine(generatedCoreRootPath, "SceneManager.cpp");
        if (File.Exists(sceneManagerSourcePath)) {
            string sceneManagerContents = File.ReadAllText(sceneManagerSourcePath).Replace("\r\n", "\n", StringComparison.Ordinal);
            string updatedSceneManagerContents = sceneManagerContents.Replace(
                "Console::WriteLine(std::string(\"[SceneTrace] \") + message + std::string(\" loadedSceneCount=\") + std::to_string(LoadedSceneRecords->get_Count()) + std::string(\" primarySceneId=\") + primarySceneId);",
                "Console::WriteLine(String::GetCString(std::string(\"[SceneTrace] \") + message + std::string(\" loadedSceneCount=\") + std::to_string(LoadedSceneRecords->get_Count()) + std::string(\" primarySceneId=\") + primarySceneId));",
                StringComparison.Ordinal);
            if (!string.Equals(sceneManagerContents, updatedSceneManagerContents, StringComparison.Ordinal)) {
                File.WriteAllText(sceneManagerSourcePath, updatedSceneManagerContents);
            }
        }

        string renderManagerHeaderPath = Path.Combine(generatedCoreRootPath, "RenderManager3D.hpp");
        File.AppendAllText(DebugLogPath, $"renderManagerHeaderPath={renderManagerHeaderPath} exists={File.Exists(renderManagerHeaderPath)}{Environment.NewLine}");
        if (File.Exists(renderManagerHeaderPath)) {
            string headerContents = File.ReadAllText(renderManagerHeaderPath).Replace("\r\n", "\n", StringComparison.Ordinal);
            string updatedHeaderContents = headerContents.Replace(
                "class RuntimeMaterial;\nclass MaterialAsset;\nclass ShaderAsset;",
                "class RuntimeMaterial;\nclass MaterialAsset;\nclass Ps2MaterialAsset;\nclass ShaderAsset;",
                StringComparison.Ordinal);
            updatedHeaderContents = updatedHeaderContents.Replace(
                "    virtual ::RuntimeMaterial* BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset);\n",
                "    virtual ::RuntimeMaterial* BuildMaterialFromCooked(::Ps2MaterialAsset* materialAsset);\n\n    virtual ::RuntimeMaterial* BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset);\n",
                StringComparison.Ordinal);
            if (!string.Equals(headerContents, updatedHeaderContents, StringComparison.Ordinal)) {
                File.WriteAllText(renderManagerHeaderPath, updatedHeaderContents);
            }
        }

        string renderManagerSourcePath = Path.Combine(generatedCoreRootPath, "RenderManager3D.cpp");
        File.AppendAllText(DebugLogPath, $"renderManagerSourcePath={renderManagerSourcePath} exists={File.Exists(renderManagerSourcePath)}{Environment.NewLine}");
        if (File.Exists(renderManagerSourcePath)) {
            string sourceContents = File.ReadAllText(renderManagerSourcePath).Replace("\r\n", "\n", StringComparison.Ordinal);
            string updatedSourceContents = sourceContents.Replace(
                "::RuntimeMaterial* RenderManager3D::BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset)\n{\nthrow new NotSupportedException(\"This renderer does not support material creation.\");\n}\n",
                "::RuntimeMaterial* RenderManager3D::BuildMaterialFromCooked(::Ps2MaterialAsset* materialAsset)\n{\nthrow new NotSupportedException(\"This renderer does not support material creation.\");\n}\n\n::RuntimeMaterial* RenderManager3D::BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset)\n{\nthrow new NotSupportedException(\"This renderer does not support material creation.\");\n}\n",
                StringComparison.Ordinal);
            if (!string.Equals(sourceContents, updatedSourceContents, StringComparison.Ordinal)) {
                File.WriteAllText(renderManagerSourcePath, updatedSourceContents);
            }
        }
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
