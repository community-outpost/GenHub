namespace GenHub.Core.Models.Enums;

/// <summary>
/// Workspace preparation strategy preference.
/// </summary>
public enum WorkspaceStrategy
{
    /// <summary>
    /// Symlink only strategy - creates symbolic links to all files. Minimal disk usage, requires admin rights.
    /// </summary>
    SymlinkOnly,

    /// <summary>
    /// Full copy strategy - copies all files to workspace. Maximum compatibility and isolation, highest disk usage.
    /// </summary>
    FullCopy,

    /// <summary>
    /// Hybrid copy/symlink strategy - copies essential files, symlinks others. Balanced disk usage and compatibility.
    /// </summary>
    HybridCopySymlink,

    /// <summary>
    /// Hard link strategy - creates hard links where possible, copies otherwise. Space-efficient, requires same volume.
    /// </summary>
    HardLink,
}
