using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.GameProfiles;

/// <summary>
/// Provides data access operations for game profiles.
/// </summary>
public interface IGameProfileRepository
{
    /// <summary>
    /// Saves a game profile to the repository.
    /// </summary>
    /// <param name="profile">The profile to save.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing the saved profile.</returns>
    Task<ProfileOperationResult<GameProfile>> SaveProfileAsync(GameProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a game profile from the repository by its identifier.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to load.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing the loaded profile.</returns>
    Task<ProfileOperationResult<GameProfile>> LoadProfileAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads all game profiles from the repository.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing all profiles.</returns>
    Task<ProfileOperationResult<IReadOnlyList<GameProfile>>> LoadAllProfilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a game profile from the repository.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to delete. Must not be null or empty.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing the deleted profile if successful.</returns>
    /// <exception cref="ArgumentNullException">Thrown when profileId is null.</exception>
    /// <exception cref="ArgumentException">Thrown when profileId is empty.</exception>
    Task<ProfileOperationResult<GameProfile>> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default);
}