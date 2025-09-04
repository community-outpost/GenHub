using GenHub.Core.Models.Results;

namespace GenHub.Core.Models.Storage;

/// <summary>
/// Represents the result of a Content-Addressable Storage operation.
/// </summary>
/// <typeparam name="T">The type of data returned by the operation.</typeparam>
public class CasOperationResult<T> : ResultBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CasOperationResult{T}"/> class.
    /// </summary>
    /// <param name="success">Whether the operation was successful.</param>
    /// <param name="data">The data returned by the operation.</param>
    /// <param name="errorMessage">The error message if the operation failed.</param>
    /// <param name="context">Additional context information.</param>
    protected CasOperationResult(bool success, T? data, string? errorMessage = null, string? context = null)
        : base(success, errorMessage)
    {
        Data = data;
        Context = context;
    }

    /// <summary>
    /// Gets the data returned by the operation.
    /// </summary>
    public T? Data { get; init; }

    /// <summary>
    /// Gets the error message if the operation failed.
    /// </summary>
    public string? ErrorMessage => FirstError;

    /// <summary>
    /// Gets additional context information for the operation.
    /// </summary>
    public string? Context { get; init; }

    /// <summary>
    /// Creates a successful CAS operation result.
    /// </summary>
    /// <param name="data">The operation result data.</param>
    /// <returns>A successful result containing the data.</returns>
    public static CasOperationResult<T> CreateSuccess(T data)
    {
        return new CasOperationResult<T>(true, data);
    }

    /// <summary>
    /// Creates a failed CAS operation result.
    /// </summary>
    /// <param name="errorMessage">The error message describing the failure.</param>
    /// <returns>A failed result with the error message.</returns>
    public static CasOperationResult<T> CreateFailure(string errorMessage)
    {
        return new CasOperationResult<T>(false, default, errorMessage);
    }

    /// <summary>
    /// Creates a failed CAS operation result with additional context.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    /// <param name="context">Additional context information.</param>
    /// <returns>A failed result with error details.</returns>
    public static CasOperationResult<T> CreateFailure(string errorMessage, string context)
    {
        return new CasOperationResult<T>(false, default, errorMessage, context);
    }
}
