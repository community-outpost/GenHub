using System;
using System.Collections.Generic;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Result of a tool operation.
/// </summary>
public class ToolOperationResult : ResultBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolOperationResult"/> class.
    /// </summary>
    public ToolOperationResult()
        : base(true, (IEnumerable<string>?)null, default)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolOperationResult"/> class.
    /// </summary>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="errors">Optional list of error messages.</param>
    /// <param name="exitCode">The exit code of the tool process.</param>
    /// <param name="elapsed">The elapsed duration of the operation.</param>
    public ToolOperationResult(bool success, IEnumerable<string>? errors = null, int exitCode = 0, TimeSpan elapsed = default)
        : base(success, errors, elapsed)
    {
        ExitCode = exitCode;
    }

    /// <summary>
    /// Gets the tool exit code.
    /// </summary>
    public int ExitCode { get; init; }

    /// <summary>
    /// Creates a successful tool operation result.
    /// </summary>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="elapsed">The elapsed execution time.</param>
    /// <returns>A new successful <see cref="ToolOperationResult"/> instance.</returns>
    public static ToolOperationResult CreateSuccess(int exitCode = 0, TimeSpan elapsed = default) =>
        new(true, null, exitCode, elapsed);

    /// <summary>
    /// Creates a failed tool operation result.
    /// </summary>
    /// <param name="error">The failure error message.</param>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="elapsed">The elapsed execution time.</param>
    /// <returns>A new failed <see cref="ToolOperationResult"/> instance.</returns>
    public static ToolOperationResult CreateFailure(string error, int exitCode = -1, TimeSpan elapsed = default) =>
        new(false, [error], exitCode, elapsed);

    /// <summary>
    /// Creates a failed tool operation result with multiple errors.
    /// </summary>
    /// <param name="errors">The collection of error messages.</param>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="elapsed">The elapsed execution time.</param>
    /// <returns>A new failed <see cref="ToolOperationResult"/> instance.</returns>
    public static ToolOperationResult CreateFailure(IEnumerable<string> errors, int exitCode = -1, TimeSpan elapsed = default) =>
        new(false, errors, exitCode, elapsed);
}
