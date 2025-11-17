using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.GameClients;

/// <summary>
/// Orchestrates client detection for game installations.
/// </summary>
public interface IGameClientDetectionOrchestrator
{
    /// <summary>
    /// Detects all game clients from all installations.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation, with a <see cref="DetectionResult{GameClient}"/> containing detected clients.</returns>
    Task<DetectionResult<GameClient>> DetectAllClientsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects game clients from pre-detected installations (no re-detection).
    /// </summary>
    /// <param name="installations">Pre-detected installations to scan for clients.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detection result with clients linked to installations.</returns>
    Task<DetectionResult<GameClient>> DetectGameClientsFromInstallationsAsync(
        IEnumerable<IGameInstallation> installations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience: return only the Items if Success, else empty.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation, with a list of detected <see cref="GameClient"/>s.</returns>
    Task<List<GameClient>> GetDetectedClientsAsync(
        CancellationToken cancellationToken = default);
}