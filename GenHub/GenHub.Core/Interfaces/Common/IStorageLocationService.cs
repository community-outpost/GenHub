using GenHub.Core.Interfaces.GameInstallations;

namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Service for resolving dynamic storage locations based on game installations.
/// </summary>
public interface IStorageLocationService
{
    /// <summary>
    /// Gets the CAS pool path adjacent to the specified game installation.
    /// </summary>
    /// <param name="installation">The game installation to base the path on.</param>
    /// <returns>The absolute path to the CAS pool directory.</returns>
    string GetCasPoolPath(IGameInstallation installation);

    /// <summary>
    /// Gets the workspace path adjacent to the specified game installation.
    /// </summary>
    /// <param name="installation">The game installation to base the path on.</param>
    /// <returns>The absolute path to the workspace directory.</returns>
    string GetWorkspacePath(IGameInstallation installation);

    /// <summary>
    /// Gets the user's preferred game installation for storage location.
    /// Returns null if no preference is set or if the preferred installation is no longer available.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The preferred installation, or null if not set or unavailable.</returns>
    Task<IGameInstallation?> GetPreferredInstallationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the user's preferred game installation for storage location.
    /// </summary>
    /// <param name="installationId">The installation ID to set as preferred.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetPreferredInstallationAsync(string installationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines if user selection is required because installations span multiple drives.
    /// </summary>
    /// <param name="installations">The available game installations.</param>
    /// <returns>True if installations are on different drives and user selection is needed.</returns>
    bool RequiresUserSelection(IEnumerable<IGameInstallation> installations);

    /// <summary>
    /// Checks if two paths are on the same volume/drive.
    /// </summary>
    /// <param name="path1">First path to compare.</param>
    /// <param name="path2">Second path to compare.</param>
    /// <returns>True if both paths are on the same volume.</returns>
    bool AreSameVolume(string path1, string path2);
}
