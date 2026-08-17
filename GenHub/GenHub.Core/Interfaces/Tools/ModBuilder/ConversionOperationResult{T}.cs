namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Result of a conversion operation with data.
/// </summary>
/// <typeparam name="T">The type of data returned by the operation.</typeparam>
public class ConversionOperationResult<T>
{
    /// <summary>
    /// Gets or sets a value indicating whether the operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the list of errors.
    /// </summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>
    /// Gets the first error message, if any.
    /// </summary>
    public string? FirstError => Errors.Count > 0 ? Errors[0] : null;

    /// <summary>
    /// Gets or sets the result data.
    /// </summary>
    public T? Data { get; set; }
}
