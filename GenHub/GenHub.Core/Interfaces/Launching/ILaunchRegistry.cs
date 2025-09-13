using GenHub.Core.Models.GameProfile;

namespace GenHub.Core.Interfaces.Launching;

/// <summary>
/// Provides methods for registering, retrieving, and unregistering game launches.
/// </summary>
public interface ILaunchRegistry
{
    /// <summary>
    /// Registers a new game launch.
    /// </summary>
    /// <param name="launchInfo">The launch information to register.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RegisterLaunchAsync(GameLaunchInfo launchInfo);

    /// <summary>
    /// Unregisters a game launch by its launch ID.
    /// </summary>
    /// <param name="launchId">The launch ID to unregister.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UnregisterLaunchAsync(string launchId);

    /// <summary>
    /// Gets launch information by launch ID.
    /// </summary>
    /// <param name="launchId">The launch ID to look up.</param>
    /// <returns>A task that returns the <see cref="GameLaunchInfo"/> if found; otherwise, null.</returns>
    Task<GameLaunchInfo?> GetLaunchInfoAsync(string launchId);

    /// <summary>
    /// Gets all active launches.
    /// </summary>
    /// <returns>A collection of all active launch information.</returns>
    Task<IEnumerable<GameLaunchInfo>> GetAllActiveLaunchesAsync();
}
