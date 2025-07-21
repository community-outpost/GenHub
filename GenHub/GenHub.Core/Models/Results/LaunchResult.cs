using System;
using GenHub.Core.Models.Launching;

namespace GenHub.Core.Models.Results;

/// <summary>
/// Result of a game launch operation.
/// </summary>
public class LaunchResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the launch was successful.
    /// </summary>
    required public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the process ID if successful.
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// Gets or sets the error message if unsuccessful.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the exception if one occurred.
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Gets or sets the launch duration.
    /// </summary>
    public TimeSpan LaunchDuration { get; set; }

    /// <summary>
    /// Gets or sets the start time.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Creates a successful launch result.
    /// </summary>
    /// <param name="processId">The process ID of the launched game.</param>
    /// <param name="startTime">The start time of the process.</param>
    /// <param name="duration">The duration of the launch.</param>
    /// <returns>A successful <see cref="LaunchResult"/> instance.</returns>
    public static LaunchResult CreateSuccess(int processId, DateTime startTime, TimeSpan duration)
    {
        return new LaunchResult
        {
            Success = true,
            ProcessId = processId,
            StartTime = startTime,
            LaunchDuration = duration,
        };
    }

    /// <summary>
    /// Creates a failed launch result.
    /// </summary>
    /// <param name="errorMessage">The error message describing the failure.</param>
    /// <param name="exception">The exception that occurred, if any.</param>
    /// <returns>A failed <see cref="LaunchResult"/> instance.</returns>
    public static LaunchResult CreateFailure(string errorMessage, Exception? exception = null)
    {
        return new LaunchResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            Exception = exception,
            StartTime = DateTime.UtcNow,
        };
    }
}
