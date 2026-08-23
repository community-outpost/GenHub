using GenHub.Core.Models.Enums;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants related to workspace management and configuration.
/// </summary>
public static class WorkspaceConstants
{
    /// <summary>
    /// The default workspace strategy to use when none is specified.
    /// Default is HardLink as it provides space-efficient file management with good compatibility.
    /// </summary>
    public const WorkspaceStrategy DefaultWorkspaceStrategy = WorkspaceStrategy.HardLink;

    /// <summary>
    /// Guidance message appended to errors when zero-copy hard links or symlinks cannot be created.
    /// </summary>
    public const string ZeroCopyElevationGuidance =
        "To use zero-copy workspaces without copying game files, ensure GenHub has permission to create links (on Windows, enable Developer Mode or run as Administrator).";
}
