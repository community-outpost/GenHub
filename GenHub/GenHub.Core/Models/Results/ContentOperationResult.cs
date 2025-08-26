using GenHub.Core.Models.Results;

namespace GenHub.Core.Models.Results;

/// <summary>
/// Represents the result of a content operation.
/// </summary>
/// <typeparam name="T">The type of data returned by the operation.</typeparam>
public class ContentOperationResult<T> : ResultBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ContentOperationResult{T}"/> class.
    /// </summary>
    /// <param name="success">Whether the operation was successful.</param>
    /// <param name="data">The data returned by the operation.</param>
    /// <param name="errors">The error messages if the operation failed.</param>
    protected ContentOperationResult(bool success, T? data, IEnumerable<string>? errors = null)
        : base(success, errors)
    {
        Data = data;
    }

    /// <summary>
    /// Gets the data returned by the operation, if successful.
    /// </summary>
    public T? Data { get; private set; }

    /// <summary>
    /// Gets the error message if the download failed.
    /// </summary>
    public string? ErrorMessage => FirstError;

    /// <summary>
    /// Creates a successful result with data.
    /// </summary>
    /// <param name="data">The data to return.</param>
    /// <returns>A successful <see cref="ContentOperationResult{T}"/>.</returns>
    public static ContentOperationResult<T> CreateSuccess(T data)
    {
        return new ContentOperationResult<T>(true, data);
    }

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>A failed <see cref="ContentOperationResult{T}"/>.</returns>
    public static ContentOperationResult<T> CreateFailure(string errorMessage)
    {
        return new ContentOperationResult<T>(false, default, errorMessage != null ? new[] { errorMessage } : null);
    }

    /// <summary>
    /// Creates a failed result with multiple error messages.
    /// </summary>
    /// <param name="errors">The error messages.</param>
    /// <returns>A failed <see cref="ContentOperationResult{T}"/>.</returns>
    public static ContentOperationResult<T> CreateFailure(IEnumerable<string> errors)
    {
        return new ContentOperationResult<T>(false, default, errors);
    }

    /// <summary>
    /// Creates a failed result from another result.
    /// </summary>
    /// <param name="result">The result to copy the error from.</param>
    /// <returns>A failed <see cref="ContentOperationResult{T}"/>.</returns>
    public static ContentOperationResult<T> CreateFailure(ResultBase result)
    {
    return new ContentOperationResult<T>(false, default, result.Errors ?? Enumerable.Empty<string>());
    }
}
