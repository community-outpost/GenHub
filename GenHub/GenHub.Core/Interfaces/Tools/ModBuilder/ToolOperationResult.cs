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
    public ToolOperationResult(bool success, IEnumerable<string>? errors = null, int exitCode = 0, TimeSpan elapsed = default)
        : base(success, errors, elapsed)
    {
        ExitCode = exitCode;
    }

    /// <summary>
    /// Gets or sets the tool exit code.
    /// </summary>
    public int ExitCode { get; init; }

    /// <summary>
    /// Creates a successful tool operation result.
    /// </summary>
    public static ToolOperationResult CreateSuccess(int exitCode = 0, TimeSpan elapsed = default) =>
        new(true, null, exitCode, elapsed);

    /// <summary>
    /// Creates a failed tool operation result.
    /// </summary>
    public static ToolOperationResult CreateFailure(string error, int exitCode = -1, TimeSpan elapsed = default) =>
        new(false, [error], exitCode, elapsed);

    /// <summary>
    /// Creates a failed tool operation result with multiple errors.
    /// </summary>
    public static ToolOperationResult CreateFailure(IEnumerable<string> errors, int exitCode = -1, TimeSpan elapsed = default) =>
        new(false, errors, exitCode, elapsed);
}
