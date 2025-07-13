namespace GenHub.Core.Models.Enums;

/// <summary>
/// Workspace preparation strategy preference.
/// </summary>
public enum WorkspaceStrategy
{
    /// <summary>
    /// Hybrid symlink/copy strategy.
    /// </summary>
    HybridSymlink,

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
