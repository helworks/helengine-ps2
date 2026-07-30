namespace helengine.ps2.builder.tests {
    /// <summary>
    /// Verifies that fast textured VU submissions are split before their packet2 allocation can overflow.
    /// </summary>
    public sealed class Ps2TexturedVuPacketCapacitySourceTests {
        /// <summary>
        /// Ensures the fast textured VU path reuses the established bounded aggregate packet partitioning instead of encoding every visible slice into one packet.
        /// </summary>
        [Fact]
        public void Ps2RenderManager3D_WhenFastTexturedVuSlicesExceedPacketCapacity_SplitsThemIntoBoundedPackets() {
            string repositoryRootPath = GetRepositoryRootPath();
            string sourcePath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
            string source = File.ReadAllText(sourcePath);

            Assert.Contains("ResolveBoundedTexturedAggregatePacketEnd(\n                    texturedVuBatches,\n                    firstTexturedVuBatchIndex)", source, StringComparison.Ordinal);
            Assert.Contains("std::vector<Ps2VuOpaqueBatchSlice> packetTexturedVuBatches(", source, StringComparison.Ordinal);
            Assert.Contains("for (const Ps2VuOpaqueBatchSlice& texturedVuBatch : packetTexturedVuBatches)", source, StringComparison.Ordinal);
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
