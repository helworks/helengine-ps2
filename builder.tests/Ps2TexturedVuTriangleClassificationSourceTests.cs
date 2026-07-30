namespace helengine.ps2.builder.tests {
    /// <summary>
    /// Verifies that clipped textured geometry retains VU submission for safe triangle runs.
    /// </summary>
    public sealed class Ps2TexturedVuTriangleClassificationSourceTests {
        /// <summary>
        /// Ensures a texture batch is split into fully visible VU runs, discarded invisible runs, and CPU-clipped boundary runs.
        /// </summary>
        [Fact]
        public void Ps2RenderManager3D_WhenTexturedSliceTouchesFrustum_ClassifiesIndividualTriangles() {
            string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
            string source = File.ReadAllText(sourcePath);

            Assert.Contains("ClassifyTexturedSourceTriangle", source, StringComparison.Ordinal);
            Assert.Contains("ResolveTexturedPackedTriangleSources", source, StringComparison.Ordinal);
            Assert.Contains("TexturedTriangleFrustumClassificationVuSafe", source, StringComparison.Ordinal);
            string packetBuilderPath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
            string packetBuilderSource = File.ReadAllText(packetBuilderPath);
            Assert.Contains("cachedSourceTriangleCount", packetBuilderSource, StringComparison.Ordinal);
            Assert.Contains("MaximumTexturedVuBatchCountPerPacket", source, StringComparison.Ordinal);
            Assert.Contains("const float signedArea", source, StringComparison.Ordinal);
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
