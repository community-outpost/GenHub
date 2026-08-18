using System.Text.Json.Serialization;
using GenHub.Core.Serialization;

namespace GenHub.Core.Models.Enums;

/// <summary>
/// Workspace preparation strategy preference.
/// </summary>
/// <remarks>
/// The numeric values are part of the on-disk format for workspace metadata written by
/// releases up to v0.0.3 and must not be reordered.
/// </remarks>
[JsonConverter(typeof(JsonWorkspaceStrategyConverter))]
public enum WorkspaceStrategy
{
    /// <summary>
    /// Symlink only strategy - creates symbolic links to all files. Minimal disk usage, requires admin rights.
    /// </summary>
    SymlinkOnly = 0,

    /// <summary>
    /// Full copy strategy - copies all files to workspace. Maximum compatibility and isolation, highest disk usage.
    /// </summary>
    FullCopy = 1,

    /// <summary>
    /// Hybrid copy/symlink strategy - copies essential files, symlinks others. Balanced disk usage and compatibility.
    /// </summary>
    HybridCopySymlink = 2,

    /// <summary>
    /// Hard link strategy - creates hard links where possible, copies otherwise. Space-efficient, requires same volume.
    /// Default strategy for new profiles.
    /// </summary>
    HardLink = 3,
}
