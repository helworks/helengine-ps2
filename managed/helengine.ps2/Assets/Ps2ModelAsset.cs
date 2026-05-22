namespace helengine {
    /// <summary>
    /// Stores one PS2-owned cooked model payload containing both the generic mesh data required by the runtime and the PS2-specific packed VU mesh bytes.
    /// </summary>
    public class Ps2ModelAsset : Asset {
        /// <summary>
        /// Gets or sets the authored vertex positions consumed by the PS2 runtime for bounds and CPU fallback paths.
        /// </summary>
        public float3[] Positions;

        /// <summary>
        /// Gets or sets the authored vertex normals consumed by the PS2 runtime for lighting and fallback rendering paths.
        /// </summary>
        public float3[] Normals;

        /// <summary>
        /// Gets or sets the authored texture coordinates consumed by the PS2 runtime for textured rendering paths.
        /// </summary>
        public float2[] TexCoords;

        /// <summary>
        /// Gets or sets the 16-bit index buffer consumed by the PS2 runtime for CPU fallback rendering paths.
        /// </summary>
        public ushort[] Indices16;

        /// <summary>
        /// Gets or sets the qword-aligned VU packed mesh payload consumed directly by the PS2 runtime fast path.
        /// </summary>
        public byte[] PackedMeshBytes;
    }
}
