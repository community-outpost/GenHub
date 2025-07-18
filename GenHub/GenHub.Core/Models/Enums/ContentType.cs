namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the type of content in a manifest.
/// </summary>
public enum ContentType
{
    /// <summary>
    /// Base game installation (Steam, EA App).
    /// </summary>
    BaseGame,

    /// <summary>
    /// Total conversion or major modification.
    /// </summary>
    Mod,

    /// <summary>
    /// Utility or enhancement tool.
    /// </summary>
    Addon,

    /// <summary>
    /// Balance or configuration changes.
    /// </summary>
    Patch,

    /// <summary>
    /// Map pack or additional content.
    /// </summary>
    MapPack,

    /// <summary>
    /// Standalone game version with custom executable.
    /// </summary>
    StandaloneVersion,
}
