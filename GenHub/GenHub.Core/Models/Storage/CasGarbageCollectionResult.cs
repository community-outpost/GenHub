using System;

namespace GenHub.Core.Models.Storage;

/// <summary>
/// Result of a CAS garbage collection operation.
/// </summary>
public class CasGarbageCollectionResult
{
    /// <summary>
    /// Gets or sets the number of objects that were deleted.
    /// </summary>
    public int ObjectsDeleted { get; set; }

    /// <summary>
    /// Gets or sets the total number of bytes freed.
    /// </summary>
    public long BytesFreed { get; set; }

    /// <summary>
    /// Gets or sets the total number of objects scanned during collection.
    /// </summary>
    public int ObjectsScanned { get; set; }

    /// <summary>
    /// Gets or sets the number of objects that were referenced and kept.
    /// </summary>
    public int ObjectsReferenced { get; set; }

    /// <summary>
    /// Gets or sets the duration of the garbage collection operation.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the operation completed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets any error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets the percentage of storage freed.
    /// </summary>
    public double PercentageFreed => ObjectsScanned > 0 ? (double)ObjectsDeleted / ObjectsScanned * 100 : 0;
}
