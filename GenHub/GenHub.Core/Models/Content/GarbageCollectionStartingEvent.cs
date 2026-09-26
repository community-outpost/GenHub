namespace GenHub.Core.Models.Content;

/// <summary>
/// Event raised before garbage collection runs.
/// </summary>
public record GarbageCollectionStartingEvent(
    bool IsForced,
    int EstimatedOrphanedObjects);
