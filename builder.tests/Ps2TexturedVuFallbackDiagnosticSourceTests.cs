namespace helengine.ps2.builder.tests {
    /// <summary>
    /// Verifies that the PS2 renderer can bypass VIF/VU textured submission while diagnosing a stalled VIF channel.
    /// </summary>
    public sealed class Ps2TexturedVuFallbackDiagnosticSourceTests {
        /// <summary>
        /// Ensures the VIF/VU fast path has an explicit diagnostic gate and the default diagnostic state selects direct-GIF textured submission.
        /// </summary>
        [Fact]
        public void Ps2RenderManager3D_CanRouteTexturedGeometryThroughDirectGifDiagnostics() {
            string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
            string source = File.ReadAllText(sourcePath);

            Assert.Contains("EnableTexturedVuFastPathDiagnostics = false", source, StringComparison.Ordinal);
            Assert.Contains("EnableTexturedVuFastPathDiagnostics && CanUseTexturedVuFastPath", source, StringComparison.Ordinal);
            Assert.Contains("EnableVuDirectGifDispatchDiagnostics = true", source, StringComparison.Ordinal);
            Assert.Contains("EnableRenderFrameBoundaryDiagnostics = true", source, StringComparison.Ordinal);
            Assert.Contains("stage=CpuTexturedBeforeVifWait", source, StringComparison.Ordinal);
            Assert.Contains("stage=CpuTexturedAfterGifReadyWait", source, StringComparison.Ordinal);
            Assert.Contains("stage=CpuTexturedAfterGifWait", source, StringComparison.Ordinal);
            Assert.Contains("stage=CpuTexturedBeforePacketEnd", source, StringComparison.Ordinal);
            Assert.Contains("stage=CpuTexturedAfterPacketEnd", source, StringComparison.Ordinal);
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
