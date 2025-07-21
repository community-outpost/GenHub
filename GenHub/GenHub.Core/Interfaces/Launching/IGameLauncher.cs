using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Launching;

/// <summary>
/// Defines the game launching service.
/// </summary>
public interface IGameLauncher
{
    /// <summary>
    /// Launches a game with the specified configuration.
    /// </summary>
    /// <param name="configuration">The configuration for launching the game.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The result of the game launch operation.</returns>
    Task<LaunchResult> LaunchGameAsync(GameLaunchConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about a running game process.
    /// </summary>
    /// <param name="processId">The process ID of the running game.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The game process information, or <c>null</c> if not found.</returns>
    Task<GameProcessInfo?> GetGameProcessInfoAsync(int processId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminates a running game process.
    /// </summary>
    /// <param name="processId">The process ID of the game to terminate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><c>true</c> if the process was terminated; otherwise, <c>false</c>.</returns>
    Task<bool> TerminateGameAsync(int processId, CancellationToken cancellationToken = default);
}
