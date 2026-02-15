using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Events;

namespace GenHub.Core.Interfaces.Tools.ReplayManager;

/// <summary>
/// Service for monitoring replay files and automatically saving them.
/// </summary>
public interface IReplayMonitorService
{
    /// <summary>
    /// Event raised when a replay file has been completed and saved.
    /// </summary>
    event EventHandler<ReplayCompletedEventArgs>? ReplayCompleted;

    /// <summary>
    /// Starts monitoring for a specific profile.
    /// </summary>
    /// <param name="profileId">The profile ID.</param>
    /// <param name="gameType">The game type.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StartMonitoringAsync(string profileId, GameType gameType);

    /// <summary>
    /// Stops monitoring for a specific profile.
    /// </summary>
    /// <param name="profileId">The profile ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StopMonitoringAsync(string profileId);

    /// <summary>
    /// Stops all active monitoring.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StopAllMonitoringAsync();

    /// <summary>
    /// Stops monitoring for a specific profile, verifying session identity.
    /// </summary>
    /// <param name="profileId">The profile ID.</param>
    /// <param name="sessionId">If provided, only stops if the active session matches this ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StopMonitoringAsync(string profileId, string? sessionId);

    /// <summary>
    /// Gets a value indicating whether monitoring is active for a profile.
    /// </summary>
    /// <param name="profileId">The profile ID.</param>
    /// <returns>True if monitoring is active; otherwise, false.</returns>
    bool IsMonitoring(string profileId);

    /// <summary>
    /// Gets the session ID for an active monitoring session.
    /// </summary>
    /// <param name="profileId">The profile ID.</param>
    /// <returns>The session ID, or null if not monitoring.</returns>
    string? GetSessionId(string profileId);
}
