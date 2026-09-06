using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Result of content replacement operation.
/// </summary>
public record ContentReplacementResult
{
    /// <summary>
    /// Gets the number of profiles updated with new manifest references.
    /// </summary>
    public int ProfilesUpdated { get; init; }

    /// <summary>
    /// Gets the number of workspaces invalidated due to content changes.
    /// </summary>
    public int WorkspacesInvalidated { get; init; }

    /// <summary>
    /// Gets the number of old manifests removed from the pool.
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

    /// <summary>
    /// Gets any warnings that occurred during the operation.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
