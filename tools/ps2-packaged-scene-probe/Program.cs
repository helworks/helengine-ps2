namespace helengine.ps2.packagedsceneprobe {
    /// <summary>
    /// Provides the command-line entry point for the packaged PS2 scene probe.
    /// </summary>
    public static class Program {
        /// <summary>
        /// Runs the packaged PS2 scene probe against the provided export root.
        /// </summary>
        /// <param name="args">Command-line arguments for the probe session.</param>
        /// <returns>Zero when the packaged scene loads successfully; otherwise one.</returns>
        public static int Main(string[] args) {
            try {
                ProbeSession session = new ProbeSession();
                return session.Run(args);
            } catch (Exception exception) {
                Console.Error.WriteLine(exception.ToString());
                return 1;
            }
        }
    }
}
