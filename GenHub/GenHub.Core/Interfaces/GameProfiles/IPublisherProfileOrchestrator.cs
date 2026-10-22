using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.GameProfiles;

/// <summary>
/// Orchestrates profile creation for publisher-based game clients (GeneralsOnline, SuperHackers, etc.).
/// Handles manifest pool queries, content acquisition, and multi-variant profile creation.
/// </summary>
public interface IPublisherProfileOrchestrator
{
    /// <summary>
    /// Attempts to create profiles for all variants of a publisher-based game client.
    /// Checks the manifest pool, triggers acquisition if needed, and creates profiles using IGameClientProfileService.
    /// On macOS and Linux a detected native client gets its profile directly and nothing is acquired.
    /// </summary>
    /// <param name="installation">The parent game installation.</param>
    /// <param name="gameClient">The detected publisher game client.</param>
    /// <param name="forceReacquireContent">True to bypass cache and re-acquire content from the provider.</param>
    /// <param name="skipAcquisition">True to skip downloading/acquiring content. Profiles come from existing pool manifests, or from the detected client when the pool has none and its executable is on disk.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the number of profiles created.</returns>
    Task<OperationResult<int>> CreateProfilesForPublisherClientAsync(
        GameInstallation installation,
        GameClient gameClient,
        bool forceReacquireContent = false,
        bool skipAcquisition = false,
        CancellationToken cancellationToken = default);
}
