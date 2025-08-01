namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the type of a content package.
/// </summary>
public enum PackageType : byte
{
    /// <summary>
    /// No package type specified / unknown.
    /// </summary>
    None = 0,

    /// <summary>
    /// A standard ZIP archive.
    /// </summary>
    Zip = 1,

    /// <summary>
    /// A tarball archive.
    /// </summary>
    Tar = 2,

    /// <summary>
    /// A GZipped tarball archive.
    /// </summary>
    TarGz = 3,

    /// <summary>
    /// A 7-Zip archive.
    /// </summary>
    SevenZip = 4,

    /// <summary>
    /// A self-contained installer executable.
    /// </summary>
    Installer = 5,
}
