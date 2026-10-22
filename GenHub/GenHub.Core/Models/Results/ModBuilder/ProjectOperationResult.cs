namespace GenHub.Core.Models.Results.ModBuilder;

/// <summary>
/// Represents the result of a ModBuilder project operation.
/// </summary>
/// <typeparam name="T">The type of data returned by the operation.</typeparam>
public class ProjectOperationResult<T> : OperationResult<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectOperationResult{T}"/> class.
    /// </summary>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="data">The data returned by the operation.</param>
    /// <param name="errors">The errors, if any.</param>
    /// <param name="validationErrors">Validation errors, if any.</param>
    /// <param name="elapsed">The elapsed time.</param>
    protected ProjectOperationResult(
        bool success,
        T? data,
        IEnumerable<string>? errors = null,
        IEnumerable<string>? validationErrors = null,
        TimeSpan elapsed = default)
        : base(success, data, errors, elapsed)
    {
        ValidationErrors = validationErrors?.ToList().AsReadOnly() ?? new List<string>().AsReadOnly();
    }

    /// <summary>
    /// Gets the validation errors, if any.
    /// </summary>
    public IReadOnlyList<string> ValidationErrors { get; }

    /// <summary>
    /// Gets a value indicating whether there are validation errors.
    /// </summary>
    public bool HasValidationErrors => ValidationErrors.Count > 0;

    /// <summary>
    /// Creates a successful project operation result.
    /// </summary>
    /// <param name="data">The data returned by the operation.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A successful <see cref="ProjectOperationResult{T}"/>.</returns>
    public static new ProjectOperationResult<T> CreateSuccess(T data, TimeSpan elapsed = default)
        => new(true, data, null, null, elapsed);

    /// <summary>
    /// Creates a failed project operation result with a single error message.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="ProjectOperationResult{T}"/>.</returns>
    public static new ProjectOperationResult<T> CreateFailure(string error, TimeSpan elapsed = default)
        => new(false, default, new[] { error }, null, elapsed);

    /// <summary>
    /// Creates a failed project operation result with multiple error messages.
    /// </summary>
    /// <param name="errors">The error messages.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="ProjectOperationResult{T}"/>.</returns>
    public static new ProjectOperationResult<T> CreateFailure(IEnumerable<string> errors, TimeSpan elapsed = default)
        => new(false, default, errors, null, elapsed);

    /// <summary>
    /// Creates a failed project operation result with validation errors.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <param name="validationErrors">The validation errors.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="ProjectOperationResult{T}"/>.</returns>
    public static ProjectOperationResult<T> CreateValidationFailure(
        string error,
        IEnumerable<string> validationErrors,
        TimeSpan elapsed = default)
        => new(false, default, new[] { error }, validationErrors, elapsed);
}
