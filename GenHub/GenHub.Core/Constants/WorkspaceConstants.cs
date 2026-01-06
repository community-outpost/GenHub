using GenHub.Core.Models.Enums;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants related to workspace management and configuration.
/// </summary>
public static class WorkspaceConstants
{
    /// <summary>
    /// The default workspace strategy to use when none is specified.
    /// Default is HardLink as it is space-efficient and works on most systems (same volume).
    /// </summary>
    public const WorkspaceStrategy DefaultWorkspaceStrategy = WorkspaceStrategy.HardLink;
}
