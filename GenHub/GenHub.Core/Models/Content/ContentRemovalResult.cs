using System;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Result of content removal operation.
/// </summary>
public record ContentRemovalResult
{
    /// <summary>
    /// Gets the number of profiles updated to remove manifest references.
    /// </summary>
    public int ProfilesUpdated { get; init; }

    /// <summary>
    /// Gets the number of workspaces invalidated due to content removal.
    /// </summary>
    public int WorkspacesInvalidated { get; init; }

    /// <summary>
    /// Gets the number of manifests removed from the pool.
    /// </summary>
    public int ManifestsRemoved { get; init; }

    /// <summary>
    /// Gets the number of CAS objects collected during garbage collection.
    /// </summary>
    public int CasObjectsCollected { get; init; }

    /// <summary>
    /// Gets the bytes freed during garbage collection.
    /// </summary>
    public long BytesFreed { get; init; }

    /// <summary>
    /// Gets the duration of the operation.
    /// </summary>
    public TimeSpan Duration { get; init; }
}
