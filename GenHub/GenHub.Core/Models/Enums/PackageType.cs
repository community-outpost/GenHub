namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the type of a content package.
/// </summary>
public enum PackageType : byte
{
    /// <summary>
    /// No package type specified / unknown.
    /// </summary>
    None,

    /// <summary>
    /// A standard ZIP archive.
    /// </summary>
    Zip,

    /// <summary>
    /// A tarball archive.
    /// </summary>
    Tar,

    /// <summary>
    /// A GZipped tarball archive.
    /// </summary>
    TarGz,

    /// <summary>
    /// A 7-Zip archive.
    /// </summary>
    SevenZip,

    /// <summary>
    /// A self-contained installer executable.
    /// </summary>
    Installer,
}