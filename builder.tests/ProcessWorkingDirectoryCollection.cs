namespace helengine.ps2.builder.tests;

/// <summary>
/// xUnit collection for tests that change the process-wide current directory (the PS2 builder resolves cooked
/// sources from it). The collection runs on its own after the parallel tests, so source-file tests that resolve
/// paths from the current directory never observe a borrowed directory.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessWorkingDirectoryCollection {
    /// <summary>
    /// Collection name referenced by test classes through <see cref="CollectionAttribute"/>.
    /// </summary>
    public const string Name = "Process working directory";
}
