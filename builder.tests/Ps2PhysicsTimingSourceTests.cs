namespace helengine.ps2.builder.tests {
    /// <summary>
    /// Verifies that PS2 uses the constrained physics schedule required for its runtime performance budget.
    /// </summary>
    public sealed class Ps2PhysicsTimingSourceTests {
        /// <summary>
        /// Ensures PS2 advances fixed physics at twenty hertz and never catches up more than one step in a core update.
        /// </summary>
        [Fact]
        public void Ps2BootHost_WhenInitializingCore_UsesTwentyHertzPhysicsWithOneCatchUpStep() {
            string repositoryRootPath = GetRepositoryRootPath();
            string sourcePath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2BootHost.cpp");
            string source = File.ReadAllText(sourcePath);

            Assert.Contains("EngineOptions->set_PhysicsFixedStepSeconds(1.0 / 20.0);", source, StringComparison.Ordinal);
            Assert.Contains("EngineOptions->set_PhysicsMaxStepsPerUpdate(1);", source, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures the retail PS2 host does not opt Core into per-stage update diagnostics, which otherwise adds managed-to-native callbacks inside every BEPU timestep.
        /// </summary>
        [Fact]
        public void Ps2BootHost_WhenRunningPhysics_DoesNotAttachHighFrequencyUpdateStageDiagnostics() {
            string repositoryRootPath = GetRepositoryRootPath();
            string sourcePath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2BootHost.cpp");
            string source = File.ReadAllText(sourcePath);

            Assert.DoesNotContain("public ::IRuntimeUpdateStageDiagnosticsProvider", source, StringComparison.Ordinal);
            Assert.DoesNotContain("void ReportUpdateStage(std::string stage) override", source, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ensures the PS2 timing overlay reports the core-measured physics duration rather than inferring it from the whole update duration.
        /// </summary>
        [Fact]
        public void Ps2BootHost_WhenCollectingFrameTiming_ReportsMeasuredPhysicsDuration() {
            string repositoryRootPath = GetRepositoryRootPath();
            string sourcePath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2BootHost.cpp");
            string source = File.ReadAllText(sourcePath);

            Assert.Contains("EngineCore->get_LastPhysicsUpdateMilliseconds()", source, StringComparison.Ordinal);
            Assert.Contains("+ \" Phy \"", source, StringComparison.Ordinal);
        }

        /// <summary>
        /// Resolves the PS2 repository root from the executing test binary directory.
        /// </summary>
        /// <returns>Absolute path to the PS2 repository root.</returns>
        static string GetRepositoryRootPath() {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        }
    }
}
