using System.Collections.Generic;

namespace GenHub.Core.Interfaces.Storage;

/// <summary>
/// Audit of CAS reference state.
/// </summary>
public record CasReferenceAudit
{
    /// <summary>
    /// Gets the total number of manifests being tracked.
    /// </summary>
    public int TotalManifests { get; init; }

    /// <summary>
    /// Gets the total number of workspaces being tracked.
    /// </summary>
    public int TotalWorkspaces { get; init; }

    /// <summary>
    /// Gets the total number of unique CAS hashes referenced.
    /// </summary>
    public int TotalReferencedHashes { get; init; }

    /// <summary>
    /// Gets the total number of CAS objects in storage.
    /// </summary>
    public int TotalCasObjects { get; init; }

    /// <summary>
    /// Gets the number of CAS objects not referenced by any manifest or workspace.
    /// </summary>
    public int OrphanedObjects { get; init; }

    /// <summary>
    /// Gets the list of tracked manifest IDs.
    /// </summary>
    public IReadOnlyList<string> ManifestIds { get; init; } = [];

    /// <summary>
    /// Gets the list of tracked workspace IDs.
    /// </summary>
    public IReadOnlyList<string> WorkspaceIds { get; init; } = [];
}
