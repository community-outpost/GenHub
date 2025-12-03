using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.UserData;

namespace GenHub.Core.Interfaces.UserData;

/// <summary>
/// Service for tracking and managing user data files (maps, replays, etc.)
/// that are installed to the user's Documents folder.
/// </summary>
public interface IUserDataTracker
{
    /// <summary>
    /// Installs content files to user data directories and tracks the installation.
    /// Uses hard links when possible for efficient disk usage.
    /// </summary>
    /// <param name="manifestId">The content manifest ID.</param>
    /// <param name="profileId">The game profile ID.</param>
    /// <param name="targetGame">The target game type.</param>
    /// <param name="files">The manifest files to install (only non-workspace targets will be processed).</param>
    /// <param name="manifestVersion">The version of the manifest being installed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created UserDataManifest tracking the installation.</returns>
    Task<OperationResult<UserDataManifest>> InstallUserDataAsync(
        string manifestId,
        string profileId,
        GameType targetGame,
        IEnumerable<ManifestFile> files,
        string manifestVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uninstalls (removes) user data files for a specific manifest and profile.
    /// Restores any backed-up files that were overwritten.
    /// </summary>
    /// <param name="manifestId">The content manifest ID.</param>
    /// <param name="profileId">The game profile ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if uninstallation was successful.</returns>
    Task<OperationResult<bool>> UninstallUserDataAsync(
        string manifestId,
        string profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates user data for a profile, creating hard links to CAS content.
    /// Called when a profile is selected/launched.
    /// </summary>
    /// <param name="profileId">The game profile ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if activation was successful.</returns>
    Task<OperationResult<bool>> ActivateProfileUserDataAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates user data for a profile, removing hard links but preserving tracking.
    /// Called when switching away from a profile.
    /// </summary>
    /// <param name="profileId">The game profile ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deactivation was successful.</returns>
    Task<OperationResult<bool>> DeactivateProfileUserDataAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all user data manifests for a specific profile.
    /// </summary>
    /// <param name="profileId">The game profile ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of user data manifests for the profile.</returns>
    Task<OperationResult<IReadOnlyList<UserDataManifest>>> GetProfileUserDataAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all user data manifests for a specific game type.
    /// </summary>
    /// <param name="targetGame">The target game type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of user data manifests for the game.</returns>
    Task<OperationResult<IReadOnlyList<UserDataManifest>>> GetGameUserDataAsync(
        GameType targetGame,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific user data manifest by manifest and profile ID.
    /// </summary>
    /// <param name="manifestId">The content manifest ID.</param>
    /// <param name="profileId">The game profile ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user data manifest, or null if not found.</returns>
    Task<OperationResult<UserDataManifest?>> GetUserDataManifestAsync(
        string manifestId,
        string profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies that all tracked files still exist and haven't been modified.
    /// </summary>
    /// <param name="manifestId">The content manifest ID.</param>
    /// <param name="profileId">The game profile ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if all files are valid.</returns>
    Task<OperationResult<bool>> VerifyInstallationAsync(
        string manifestId,
        string profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a file path conflicts with any existing installation.
    /// </summary>
    /// <param name="absolutePath">The absolute file path to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The installation key that owns the file, or null if no conflict.</returns>
    Task<OperationResult<string?>> CheckFileConflictAsync(
        string absolutePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up all user data for a deleted profile.
    /// </summary>
    /// <param name="profileId">The game profile ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if cleanup was successful.</returns>
    Task<OperationResult<bool>> CleanupProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics about user data storage usage.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total bytes used by tracked user data files.</returns>
    Task<OperationResult<long>> GetTotalUserDataSizeAsync(
        CancellationToken cancellationToken = default);
}
