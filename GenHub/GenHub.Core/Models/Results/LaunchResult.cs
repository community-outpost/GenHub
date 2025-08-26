using System;
using GenHub.Core.Models.Launching;

namespace GenHub.Core.Models.Results;

/// <summary>
/// Result of a game launch operation.
/// </summary>
public class LaunchResult(bool success, int? processId = null, string? errorMessage = null, Exception? exception = null, TimeSpan launchDuration = default, DateTime startTime = default)
{
    /// <summary>
    /// Gets a value indicating whether the launch was successful.
    /// </summary>
    public bool Success { get; } = success;

    /// <summary>
    /// Gets the process ID if successful.
    /// </summary>
    public int? ProcessId { get; } = processId;

    /// <summary>
    /// Gets the error message if unsuccessful.
    /// </summary>
    public string? ErrorMessage { get; } = errorMessage;

    /// <summary>
    /// Gets the exception if one occurred.
    /// </summary>
    public Exception? Exception { get; } = exception;

    /// <summary>
    /// Gets the launch duration.
    /// </summary>
    public TimeSpan LaunchDuration { get; } = launchDuration;

    /// <summary>
    /// Gets the start time.
    /// </summary>
    public DateTime StartTime { get; } = startTime;

    /// <summary>
    /// Creates a failed <see cref="LaunchResult"/>.
    /// </summary>
    /// <param name="errorMessage">The error message describing the failure.</param>
    /// <param name="exception">Optional exception captured during the launch attempt.</param>
    /// <returns>A <see cref="LaunchResult"/> representing the failed launch.</returns>
    public static LaunchResult CreateFailure(string errorMessage, Exception? exception = null)
    {
        return new LaunchResult(false, null, errorMessage, exception, default, DateTime.UtcNow);
    }

    /// <summary>
    /// Creates a successful <see cref="LaunchResult"/>.
    /// </summary>
    /// <param name="processId">The launched process id.</param>
    /// <param name="startTime">The process start time.</param>
    /// <param name="launchDuration">The duration it took to launch.</param>
    /// <returns>A <see cref="LaunchResult"/> representing a successful launch.</returns>
    public static LaunchResult CreateSuccess(int processId, DateTime startTime, TimeSpan launchDuration)
    {
        return new LaunchResult(true, processId, null, null, launchDuration, startTime);
    }
}
