namespace GenHub.Core.Models.Results.CAS;

/// <summary>
/// Result of a CAS garbage collection operation.
/// </summary>
public class CasGarbageCollectionResult : ResultBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CasGarbageCollectionResult"/> class.
    /// </summary>
    /// <param name="success">Whether the operation was successful.</param>
    /// <param name="errors">Any errors that occurred.</param>
    /// <param name="elapsed">Time taken for the operation.</param>
    public CasGarbageCollectionResult(bool success, IEnumerable<string>? errors = null, TimeSpan elapsed = default)
        : base(success, errors, elapsed)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CasGarbageCollectionResult"/> class.
    /// </summary>
    /// <param name="success">Whether the operation was successful.</param>
    /// <param name="error">A single error message.</param>
    /// <param name="elapsed">Time taken for the operation.</param>
    public CasGarbageCollectionResult(bool success, string? error = null, TimeSpan elapsed = default)
        : base(success, error, elapsed)
    {
    }

    /// <summary>Gets or sets the number of objects that were deleted.</summary>
    public int ObjectsDeleted { get; set; }

    /// <summary>Gets or sets the total number of bytes freed.</summary>
    public long BytesFreed { get; set; }

    /// <summary>Gets or sets the total number of objects scanned during collection.</summary>
    public int ObjectsScanned { get; set; }

    /// <summary>Gets or sets the number of objects that were referenced and kept.</summary>
    public int ObjectsReferenced { get; set; }

    /// <summary>Gets the percentage of storage freed.</summary>
    public double PercentageFreed
    {
        get
        {
            if (ObjectsScanned <= 0)
            {
                return 0;
            }

            if (ObjectsDeleted < 0)
            {
                return 0;
            }

            if (ObjectsDeleted > ObjectsScanned)
            {
                // This shouldn't happen in normal operation, but guard against it
                return 100;
            }

            return (double)ObjectsDeleted / ObjectsScanned * 100;
        }
    }
}