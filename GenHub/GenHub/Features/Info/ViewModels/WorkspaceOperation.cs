namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// Represents a single file operation in the workspace demo.
/// </summary>
public class WorkspaceOperation
{
    /// <summary>
    /// Gets or sets the source path.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target path in the workspace.
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of link (Hardlink, Symlink, Copy).
    /// </summary>
    public string Type { get; set; } = string.Empty;
}
