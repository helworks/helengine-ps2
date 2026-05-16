namespace helengine.ps2.packagedsceneprobe {
    /// <summary>
    /// Materializes runtime textures for packaged font deserialization without performing any raster output.
    /// </summary>
    public sealed class ProbeRenderManager2D : RenderManager2D {
        /// <summary>
        /// Builds one managed runtime texture that mirrors the supplied raw texture dimensions.
        /// </summary>
        /// <param name="data">Raw texture data requested by the packaged font loader.</param>
        /// <returns>Managed runtime texture with matching dimensions.</returns>
        public override RuntimeTexture BuildTextureFromRaw(TextureAsset data) {
            if (data == null) {
                throw new ArgumentNullException(nameof(data));
            }

            return new ManagedRuntimeTexture {
                Width = data.Width,
                Height = data.Height
            };
        }

        /// <summary>
        /// Ignores sprite draw requests because the probe only needs asset materialization.
        /// </summary>
        /// <param name="sprite">Sprite draw request issued by runtime code.</param>
        public override void DrawSprite(ISpriteDrawable2D sprite) {
        }

        /// <summary>
        /// Ignores text draw requests because the probe only needs asset materialization.
        /// </summary>
        /// <param name="text">Text draw request issued by runtime code.</param>
        public override void DrawText(ITextDrawable2D text) {
        }

        /// <summary>
        /// Ignores rounded-rectangle draw requests because the probe only needs asset materialization.
        /// </summary>
        /// <param name="shape">Rounded-rectangle draw request issued by runtime code.</param>
        public override void DrawRoundedRect(IRoundedRectDrawable2D shape) {
        }
    }
}
