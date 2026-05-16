namespace helengine.ps2.packagedsceneprobe {
    /// <summary>
    /// Rebuilds lightweight runtime model and material instances for packaged scene probing without issuing draw calls.
    /// </summary>
    public sealed class ProbeRenderManager3D : RenderManager3D {
        /// <summary>
        /// Builds one lightweight runtime model from the supplied raw asset.
        /// </summary>
        /// <param name="data">Raw model asset requested by the runtime scene loader.</param>
        /// <returns>Lightweight runtime model populated with bounds and runtime submeshes.</returns>
        public override RuntimeModel BuildModelFromRaw(ModelAsset data) {
            if (data == null) {
                throw new ArgumentNullException(nameof(data));
            }

            ProbeRuntimeModel runtimeModel = new ProbeRuntimeModel();
            runtimeModel.SetBounds(data.BoundsMin, data.BoundsMax);
            runtimeModel.SetSubmeshes(ModelSubmeshResolver.BuildRuntimeSubmeshes(data));
            return runtimeModel;
        }

        /// <summary>
        /// Builds one lightweight runtime material from the supplied raw asset data.
        /// </summary>
        /// <param name="materialAsset">Raw material asset requested by the runtime scene loader.</param>
        /// <param name="shaderAsset">Shader asset associated with the material.</param>
        /// <returns>Lightweight runtime material instance.</returns>
        public override RuntimeMaterial BuildMaterialFromRaw(MaterialAsset materialAsset, ShaderAsset shaderAsset) {
            if (materialAsset == null) {
                throw new ArgumentNullException(nameof(materialAsset));
            }
            if (shaderAsset == null) {
                throw new ArgumentNullException(nameof(shaderAsset));
            }

            return new RuntimeMaterial();
        }

#if HELENGINE_RUNTIME_MATERIAL_RESOLUTION_COOKED_PLATFORM_OWNED
        /// <summary>
        /// Builds one lightweight runtime material from the supplied platform-owned cooked material asset.
        /// </summary>
        /// <param name="materialAsset">Cooked material asset requested by the runtime scene loader.</param>
        /// <returns>Lightweight runtime material instance.</returns>
        public override RuntimeMaterial BuildMaterialFromCooked(PlatformMaterialAsset materialAsset) {
            if (materialAsset == null) {
                throw new ArgumentNullException(nameof(materialAsset));
            }

            return new RuntimeMaterial();
        }
#endif
    }
}
