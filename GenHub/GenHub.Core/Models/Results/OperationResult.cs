using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Models.Results;

/// <summary>Represents the result of an operation without return data.</summary>
public class OperationResult : ResultBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OperationResult"/> class.
    /// </summary>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="errors">The errors, if any.</param>
    /// <param name="elapsed">The elapsed time.</param>
    protected OperationResult(bool success, IEnumerable<string>? errors = null, TimeSpan elapsed = default)
        : base(success, errors, elapsed)
    {
    }

    /// <summary>Creates a successful operation result.</summary>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A successful <see cref="OperationResult"/>.</returns>
    public static OperationResult CreateSuccess(TimeSpan elapsed = default)
    {
        return new OperationResult(true, null, elapsed);
    }

    /// <summary>Creates a failed operation result with a single error message.</summary>
    /// <param name="error">The error message.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="OperationResult"/>.</returns>
    public static OperationResult CreateFailure(string error, TimeSpan elapsed = default)
    {
        return new OperationResult(false, [error], elapsed);
    }

    /// <summary>Creates a failed operation result with multiple error messages.</summary>
    /// <param name="errors">The error messages.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="OperationResult"/>.</returns>
    public static OperationResult CreateFailure(IEnumerable<string> errors, TimeSpan elapsed = default)
    {
        ArgumentNullException.ThrowIfNull(errors, nameof(errors));
        if (!errors.Any())
            throw new ArgumentException("Errors collection cannot be empty.", nameof(errors));
        return new OperationResult(false, errors, elapsed);
    }
}