using System;

namespace GenHub.Core.Models.Results;

/// <summary>
/// Type alias for process operations. Use OperationResult&lt;T&gt; directly in new code.
/// </summary>
/// <typeparam name="T">The type of data returned by the operation.</typeparam>
public class ProcessOperationResult<T> : OperationResult<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessOperationResult{T}"/> class.
    /// </summary>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="data">The data returned by the operation.</param>
    /// <param name="error">The error message, if any.</param>
    /// <param name="elapsed">The elapsed time.</param>
    protected ProcessOperationResult(bool success, T? data, string? error = null, TimeSpan elapsed = default)
        : base(success, data, error, elapsed)
    {
    }

    /// <summary>
    /// Creates a successful process operation result.
    /// </summary>
    /// <param name="data">The data returned by the operation.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A successful <see cref="ProcessOperationResult{T}"/>.</returns>
    public static new ProcessOperationResult<T> CreateSuccess(T data, TimeSpan elapsed = default)
        => new(true, data, null, elapsed);

    /// <summary>
    /// Creates a failed process operation result.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="ProcessOperationResult{T}"/>.</returns>
    public static new ProcessOperationResult<T> CreateFailure(string error, TimeSpan elapsed = default)
        => new(false, default, error, elapsed);
}
