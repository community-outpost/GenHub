namespace GenHub.Core.Models.Content;

/// <summary>
/// Types of reconciliation operations.
/// </summary>
public enum ReconciliationOperationType
{
    /// <summary>
    /// Replacing manifest references in profiles.
    /// </summary>
    ManifestReplacement,

    /// <summary>
    /// Removing manifest references from profiles.
    /// </summary>
    ManifestRemoval,

    /// <summary>
    /// Updating a single profile.
    /// </summary>
    ProfileUpdate,

    /// <summary>
    /// Cleaning up workspaces.
    /// </summary>
    WorkspaceCleanup,

    /// <summary>
    /// Untracking CAS references.
    /// </summary>
    CasUntrack,

    /// <summary>
    /// Running garbage collection.
    /// </summary>
    GarbageCollection,

    /// <summary>
    /// Local content update orchestration.
    /// </summary>
    LocalContentUpdate,

    /// <summary>
    /// GeneralsOnline update orchestration.
    /// </summary>
    GeneralsOnlineUpdate,
}
