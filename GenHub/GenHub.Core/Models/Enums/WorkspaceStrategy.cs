namespace GenHub.Core.Models.Enums;

/// <summary>
/// Workspace preparation strategy preference.
/// </summary>
public enum WorkspaceStrategy
{
    /// <summary>
    /// Hard link strategy.
    /// </summary>
    HardLink,

    /// <summary>
    /// Symlink only strategy.
    /// </summary>
    SymlinkOnly,

    /// <summary>
    /// Hybrid symlink/copy strategy.
    /// </summary>
    HybridCopySymlink,

    /// <summary>
    /// Full copy strategy.
    /// </summary>
    FullCopy,

    /// <summary>
    /// Content addressable strategy.
    /// </summary>
    ContentAddressable,

    /// <summary>
    /// Full symlink strategy.
    /// </summary>
    FullSymlink,
}
