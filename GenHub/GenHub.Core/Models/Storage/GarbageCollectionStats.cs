using System;

namespace GenHub.Core.Models.Storage;

/// <summary>
/// Statistics from a garbage collection run.
/// </summary>
public record GarbageCollectionStats
{
    /// <summary>
    /// Gets the number of CAS objects scanned.
    /// </summary>
    public int ObjectsScanned { get; init; }

    /// <summary>
    /// Gets the number of CAS objects that are referenced.
    /// </summary>
    public int ObjectsReferenced { get; init; }

    /// <summary>
    /// Gets the number of CAS objects deleted.
    /// </summary>
    public int ObjectsDeleted { get; init; }

    /// <summary>
    /// Gets the bytes freed by deletion.
    /// </summary>
    public long BytesFreed { get; init; }

    /// <summary>
    /// Gets the duration of the GC operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets a value indicating whether garbage collection was skipped because another GC operation was already in progress.
    /// </summary>
    public bool Skipped { get; init; }

    /// <summary>
    /// Gets a value indicating whether garbage collection was skipped specifically because another GC operation was already in progress.
    /// </summary>
    public bool InProgress { get; init; }

    /// <summary>
    /// Gets a static instance representing a skipped GC operation.
    /// </summary>
    public static GarbageCollectionStats SkippedResult { get; } = new()
    {
        ObjectsScanned = 0,
        ObjectsReferenced = 0,
        ObjectsDeleted = 0,
        BytesFreed = 0,
        Duration = TimeSpan.Zero,
        Skipped = true,
        InProgress = false,
    };

    /// <summary>
    /// Gets a static instance representing a GC operation that was skipped because another is already in progress.
    /// </summary>
    public static GarbageCollectionStats InProgressResult { get; } = new()
    {
        ObjectsScanned = 0,
        ObjectsReferenced = 0,
        ObjectsDeleted = 0,
        BytesFreed = 0,
        Duration = TimeSpan.Zero,
        Skipped = true,
        InProgress = true,
    };
}
