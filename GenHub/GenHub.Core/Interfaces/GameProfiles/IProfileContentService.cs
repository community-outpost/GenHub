using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.GameProfiles;

/// <summary>
/// Service for managing content-to-profile operations including adding content,
/// detecting conflicts, and creating profiles with pre-enabled content.
/// </summary>
public interface IProfileContentService
{
    /// <summary>
    /// Adds content to a profile by manifest ID, resolving dependencies and handling conflicts.
    /// </summary>
    /// <param name="profileId">The profile ID to add content to.</param>
    /// <param name="manifestId">The manifest ID of the content to add.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An operation result indicating success with details about any swapped content.</returns>
    Task<AddToProfileResult> AddContentToProfileAsync(
        string profileId,
        string manifestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks for content conflicts without making changes.
    /// </summary>
    /// <param name="profileId">The profile ID to check against.</param>
    /// <param name="manifestId">The manifest ID of the content to check.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Information about any conflicts that would occur.</returns>
    Task<ContentConflictInfo> CheckContentConflictsAsync(
        string profileId,
        string manifestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new basic profile with the specified content pre-enabled.
    /// </summary>
    /// <param name="profileName">Name for the new profile.</param>
    /// <param name="manifestId">The manifest ID of the content to enable.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An operation result containing the created profile.</returns>
    Task<ProfileOperationResult<GameProfile>> CreateProfileWithContentAsync(
        string profileName,
        string manifestId,
        CancellationToken cancellationToken = default);
}