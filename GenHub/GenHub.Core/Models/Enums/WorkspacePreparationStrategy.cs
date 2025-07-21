namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines different workspace preparation strategies for game installations.
/// </summary>
public enum WorkspacePreparationStrategy
{
    /// <summary>
    /// Full copy strategy - copies all files to workspace. Maximum compatibility and isolation, highest disk usage.
    /// </summary>
    FullCopy,

    /// <summary>
    /// Symlink only strategy - creates symbolic links to all files. Minimal disk usage, requires admin rights.
    /// </summary>
    SymlinkOnly,

    /// <summary>
    /// Hybrid copy/symlink strategy - copies essential files, symlinks others. Balanced disk usage and compatibility.
    /// </summary>
    HybridCopySymlink,

    /// <summary>
    /// Hard link strategy - creates hard links where possible, copies otherwise. Space-efficient, requires same volume.
    /// </summary>
    HardLink,
}
