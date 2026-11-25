using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Results.ModBuilder;

/// <summary>
/// Represents the result of a build operation, deriving from ResultBase.
/// </summary>
public class BuildOperationResult : ResultBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BuildOperationResult"/> class with multiple errors.
    /// </summary>
    /// <param name="success">Whether the build succeeded.</param>
    /// <param name="errors">Any error messages.</param>
    /// <param name="elapsed">Time taken for the build operation.</param>
    /// <param name="filesProcessed">Number of files processed.</param>
    /// <param name="filesSkipped">Number of files skipped.</param>
    /// <param name="filesFailed">Number of files failed.</param>
    public BuildOperationResult(
        bool success,
        IEnumerable<string>? errors = null,
        TimeSpan elapsed = default,
        int filesProcessed = 0,
        int filesSkipped = 0,
        int filesFailed = 0)
        : base(success, errors, elapsed)
    {
        FilesProcessed = filesProcessed;
        FilesSkipped = filesSkipped;
        FilesFailed = filesFailed;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BuildOperationResult"/> class with a single error.
    /// </summary>
    /// <param name="success">Whether the build succeeded.</param>
    /// <param name="error">A single error message.</param>
    /// <param name="elapsed">Time taken for the build operation.</param>
    /// <param name="filesProcessed">Number of files processed.</param>
    /// <param name="filesSkipped">Number of files skipped.</param>
    /// <param name="filesFailed">Number of files failed.</param>
    public BuildOperationResult(
        bool success,
        string? error,
        TimeSpan elapsed = default,
        int filesProcessed = 0,
        int filesSkipped = 0,
        int filesFailed = 0)
        : base(success, error, elapsed)
    {
        FilesProcessed = filesProcessed;
        FilesSkipped = filesSkipped;
        FilesFailed = filesFailed;
    }

    /// <summary>
    /// Gets the number of files processed.
    /// </summary>
    public int FilesProcessed { get; init; }

    /// <summary>
    /// Gets the number of files failed.
    /// </summary>
    public int FilesFailed { get; init; }

    /// <summary>
    /// Gets the number of files skipped (unchanged).
    /// </summary>
    public int FilesSkipped { get; init; }

    /// <summary>
    /// Creates a successful build operation result.
    /// </summary>
    /// <param name="filesProcessed">Number of files processed.</param>
    /// <param name="filesSkipped">Number of files skipped.</param>
    /// <param name="filesFailed">Number of files failed.</param>
    /// <param name="elapsed">Time taken for the build operation.</param>
    /// <returns>A successful build operation result.</returns>
    public static BuildOperationResult CreateSuccess(
        int filesProcessed = 0,
        int filesSkipped = 0,
        int filesFailed = 0,
        TimeSpan elapsed = default)
    {
        return new BuildOperationResult(
            success: true,
            errors: null,
            elapsed: elapsed,
            filesProcessed: filesProcessed,
            filesSkipped: filesSkipped,
            filesFailed: filesFailed);
    }

    /// <summary>
    /// Creates a failed build operation result with error messages.
    /// </summary>
    /// <param name="errors">Collection of error messages.</param>
    /// <param name="filesProcessed">Number of files processed.</param>
    /// <param name="filesSkipped">Number of files skipped.</param>
    /// <param name="filesFailed">Number of files failed.</param>
    /// <param name="elapsed">Time taken for the build operation.</param>
    /// <returns>A failed build operation result.</returns>
    public static BuildOperationResult CreateFailure(
        IEnumerable<string> errors,
        int filesProcessed = 0,
        int filesSkipped = 0,
        int filesFailed = 0,
        TimeSpan elapsed = default)
    {
        return new BuildOperationResult(
            success: false,
            errors: errors,
            elapsed: elapsed,
            filesProcessed: filesProcessed,
            filesSkipped: filesSkipped,
            filesFailed: filesFailed);
    }

    /// <summary>
    /// Creates a failed build operation result with a single error message.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    /// <param name="filesProcessed">Number of files processed.</param>
    /// <param name="filesSkipped">Number of files skipped.</param>
    /// <param name="filesFailed">Number of files failed.</param>
    /// <param name="elapsed">Time taken for the build operation.</param>
    /// <returns>A failed build operation result.</returns>
    public static BuildOperationResult CreateFailure(
        string errorMessage,
        int filesProcessed = 0,
        int filesSkipped = 0,
        int filesFailed = 0,
        TimeSpan elapsed = default)
    {
        return new BuildOperationResult(
            success: false,
            error: errorMessage,
            elapsed: elapsed,
            filesProcessed: filesProcessed,
            filesSkipped: filesSkipped,
            filesFailed: filesFailed);
    }
}
