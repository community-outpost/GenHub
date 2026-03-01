using System.Text.Json.Serialization;
using GenHub.Core.Serialization;

namespace GenHub.Core.Models.Enums;

/// <summary>
/// Workspace preparation strategy preference.
/// </summary>
[JsonConverter(typeof(JsonWorkspaceStrategyConverter))]
public enum WorkspaceStrategy
{
    /// <summary>
    /// Hard link strategy - creates hard links where possible, copies otherwise. Space-efficient, requires same volume.
    /// Default strategy for new profiles.
    /// </summary>
    HardLink = 0,

    /// <summary>
    /// Symlink only strategy - creates symbolic links to all files. Minimal disk usage, requires admin rights.
    /// </summary>
    SymlinkOnly = 1,

    /// <summary>
    /// Full copy strategy - copies all files to workspace. Maximum compatibility and isolation, highest disk usage.
    /// </summary>
    FullCopy = 2,

    /// <summary>
    /// Hybrid copy/symlink strategy - copies essential files, symlinks others. Balanced disk usage and compatibility.
    /// </summary>
    HybridCopySymlink = 3,
}
