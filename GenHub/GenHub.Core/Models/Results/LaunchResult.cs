using System;
using GenHub.Core.Models.Launching;

namespace GenHub.Core.Models.Results;

/// <summary>
/// Result of a game launch operation.
/// </summary>
public class LaunchResult : ResultBase
{
    private LaunchResult(
        bool success,
        int? processId = null,
        string? errorMessage = null,
        Exception? exception = null,
        DateTime? startTime = null,
        TimeSpan elapsed = default)
        : base(success, errorMessage, elapsed)
    {
        ProcessId = processId;
        Exception = exception;
        StartTime = startTime ?? DateTime.UtcNow;
    }

    /// <summary>Gets the process ID if successful.</summary>
    public int? ProcessId { get; }

    /// <summary>Gets the exception if one occurred.</summary>
    public Exception? Exception { get; }

    /// <summary>Gets the start time.</summary>
    public DateTime StartTime { get; }

    /// <summary>Gets the launch duration (alias for Elapsed).</summary>
    public TimeSpan LaunchDuration => Elapsed;

    // Factory Methods

    /// <summary>
    /// Creates a successful launch result.
    /// </summary>
    /// <param name="processId">The process ID of the launched game.</param>
    /// <param name="startTime">The start time of the process.</param>
    /// <param name="duration">The duration of the launch.</param>
    /// <returns>A successful <see cref="LaunchResult"/> instance.</returns>
    public static LaunchResult CreateSuccess(int processId, DateTime startTime, TimeSpan duration)
        => new(true, processId, null, null, startTime, duration);

    /// <summary>
    /// Creates a failed launch result.
    /// </summary>
    /// <param name="errorMessage">The error message describing the failure.</param>
    /// <param name="exception">The exception that occurred, if any.</param>
    /// <returns>A failed <see cref="LaunchResult"/> instance.</returns>
    public static LaunchResult CreateFailure(string errorMessage, Exception? exception = null)
        => new(false, null, errorMessage, exception);
}
