namespace helengine.ps2.builder.tests {
    /// <summary>
    /// Verifies that PS2 startup defers to the cooked startup manifest so an isolated package can contain any valid scene.
    /// </summary>
    public sealed class Ps2StartupManifestSourceTests {
        /// <summary>
        /// Ensures no diagnostic scene identifier can replace the startup scene selected by the package cook.
        /// </summary>
        [Fact]
        public void Ps2BootHost_WhenStartingAnIsolatedPackage_UsesTheCookedStartupScene() {
            string repositoryRootPath = GetRepositoryRootPath();
            string sourcePath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2BootHost.cpp");
            string source = File.ReadAllText(sourcePath);

            Assert.Contains("constexpr const char* StartupSceneDiagnosticOverrideId = nullptr;", source, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures the visible runtime marker identifies the current package that removes the diagnostic scene override.
        /// </summary>
        [Fact]
    public void Ps2BootHost_WhenRemovingTheDiagnosticOverride_UsesBuildMarkerB135() {
            string repositoryRootPath = GetRepositoryRootPath();
            string sourcePath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2BootHost.cpp");
            string source = File.ReadAllText(sourcePath);

            Assert.Contains("constexpr const char* FrameTimingOverlayBuildNumber = \"B135\";", source, StringComparison.Ordinal);
        }

        /// <summary>
        /// Resolves the repository root from the test assembly output directory.
        /// </summary>
        /// <returns>Absolute path to the PS2 repository root.</returns>
        static string GetRepositoryRootPath() {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        }
    }
}
