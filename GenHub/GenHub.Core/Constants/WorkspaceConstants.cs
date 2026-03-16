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
}
