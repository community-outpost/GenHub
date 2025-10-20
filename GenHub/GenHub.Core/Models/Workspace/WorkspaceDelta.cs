using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Models.Workspace;

/// <summary>
/// Represents a delta operation for workspace reconciliation.
/// </summary>
public class WorkspaceDelta
{
    /// <summary>Gets or sets the operation type.</summary>
    public WorkspaceDeltaOperation Operation { get; set; }

    /// <summary>Gets or sets the manifest file.</summary>
    public ManifestFile File { get; set; } = null!;

    /// <summary>Gets or sets the workspace file path.</summary>
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the reason for the operation.</summary>
    public string Reason { get; set; } = string.Empty;
}
