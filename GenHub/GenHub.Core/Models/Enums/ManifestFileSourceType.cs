namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the origin and type of a file within a manifest. This describes where the file
/// comes from and how it should be treated during workspace preparation.
/// </summary>
public enum ManifestFileSourceType
{
    /// <summary>
    /// The file originates from the base game installation. The workspace strategy will source it from the BaseInstallationPath.
    /// </summary>
    BaseGame,

    /// <summary>
    /// The file is unique content from this package (e.g., a mod file, custom asset).
    /// The workspace strategy will source it from the content directory or download it via its DownloadUrl if not present.
    /// </summary>
    Content,

    /// <summary>
    /// The file is a patch to be applied to an existing file in the workspace.
    /// This requires special handling by the workspace strategy.
    /// </summary>
    Patch,

    /// <summary>
    /// The file is part of an optional addon or extension. Used by validators to detect and report installed addons.
    /// The workspace strategy treats this similar to Content but marks it as optional.
    /// </summary>
    OptionalAddon,

    /// <summary>
    /// The file is downloaded from a remote URL and should be cached locally.
    /// The workspace strategy will download it if not present in the content cache.
    /// </summary>
    Download,

    /// <summary>
    /// The file is generated or modified at runtime by the content package.
    /// The workspace strategy will ensure the target location exists but won't source the file directly.
    /// </summary>
    Generated,
}
