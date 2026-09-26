using System;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Result of content update operation.
/// </summary>
public record ContentUpdateResult
{
    /// <summary>
    /// Gets a value indicating whether the manifest ID changed during the update.
    /// </summary>
    public bool IdChanged { get; init; }

    /// <summary>
    /// Gets the number of profiles updated with new manifest reference.
    /// </summary>
    public int ProfilesUpdated { get; init; }

    /// <summary>
    /// Gets the number of workspaces invalidated due to content change.
    /// </summary>
    public int WorkspacesInvalidated { get; init; }

    /// <summary>
    /// Gets the duration of the operation.
    /// </summary>
    public TimeSpan Duration { get; init; }
}
