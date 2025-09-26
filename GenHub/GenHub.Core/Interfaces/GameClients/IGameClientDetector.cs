using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.GameClients;

/// <summary>
/// From one or more GameInstallation(s), finds all runnable executables/patches.
/// </summary>
public interface IGameClientDetector
{
    /// <summary>
    /// Given a set of base installations, produce all GameClient variants.
    /// </summary>
    /// <param name="installations">The set of game installations.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation, with a <see cref="DetectionResult{GameClient}"/> containing detected clients.</returns>
    Task<DetectionResult<GameClient>> DetectClientsFromInstallationsAsync(
       IEnumerable<GameInstallation> installations,
       CancellationToken cancellationToken = default);

    /// <summary>
    /// Scan an arbitrary directory (e.g. a GitHub‐extracted folder) for executables.
    /// </summary>
    /// <param name="path">The directory path to scan.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation, with a <see cref="DetectionResult{GameClient}"/> containing detected clients.</returns>
    Task<DetectionResult<GameClient>> ScanDirectoryForClientsAsync(
       string path,
       CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate a single GameClient (e.g. does the EXE exist?).
    /// </summary>
    /// <param name="client">The game client to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation, with a boolean indicating validity.</returns>
    Task<bool> ValidateClientAsync(
       GameClient client,
       CancellationToken cancellationToken = default);
}
