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

    /// <summary>
    /// Builds one PS2 ELF for the prepared workspace.
    /// </summary>
    /// <param name="workspace">Prepared PS2 build workspace.</param>
    /// <param name="cancellationToken">Cancellation token used to stop the native build cooperatively.</param>
    public void Build(Ps2BuildWorkspace workspace, CancellationToken cancellationToken) {
        if (workspace == null) {
            throw new ArgumentNullException(nameof(workspace));
        }

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
