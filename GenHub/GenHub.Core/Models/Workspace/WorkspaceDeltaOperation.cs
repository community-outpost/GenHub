namespace GenHub.Core.Models.Workspace;

/// <summary>
/// Workspace delta operation types for reconciliation.
/// </summary>
public enum WorkspaceDeltaOperation
{
    /// <summary>File needs to be added to workspace.</summary>
    Add,

    /// <summary>File needs to be updated in workspace.</summary>
    Update,

    /// <summary>File needs to be removed from workspace.</summary>
    Remove,

    /// <summary>File is unchanged and can be skipped.</summary>
    Skip,
}
