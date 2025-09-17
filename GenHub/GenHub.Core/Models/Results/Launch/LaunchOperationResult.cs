using System;

namespace GenHub.Core.Models.Results;

/// <summary>
/// Operation result specialized for launch operations with launch and profile context.
/// </summary>
/// <typeparam name="T">The type of data returned by the operation.</typeparam>
public class LaunchOperationResult<T> : OperationResult<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LaunchOperationResult{T}"/> class.
    /// </summary>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="data">The data returned by the operation.</param>
    /// <param name="profileId">The profile ID associated with the operation.</param>
    /// <param name="launchId">The launch ID associated with the operation.</param>
    /// <param name="error">The error message, if any.</param>
    /// <param name="elapsed">The elapsed time.</param>
    protected LaunchOperationResult(
        bool success,
        T? data,
        string? error = null,
        string? launchId = null,
        string? profileId = null,
        TimeSpan elapsed = default)
        : base(success, data, error != null ? new[] { error } : null, elapsed)
    {
        LaunchId = launchId;
        ProfileId = profileId;
    }

    /// <summary>Gets the launch ID associated with this operation.</summary>
    public string? LaunchId { get; }

    /// <summary>Gets the profile ID associated with this operation.</summary>
    public string? ProfileId { get; }

    /// <summary>
    /// Creates a successful launch operation result.
    /// </summary>
    /// <param name="data">The data returned by the operation.</param>
    /// <param name="launchId">The launch ID associated with the operation.</param>
    /// <param name="profileId">The profile ID associated with the operation.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A successful <see cref="LaunchOperationResult{T}"/>.</returns>
    public static LaunchOperationResult<T> CreateSuccess(T data, string? launchId = null, string? profileId = null, TimeSpan elapsed = default)
        => new(true, data, null, launchId, profileId, elapsed);

    /// <summary>
    /// Creates a failed launch operation result.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <param name="launchId">The launch ID associated with the operation.</param>
    /// <param name="profileId">The profile ID associated with the operation.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="LaunchOperationResult{T}"/>.</returns>
    public static LaunchOperationResult<T> CreateFailure(string error, string? launchId = null, string? profileId = null, TimeSpan elapsed = default)
        => new(false, default, error, launchId, profileId, elapsed);
}
