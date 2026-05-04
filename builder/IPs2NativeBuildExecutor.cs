namespace helengine.ps2.builder;

/// <summary>
/// Executes the native Docker-backed PS2 build for one prepared workspace.
/// </summary>
public interface IPs2NativeBuildExecutor {
    /// <summary>
    /// Builds one PS2 ELF for the prepared workspace.
    /// </summary>
    /// <param name="workspace">Prepared PS2 build workspace.</param>
    /// <param name="cancellationToken">Cancellation token used to stop the native build cooperatively.</param>
    void Build(Ps2BuildWorkspace workspace, CancellationToken cancellationToken);
}
