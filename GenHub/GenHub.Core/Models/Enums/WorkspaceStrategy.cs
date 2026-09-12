using System.Text.Json.Serialization;
using GenHub.Core.Serialization;

namespace GenHub.Core.Models.Enums;

/// <summary>
/// Workspace preparation strategy preference.
/// </summary>
/// <remarks>
/// <para>
/// The numeric values are part of the on-disk format. Releases up to v0.0.3 serialized workspace
/// metadata without an enum converter, so <c>workspaces.json</c> holds raw ordinals in this order;
/// they must not be reordered. Profile files are unaffected: v0.0.3 wrote the member name.
/// </para>
/// <para>
/// Builds of the default branch made after v0.0.3 and before this ordering was restored wrote
/// ordinals under a reordered enum, so numbers they persisted are now read as a different member
/// (<c>0</c> meant HardLink there and means SymlinkOnly here). No release is affected, but such an
/// install should have its <c>workspaces.json</c> and profile strategies checked after upgrading.
/// </para>
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
