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

    /// <summary>
    /// Builds sidecar arguments for the given staged configuration path and optional overlay IP.
    /// </summary>
    /// <param name="configPath">The staged configuration file path.</param>
    /// <param name="overlayIp">Optional overlay IP address override.</param>
    /// <returns>The process arguments.</returns>
    string BuildArguments(string configPath, string? overlayIp) => BuildArguments(configPath);
}
