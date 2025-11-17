namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the source of content files in a manifest, properly separating content origins from workspace placement strategies.
/// </summary>
public enum ContentSourceType
{
    /// <summary>
    /// Content source is unknown or undefined (default).
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Content comes from the game installation.
    /// </summary>
    GameInstallation = 1,

    /// <summary>
    /// Content is stored in the Content-Addressable Storage (CAS) system.
    /// </summary>
    ContentAddressable = 2,

    /// <summary>
    /// Content is a local file on the filesystem.
    /// </summary>
    LocalFile = 3,

    /// <summary>
    /// Content needs to be downloaded from a remote URL.
    /// </summary>
    RemoteDownload = 4,

    /// <summary>
    /// Content is extracted from a package/archive file.
    /// </summary>
    ExtractedPackage = 5,

    /// <summary>
    /// Content is a patch file that modifies existing content.
    /// </summary>
    PatchFile = 6,
}