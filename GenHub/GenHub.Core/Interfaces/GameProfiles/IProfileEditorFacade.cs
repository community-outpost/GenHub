using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.GameProfiles;

/// <summary>
/// Provides a facade for game profile editing operations, simplifying complex interactions
/// between profile management, content orchestration, and workspace preparation.
/// </summary>
public interface IProfileEditorFacade
{
    /// <summary>
    /// Creates a new game profile with automatic content discovery and workspace setup.
    /// </summary>
    /// <param name="request">The profile creation request.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing the created profile with workspace information.</returns>
    Task<ProfileOperationResult<GameProfile>> CreateProfileWithWorkspaceAsync(CreateProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing profile and refreshes its workspace if needed.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to update.</param>
    /// <param name="request">The profile update request.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing the updated profile.</returns>
    Task<ProfileOperationResult<GameProfile>> UpdateProfileWithWorkspaceAsync(string profileId, UpdateProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a profile with its current workspace status and available content.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing the profile with workspace and content information.</returns>
    Task<ProfileOperationResult<GameProfile>> GetProfileWithWorkspaceAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers and suggests content for a given game version.
    /// </summary>
    /// <param name="gameVersionId">The game version identifier.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing suggested content manifests.</returns>
    Task<ProfileOperationResult<IReadOnlyList<ContentManifest>>> DiscoverContentForVersionAsync(string gameVersionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a profile configuration before saving.
    /// </summary>
    /// <param name="profile">The profile to validate.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result indicating validation success or failure with details.</returns>
    Task<ProfileOperationResult<bool>> ValidateProfileAsync(GameProfile profile, CancellationToken cancellationToken = default);
}
