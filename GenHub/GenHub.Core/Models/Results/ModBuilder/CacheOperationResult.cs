using System;
using System.Collections.Generic;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Models.Results.ModBuilder;

/// <summary>
/// Represents the result of a cache operation.
/// </summary>
public class CacheOperationResult : ResultBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CacheOperationResult"/> class.
    /// </summary>
    public CacheOperationResult()
        : base(true, (IEnumerable<string>?)null, default)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheOperationResult"/> class.
    /// </summary>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="errors">The errors, if any.</param>
    /// <param name="elapsed">The elapsed time.</param>
    public CacheOperationResult(bool success, IEnumerable<string>? errors = null, TimeSpan elapsed = default)
        : base(success, errors, elapsed)
    {
    }

    /// <summary>Creates a successful cache operation result.</summary>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A successful <see cref="CacheOperationResult"/>.</returns>
    public static CacheOperationResult CreateSuccess(TimeSpan elapsed = default) => new(true, (IEnumerable<string>?)null, elapsed);

    /// <summary>Creates a failed cache operation result with a single error message.</summary>
    /// <param name="error">The error message.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="CacheOperationResult"/>.</returns>
    public static CacheOperationResult CreateFailure(string error, TimeSpan elapsed = default) => new(false, [error], elapsed);

    /// <summary>Creates a failed cache operation result with multiple error messages.</summary>
    /// <param name="errors">The error messages.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="CacheOperationResult"/>.</returns>
    public static CacheOperationResult CreateFailure(IEnumerable<string> errors, TimeSpan elapsed = default) => new(false, errors, elapsed);
}
