namespace GenHub.Core.Interfaces.Online;

/// <summary>
/// Locates the overlay sidecar binary and builds its arguments. Implemented
/// per platform; returns null until the Phase 0 overlay selection lands.
/// </summary>
public interface IOverlaySidecarLocator
{
    /// <summary>
    /// Locates the sidecar binary, honoring the override environment variable.
    /// </summary>
    /// <returns>The binary path, or null when no sidecar is installed.</returns>
    string? LocateBinary();

    /// <summary>
    /// Builds sidecar arguments for the given staged configuration path.
    /// </summary>
    /// <param name="configPath">The staged configuration file path.</param>
    /// <returns>The process arguments.</returns>
    string BuildArguments(string configPath);
}
