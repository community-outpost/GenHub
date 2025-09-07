namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the source of content files in a manifest, properly separating content origins from workspace placement strategies.
/// </summary>
public enum ContentSourceType
{
    /// <summary>
    /// Content source is unknown or undefined (default).
    /// </summary>
    Unknown,

    /// <summary>
    /// Content comes from the base game installation.
    /// </summary>
    BaseGame,

    /// <summary>
    /// Content is stored in the Content-Addressable Storage (CAS) system.
    /// </summary>
    ContentAddressable,

    /// <summary>
    /// Content is a local file on the filesystem.
    /// </summary>
    LocalFile,

    /// <summary>
    /// Content needs to be downloaded from a remote URL.
    /// </summary>
    RemoteDownload,

    /// <summary>
    /// Content is extracted from a package/archive file.
    /// </summary>
    ExtractedPackage,

    /// <summary>
    /// Content is a patch file that modifies existing content.
    /// </summary>
    PatchFile,
}
