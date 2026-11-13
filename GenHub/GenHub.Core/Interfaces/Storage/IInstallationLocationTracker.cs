namespace GenHub.Core.Interfaces.Storage;

/// <summary>
/// Tracks platform-specific installation markers and detects duplicate or conflicting installations.
/// </summary>
public interface IInstallationLocationTracker
{
    /// <summary>
    /// Records the current installation directory when running from a custom install root.
    /// </summary>
    void RecordInstallLocation();

    /// <summary>
    /// Retrieves the registered custom installation directory, if any.
    /// </summary>
    /// <returns>The registered custom installation path, or <see langword="null"/> if none.</returns>
    string? GetRegisteredCustomInstallPath();

    /// <summary>
    /// Clears the registered custom installation path (e.g. after migration).
    /// </summary>
    void ClearCustomInstallPath();
}
