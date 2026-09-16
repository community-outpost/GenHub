namespace GenHub.Core.Models.Events;

/// <summary>
/// Event arguments for when a game process exits.
/// </summary>
public class GameProcessExitedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the process ID that exited.
    /// </summary>
    public int ProcessId { get; init; }

    /// <summary>
    /// Gets the exit code of the process.
    /// </summary>
    public int? ExitCode { get; init; }

    /// <summary>
    /// Gets the time when the process exited.
    /// </summary>
    public DateTime ExitTime { get; init; } = DateTime.UtcNow;
}
