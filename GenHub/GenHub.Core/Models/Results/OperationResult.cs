namespace GenHub.Core.Models.Results;

/// <summary>Represents the result of an operation, including success/failure, data, and errors.</summary>
/// <typeparam name="T">The type of data returned by the operation.</typeparam>
public class OperationResult<T> : ResultBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OperationResult{T}"/> class.
    /// </summary>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="data">The data returned by the operation.</param>
    /// <param name="errors">The errors, if any.</param>
    /// <param name="elapsed">The elapsed time.</param>
    protected OperationResult(bool success, T? data, IEnumerable<string>? errors = null, TimeSpan elapsed = default)
        : base(success, errors, elapsed)
    {
        Data = data;
    }

    /// <summary>Gets the data returned by the operation.</summary>
    public T? Data { get; }

    /// <summary>Creates a successful operation result.</summary>
    /// <param name="data">The data returned by the operation.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A successful <see cref="OperationResult{T}"/>.</returns>
    public static OperationResult<T> CreateSuccess(T data, TimeSpan elapsed = default)
    {
        return new OperationResult<T>(true, data, null, elapsed);
    }

    /// <summary>Creates a failed operation result with a single error message.</summary>
    /// <param name="error">The error message.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="OperationResult{T}"/>.</returns>
    public static OperationResult<T> CreateFailure(string error, TimeSpan elapsed = default)
    {
        return new OperationResult<T>(false, default, new[] { error }, elapsed);
    }

    /// <summary>Creates a failed operation result with multiple error messages.</summary>
    /// <param name="errors">The error messages.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="OperationResult{T}"/>.</returns>
    public static OperationResult<T> CreateFailure(IEnumerable<string> errors, TimeSpan elapsed = default)
    {
        ArgumentNullException.ThrowIfNull(errors, nameof(errors));
        if (!errors.Any())
            throw new ArgumentException("Errors collection cannot be empty.", nameof(errors));
        return new OperationResult<T>(false, default, errors, elapsed);
    }

    /// <summary>Creates a failed operation result from another result, copying its errors.</summary>
    /// <param name="result">The source result to copy errors from.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="OperationResult{T}"/> with copied errors.</returns>
    public static OperationResult<T> CreateFailure(ResultBase result, TimeSpan elapsed = default)
    {
        ArgumentNullException.ThrowIfNull(result, nameof(result));
        return new OperationResult<T>(false, default, result.Errors ?? Enumerable.Empty<string>(), elapsed);
    }
}
