using System;
using System.Collections.Generic;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Result of a tool operation with data.
/// </summary>
/// <typeparam name="T">The type of data returned by the operation.</typeparam>
public class ToolOperationResult<T> : ToolOperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolOperationResult{T}"/> class.
    /// </summary>
    public ToolOperationResult()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolOperationResult{T}"/> class.
    /// </summary>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="data">The result payload data.</param>
    /// <param name="errors">Optional list of error messages.</param>
    /// <param name="exitCode">The exit code of the tool process.</param>
    /// <param name="elapsed">The elapsed duration of the operation.</param>
    public ToolOperationResult(bool success, T? data = default, IEnumerable<string>? errors = null, int exitCode = 0, TimeSpan elapsed = default)
        : base(success, errors, exitCode, elapsed)
    {
        Data = data;
    }

    /// <summary>
    /// Gets the result data.
    /// </summary>
    public T? Data { get; init; }

    /// <summary>
    /// Creates a successful tool operation result with data.
    /// </summary>
    /// <param name="data">The operation result data.</param>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="elapsed">The elapsed execution time.</param>
    /// <returns>A new successful <see cref="ToolOperationResult{T}"/> instance with data.</returns>
    public static ToolOperationResult<T> CreateSuccess(T data, int exitCode = 0, TimeSpan elapsed = default) =>
        new(true, data, null, exitCode, elapsed);

    /// <summary>
    /// Creates a failed tool operation result.
    /// </summary>
    /// <param name="error">The failure error message.</param>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="elapsed">The elapsed execution time.</param>
    /// <returns>A new failed <see cref="ToolOperationResult{T}"/> instance.</returns>
    public static new ToolOperationResult<T> CreateFailure(string error, int exitCode = -1, TimeSpan elapsed = default) =>
        new(false, default, [error], exitCode, elapsed);

    /// <summary>
    /// Creates a failed tool operation result with multiple errors.
    /// </summary>
    /// <param name="errors">The collection of error messages.</param>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="elapsed">The elapsed execution time.</param>
    /// <returns>A new failed <see cref="ToolOperationResult{T}"/> instance.</returns>
    public static new ToolOperationResult<T> CreateFailure(IEnumerable<string> errors, int exitCode = -1, TimeSpan elapsed = default) =>
        new(false, default, errors, exitCode, elapsed);
}
