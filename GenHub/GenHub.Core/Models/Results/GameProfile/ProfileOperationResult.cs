namespace GenHub.Core.Models.Results;

/// <summary>
/// Represents the result of a profile operation.
/// </summary>
/// <typeparam name="T">The type of data returned by the operation.</typeparam>
public class ProfileOperationResult<T> : OperationResult<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileOperationResult{T}"/> class.
    /// </summary>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="data">The data returned by the operation.</param>
    /// <param name="error">The error message, if any.</param>
    /// <param name="errorCode">The error code, if any.</param>
    /// <param name="elapsed">The elapsed time.</param>
    protected ProfileOperationResult(bool success, T? data, string? error = null, string? errorCode = null, TimeSpan elapsed = default)
        : base(success, data, error != null ? new[] { error } : null, elapsed)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Gets the error code, if any.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets a value indicating whether changes were synchronized into an active game session.
    /// </summary>
    public bool WasAppliedLive { get; init; }

    /// <summary>
    /// Gets a value indicating whether a live content rollback was attempted after a save failure.
    /// </summary>
    public bool LiveRollbackAttempted { get; init; }

    /// <summary>
    /// Gets a value indicating whether live content rollback after a save failure succeeded.
    /// </summary>
    public bool LiveRollbackSucceeded { get; init; }

    /// <summary>
    /// Gets details on any failure during live content rollback after a save failure.
    /// </summary>
    public string? LiveRollbackError { get; init; }

    /// <summary>
    /// Creates a successful profile operation result.
    /// </summary>
    /// <param name="data">The data returned by the operation.</param>
    /// <param name="errorCode">The error code, if any.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <param name="wasAppliedLive">Whether changes were synchronized into an active game session.</param>
    /// <returns>A successful <see cref="ProfileOperationResult{T}"/>.</returns>
    public static ProfileOperationResult<T> CreateSuccess(
        T data,
        string? errorCode = null,
        TimeSpan elapsed = default,
        bool wasAppliedLive = false)
        => new(true, data, null, errorCode, elapsed)
        {
            WasAppliedLive = wasAppliedLive,
        };

    /// <summary>
    /// Creates a failed profile operation result.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <param name="errorCode">The error code, if any.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <param name="liveRollbackAttempted">Whether live content rollback was attempted.</param>
    /// <param name="liveRollbackSucceeded">Whether live content rollback succeeded.</param>
    /// <param name="liveRollbackError">Details on rollback failure, if any.</param>
    /// <returns>A failed <see cref="ProfileOperationResult{T}"/>.</returns>
    public static ProfileOperationResult<T> CreateFailure(
        string error,
        string? errorCode = null,
        TimeSpan elapsed = default,
        bool liveRollbackAttempted = false,
        bool liveRollbackSucceeded = false,
        string? liveRollbackError = null)
        => new(false, default, error, errorCode, elapsed)
        {
            LiveRollbackAttempted = liveRollbackAttempted,
            LiveRollbackSucceeded = liveRollbackSucceeded,
            LiveRollbackError = liveRollbackError,
        };
}
