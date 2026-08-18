using System;
using System.Collections.Generic;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Result of a conversion operation.
/// </summary>
public class ConversionOperationResult : ResultBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConversionOperationResult"/> class.
    /// </summary>
    public ConversionOperationResult()
        : base(true, (IEnumerable<string>?)null, default)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversionOperationResult"/> class.
    /// </summary>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="errors">The errors, if any.</param>
    /// <param name="elapsed">The elapsed time.</param>
    public ConversionOperationResult(bool success, IEnumerable<string>? errors = null, TimeSpan elapsed = default)
        : base(success, errors, elapsed)
    {
    }

    /// <summary>Creates a successful conversion operation result.</summary>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A successful <see cref="ConversionOperationResult"/>.</returns>
    public static ConversionOperationResult CreateSuccess(TimeSpan elapsed = default) => new(true, (IEnumerable<string>?)null, elapsed);

    /// <summary>Creates a failed conversion operation result with a single error message.</summary>
    /// <param name="error">The error message.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="ConversionOperationResult"/>.</returns>
    public static ConversionOperationResult CreateFailure(string error, TimeSpan elapsed = default) => new(false, [error], elapsed);

    /// <summary>Creates a failed conversion operation result with multiple error messages.</summary>
    /// <param name="errors">The error messages.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="ConversionOperationResult"/>.</returns>
    public static ConversionOperationResult CreateFailure(IEnumerable<string> errors, TimeSpan elapsed = default) => new(false, errors, elapsed);
}
