using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.GameProfiles;

/// <summary>
/// Provides services for managing game profiles including creation, updates, and content management.
/// </summary>
public interface IGameProfileManager
{
    /// <summary>
    /// Creates a new game profile from the specified request.
    /// </summary>
    /// <param name="request">The profile creation request.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing the created profile.</returns>
    Task<ProfileOperationResult<GameProfile>> CreateProfileAsync(CreateProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing game profile with the specified changes.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to update.</param>
    /// <param name="request">The profile update request.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing the updated profile.</returns>
    Task<ProfileOperationResult<GameProfile>> UpdateProfileAsync(string profileId, UpdateProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the specified game profile.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to delete.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result indicating success or failure.</returns>
    Task<ProfileOperationResult<GameProfile>> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all game profiles.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing all profiles.</returns>
    Task<ProfileOperationResult<IReadOnlyList<GameProfile>>> GetAllProfilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific game profile by its identifier.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing the requested profile.</returns>
    Task<ProfileOperationResult<GameProfile>> GetProfileAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available content manifests for the specified game client.
    /// </summary>
    /// <param name="gameClient">The game client to get content for.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing available content manifests.</returns>
    Task<ProfileOperationResult<IReadOnlyList<ContentManifest>>> GetAvailableContentAsync(GameClient gameClient, CancellationToken cancellationToken = default);
}
