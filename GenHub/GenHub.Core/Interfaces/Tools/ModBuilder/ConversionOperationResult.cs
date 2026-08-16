namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Result of a conversion operation.
/// </summary>
public class ConversionOperationResult
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
}
