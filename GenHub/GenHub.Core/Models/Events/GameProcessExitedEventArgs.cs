using GenHub.Core.Constants;

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

    /// <summary>
    /// Gets a value indicating whether this exit was requested through the process
    /// manager's terminate path before the kill was attempted.
    /// </summary>
    /// <remarks>
    /// A killed process exits non-zero, which is otherwise the signature of a crash;
    /// this flag is what lets consumers tell a deliberate stop apart from one.
    /// </remarks>
    public bool TerminationRequested { get; init; }

    /// <summary>
    /// Describes why this exit is a failure, or returns null for a clean or unknown exit.
    /// </summary>
    /// <remarks>
    /// The single source of the late-failure wording: the launch registry records it and
    /// the UI surfaces it, so composing it here keeps the two from drifting apart. The
    /// advisory mount sentinels, when present, name the archive; otherwise the stderr
    /// tail stands in. Only the non-zero exit code decides that the exit counts as a
    /// failure — quitting the game cleanly is not one.
    /// </remarks>
    /// <returns>The failure description, or null when the exit is not a failure.</returns>
    public string? DescribeFailure()
    {
        // A requested termination is never a failure, even though the kill produces a
        // non-zero exit code. Trade-off, accepted deliberately: an engine that genuinely
        // crashed moments before the user clicked Stop is suppressed too — a missed
        // report of an already-dying process is preferred over false-alarming "exited
        // unexpectedly" on every deliberate stop.
        if (TerminationRequested)
        {
            return null;
        }

        if (ExitCode is not int exitCode || exitCode == ProcessConstants.ExitCodeSuccess)
        {
            return null;
        }

        if (UnmountableArchives.Count > 0)
        {
            return $"The game could not mount required archive(s): {string.Join(", ", UnmountableArchives)}. Process exited with code {exitCode} after launch.";
        }

        return StandardErrorTail is null
            ? $"Process exited with code {exitCode} after launch. No output was captured."
            : $"Process exited with code {exitCode} after launch. {StandardErrorTail}";
    }
}