namespace helengine {
    /// <summary>
    /// Stores one PS2-owned packed-mesh sidecar payload emitted alongside a generic cooked model asset.
    /// </summary>
    public class Ps2PackedModelAsset : Asset {
        /// <summary>
        /// Gets or sets the qword-aligned VU packed mesh payload consumed directly by the PS2 runtime.
        /// </summary>
        public byte[] PackedMeshBytes;
    }
}
