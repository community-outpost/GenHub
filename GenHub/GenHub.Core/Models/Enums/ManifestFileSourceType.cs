namespace GenHub.Core.Models.Enums;

/// <summary>
/// File handling strategy for workspace preparation.
/// </summary>
public enum ManifestFileSourceType
{
    /// <summary>
    /// Link from base game installation.
    /// </summary>
    LinkFromBase,

    /// <summary>
    /// Copy unique content file.
    /// </summary>
    CopyUnique,

    /// <summary>
    /// Download from remote source.
    /// </summary>
    Download,

    /// <summary>
    /// Generate or patch existing file.
    /// </summary>
    Generate,

    /// <summary>
    /// Optional addon detection.
    /// </summary>
    OptionalAddon,
}
