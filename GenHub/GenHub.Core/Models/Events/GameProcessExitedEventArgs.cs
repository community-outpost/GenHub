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

    /// <summary>
    /// Gets the bounded tail of the process's captured standard error, when any was captured.
    /// </summary>
    /// <remarks>
    /// Populated only for processes whose stderr the process manager was capturing, i.e.
    /// ones it started itself. An initialisation abort slow enough to escape the
    /// post-spawn detection window surfaces here, so subscribers can record why a launch
    /// that was reported as started actually failed.
    /// </remarks>
    public string? StandardErrorTail { get; init; }

    /// <summary>
    /// Gets the archives named by the engine's mount-failure stderr sentinels, if any.
    /// </summary>
    /// <remarks>
    /// Advisory: the sentinels are emitted only by the fork engine, so an empty list says
    /// nothing about whether archives mounted.
    /// </remarks>
    public IReadOnlyList<string> UnmountableArchives { get; init; } = [];
}