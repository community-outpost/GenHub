using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.UserData;

namespace GenHub.Core.Interfaces.UserData;

/// <summary>
/// Service for managing user data content (maps, replays, etc.) when switching between profiles.
/// Handles the lifecycle of content linking based on profile activation.
/// </summary>
public interface IProfileContentLinker
{
    /// <summary>
    /// Prepares user data content for a profile before launch.
    /// Installs any required user data files (maps, etc.) and activates them.
    /// </summary>
    /// <param name="profileId">The profile ID being launched.</param>
    /// <param name="manifests">The content manifests for the profile (includes maps, etc.).</param>
    /// <param name="targetGame">The target game type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success if all user data is ready.</returns>
    Task<OperationResult<bool>> PrepareProfileUserDataAsync(
        string profileId,
        IEnumerable<ContentManifest> manifests,
        GameType targetGame,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches user data content from one profile to another.
    /// Deactivates the old profile's content and activates the new profile's content.
    /// The <paramref name="newManifests"/> collection represents the complete set of content that should be active for the new profile.
    /// </summary>
    /// <param name="oldProfileId">The profile being switched away from (null if first launch).</param>
    /// <param name="newProfileId">The profile being switched to.</param>
    /// <param name="newManifests">The content manifests defining the desired state for the new profile.</param>
    /// <param name="targetGame">The target game type.</param>
    /// <param name="skipCleanup">Whether to skip cleanup of files from the old profile (user maps, etc.) and instead add them to the new profile.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success if switch completed.</returns>
    Task<OperationResult<bool>> SwitchProfileUserDataAsync(
        string? oldProfileId,
        string newProfileId,
        IEnumerable<ContentManifest> newManifests,
        GameType targetGame,
        bool skipCleanup = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up all user data (installed files, hard links) associated with a profile.
    /// This should be called after the profile entity itself has been deleted.
    /// This method does NOT delete the profile configuration/entity itself.
    /// </summary>
    /// <param name="profileId">The ID of the deleted profile.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success if cleanup completed.</returns>
    Task<OperationResult<bool>> CleanupDeletedProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates user data for a profile when its content selection changes.
    /// Adds new content and removes deselected content.
    /// </summary>
    /// <param name="profileId">The profile being updated.</param>
    /// <param name="newManifests">The new set of content manifests.</param>
    /// <param name="targetGame">The target game type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success if update completed.</returns>
    Task<OperationResult<bool>> UpdateProfileUserDataAsync(
        string profileId,
        IEnumerable<ContentManifest> newManifests,
        GameType targetGame,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the currently active profile ID (if any).
    /// </summary>
    /// <returns>The active profile ID, or null if no profile is active.</returns>
    string? GetActiveProfileId();

    /// <summary>
    /// Checks if a profile has its user data currently active.
    /// </summary>
    /// <param name="profileId">The profile ID to check.</param>
    /// <returns>True if the profile's user data is active.</returns>
    bool IsProfileActive(string profileId);
}
