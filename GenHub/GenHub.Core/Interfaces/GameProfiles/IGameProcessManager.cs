using GenHub.Core.Models.Events;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.GameProfiles;

/// <summary>
/// Manages game processes and their lifecycle.
/// </summary>
public interface IGameProcessManager
{
    /// <summary>
    /// Occurs when a managed game process exits.
    /// </summary>
    event EventHandler<GameProcessExitedEventArgs>? ProcessExited;

    /// <summary>
    /// Starts a new game process with the specified configuration.
    /// </summary>
    /// <param name="configuration">The launch configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A process operation result containing the process information.</returns>
    Task<OperationResult<GameProcessInfo>> StartProcessAsync(GameLaunchConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminates a game process by its process ID.
    /// </summary>
    /// <param name="processId">The process ID to terminate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A process operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> TerminateProcessAsync(int processId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about a specific process by its ID.
    /// </summary>
    /// <param name="processId">The process ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A process operation result containing the process information.</returns>
    Task<OperationResult<GameProcessInfo>> GetProcessInfoAsync(int processId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active game processes managed by this instance.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A process operation result containing the list of active processes.</returns>
    Task<OperationResult<IReadOnlyList<GameProcessInfo>>> GetActiveProcessesAsync(CancellationToken cancellationToken = default);
}
