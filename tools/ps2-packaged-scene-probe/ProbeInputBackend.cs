namespace helengine.ps2.packagedsceneprobe {
    /// <summary>
    /// Supplies an empty input frame for packaged scene probe sessions.
    /// </summary>
    public sealed class ProbeInputBackend : IInputBackend {
        /// <summary>
        /// Gets or sets whether the probe backend should report input while unfocused.
        /// </summary>
        public bool ReceiveInputInBackground { get; set; }

        /// <summary>
        /// Captures one empty input frame for the probe session.
        /// </summary>
        /// <returns>Empty input frame state.</returns>
        public InputFrameState CaptureFrame() {
            return new InputFrameState();
        }
    }
}
