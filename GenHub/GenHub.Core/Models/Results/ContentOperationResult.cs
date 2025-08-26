using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Models.Results;

/// <summary>
/// Represents the result of a content operation.
/// </summary>
/// <typeparam name="T">The type of data returned by the operation.</typeparam>
public class ContentOperationResult<T>(bool success, T? data, IEnumerable<string>? errors = null)
    : ResultBase(success, errors)
{
    /// <summary>
    /// Gets the data returned by the operation, if successful.
    /// </summary>
    public T? Data { get; } = data;

    /// <summary>
    /// Gets the error message if the download failed.
    /// </summary>
    public string? ErrorMessage => FirstError;

    /// <summary>
    /// Creates a successful <see cref="ContentOperationResult{T}"/> containing <paramref name="data"/>.
    /// </summary>
    /// <param name="data">The data to return.</param>
    /// <returns>A successful result containing the provided data.</returns>
    public static ContentOperationResult<T> CreateSuccess(T data)
    {
        return new ContentOperationResult<T>(true, data);
    }

    /// <summary>
    /// Creates a failed <see cref="ContentOperationResult{T}"/> with a single error message.
    /// </summary>
    /// <param name="errorMessage">A description of the failure.</param>
    /// <returns>A failed result containing the provided error.</returns>
    public static ContentOperationResult<T> CreateFailure(string errorMessage)
    {
        return new ContentOperationResult<T>(false, default, errorMessage != null ? new[] { errorMessage } : null);
    }

    /// <summary>
    /// Creates a failed <see cref="ContentOperationResult{T}"/> with multiple error messages.
    /// </summary>
    /// <param name="errors">The error messages.</param>
    /// <returns>A failed result containing the provided errors.</returns>
    public static ContentOperationResult<T> CreateFailure(IEnumerable<string> errors)
    {
        return new ContentOperationResult<T>(false, default, errors);
    }

    /// <summary>
    /// Creates a failed <see cref="ContentOperationResult{T}"/> from another <see cref="ResultBase"/>, copying its errors.
    /// </summary>
    /// <param name="result">Source result to copy errors from.</param>
    /// <returns>A failed result containing the copied errors.</returns>
    public static ContentOperationResult<T> CreateFailure(ResultBase result)
    {
        return new ContentOperationResult<T>(false, default, result.Errors ?? Enumerable.Empty<string>());
    }
}
