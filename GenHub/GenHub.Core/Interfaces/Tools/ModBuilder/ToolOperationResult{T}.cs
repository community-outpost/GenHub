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
    public ToolOperationResult(bool success, T? data = default, IEnumerable<string>? errors = null, int exitCode = 0, TimeSpan elapsed = default)
        : base(success, errors, exitCode, elapsed)
    {
        Data = data;
    }

    /// <summary>
    /// Gets or sets the result data.
    /// </summary>
    public T? Data { get; init; }

    /// <summary>
    /// Creates a successful tool operation result with data.
    /// </summary>
    public static ToolOperationResult<T> CreateSuccess(T data, int exitCode = 0, TimeSpan elapsed = default) =>
        new(true, data, null, exitCode, elapsed);

    /// <summary>
    /// Creates a failed tool operation result.
    /// </summary>
    public static new ToolOperationResult<T> CreateFailure(string error, int exitCode = -1, TimeSpan elapsed = default) =>
        new(false, default, [error], exitCode, elapsed);

    /// <summary>
    /// Creates a failed tool operation result with multiple errors.
    /// </summary>
    public static new ToolOperationResult<T> CreateFailure(IEnumerable<string> errors, int exitCode = -1, TimeSpan elapsed = default) =>
        new(false, default, errors, exitCode, elapsed);
}
