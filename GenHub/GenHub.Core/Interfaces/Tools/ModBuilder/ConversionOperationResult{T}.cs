namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Result of a conversion operation with data.
/// </summary>
/// <typeparam name="T">The type of data returned by the operation.</typeparam>
public class ConversionOperationResult<T> : ConversionOperationResult
{
    /// <summary>
    /// Gets or sets the result data.
    /// </summary>
    public T? Data { get; set; }
}
