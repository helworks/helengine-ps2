namespace helengine.ps2.builder.tests;

/// <summary>
/// Guards the canonical PS2 emulator launcher contract.
/// </summary>
public sealed class Ps2Pcsx2LauncherScriptTests {
    /// <summary>
    /// Ensures the canonical launcher requires one explicit artifact path and preserves the PCSX2 fastboot workflow.
    /// </summary>
    [Fact]
    public void Launcher_RequiresArtifactPath_AndKeepsPcsx2FastbootContract() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string scriptPath = Path.Combine(repositoryRootPath, "scripts", "launch_in_emulator.ps1");

        Assert.True(File.Exists(scriptPath), "Expected scripts/launch_in_emulator.ps1 to exist.");

        string scriptSource = File.ReadAllText(scriptPath);

        Assert.Contains("[Parameter(Mandatory = $true)]", scriptSource, StringComparison.Ordinal);
        Assert.Contains("[string]$ArtifactPath", scriptSource, StringComparison.Ordinal);
        Assert.Contains("pcsx2-qt.exe", scriptSource, StringComparison.Ordinal);
        Assert.Contains("Get-Process -Name 'pcsx2-qt'", scriptSource, StringComparison.Ordinal);
        Assert.Contains("'-fastboot', '-logfile', $logFilePath, '--', $resolvedArtifactPath", scriptSource, StringComparison.Ordinal);
        Assert.Contains("PROCESS_ID=", scriptSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[string]$IsoPath", scriptSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the root README points users at the canonical launcher entrypoint.
    /// </summary>
    [Fact]
    public void Readme_DocumentsCanonicalLauncherWorkflow() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string readmeSource = File.ReadAllText(Path.Combine(repositoryRootPath, "README.md"));

        Assert.Contains("launch_in_emulator.ps1", readmeSource, StringComparison.Ordinal);
        Assert.Contains("-ArtifactPath", readmeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("launch_ps2_iso_in_pcsx2.ps1", readmeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("-IsoPath", readmeSource, StringComparison.Ordinal);
    }
}
