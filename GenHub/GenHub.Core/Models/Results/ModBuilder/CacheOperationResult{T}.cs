namespace GenHub.Core.Models.Results.ModBuilder;

/// <summary>
/// Represents the result of a cache operation with data.
/// </summary>
/// <typeparam name="T">The type of data returned by the operation.</typeparam>
public class CacheOperationResult<T> : CacheOperationResult
{
    /// <summary>
    /// Gets or sets the result data.
    /// </summary>
    public T? Data { get; set; }
}
