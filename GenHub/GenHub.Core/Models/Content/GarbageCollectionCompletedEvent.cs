using System;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Event raised after garbage collection completes.
/// </summary>
public record GarbageCollectionCompletedEvent(
    int ObjectsScanned,
    int ObjectsDeleted,
    long BytesFreed,
    TimeSpan Duration);
