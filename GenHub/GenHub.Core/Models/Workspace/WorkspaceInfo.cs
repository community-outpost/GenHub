using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Validation;

namespace GenHub.Core.Models.Workspace;

/// <summary>
/// Information about a prepared workspace.
/// </summary>
public class WorkspaceInfo
{
    /// <summary>Gets or sets the workspace identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the workspace path.</summary>
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the game version ID.</summary>
    public string GameClientId { get; set; } = string.Empty;

    /// <summary>Gets or sets the workspace strategy used.</summary>
    public WorkspaceStrategy Strategy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the workspace preparation was successful.
    /// </summary>
    public bool IsPrepared { get; set; }

    /// <summary>
    /// Gets or sets a list of validation issues or errors encountered during preparation.
    /// </summary>
    public List<ValidationIssue> ValidationIssues { get; set; } = [];

    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the last access timestamp.</summary>
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the total size in bytes.</summary>
    public long TotalSizeBytes { get; set; }

    /// <summary>Gets or sets the number of files.</summary>
    public int FileCount { get; set; }

    /// <summary>Gets or sets a value indicating whether the workspace is valid.</summary>
    public bool IsValid { get; set; } = true;

    /// <summary>Gets or sets the main executable path.</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the working directory.</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of manifest IDs used to create this workspace.
    /// Used to detect when manifests have changed and workspace needs recreation.
    /// </summary>
    public List<string> ManifestIds { get; set; } = [];
}