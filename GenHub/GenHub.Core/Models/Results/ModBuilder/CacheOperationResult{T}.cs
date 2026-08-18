using System;
using System.Collections.Generic;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Models.Results.ModBuilder;

/// <summary>
/// Represents the result of a cache operation with data.
/// </summary>
/// <typeparam name="T">The type of data returned by the operation.</typeparam>
public class CacheOperationResult<T> : ResultBase
{
    /// <summary>
    /// Gets or sets the result data.
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheOperationResult{T}"/> class.
    /// </summary>
    public CacheOperationResult()
        : base(true, (IEnumerable<string>?)null, default)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheOperationResult{T}"/> class.
    /// </summary>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="data">The result data.</param>
    /// <param name="errors">The errors, if any.</param>
    /// <param name="elapsed">The elapsed time.</param>
    public CacheOperationResult(bool success, T? data = default, IEnumerable<string>? errors = null, TimeSpan elapsed = default)
        : base(success, errors, elapsed)
    {
        Data = data;
    }

    /// <summary>Creates a successful cache operation result with data.</summary>
    /// <param name="data">The result data.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A successful <see cref="CacheOperationResult{T}"/>.</returns>
    public static CacheOperationResult<T> CreateSuccess(T data, TimeSpan elapsed = default) => new(true, data, (IEnumerable<string>?)null, elapsed);

    /// <summary>Creates a failed cache operation result with a single error message.</summary>
    /// <param name="error">The error message.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="CacheOperationResult{T}"/>.</returns>
    public static CacheOperationResult<T> CreateFailure(string error, TimeSpan elapsed = default) => new(false, default, [error], elapsed);

    /// <summary>Creates a failed cache operation result with multiple error messages.</summary>
    /// <param name="errors">The error messages.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="CacheOperationResult{T}"/>.</returns>
    public static CacheOperationResult<T> CreateFailure(IEnumerable<string> errors, TimeSpan elapsed = default) => new(false, default, errors, elapsed);
}
