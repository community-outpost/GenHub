using System;

namespace GenHub.Features.Tools.ModBuilder.Models;

/// <summary>
/// Represents a file in a bundle pack.
/// </summary>
public class BundleFileInfo
{
    /// <summary>
    /// Gets or sets the file name.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source path.
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the destination path within the bundle.
    /// </summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file type (TGA, DDS, PSD, CSF, INI, etc.).
    /// </summary>
    public string FileType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Gets or sets the file size formatted as a string.
    /// </summary>
    public string FileSizeFormatted { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the file is cached.
    /// </summary>
    public bool IsCached { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the file is modified.
    /// </summary>
    public bool IsModified { get; set; }

    /// <summary>
    /// Gets or sets the icon key for the file type.
    /// </summary>
    public string IconKey { get; set; } = "IconTextFile";

    /// <summary>
    /// Gets or sets the last modified date.
    /// </summary>
    public DateTime LastModified { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the file is selected.
    /// </summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// Gets or sets the display order in the bundle.
    /// </summary>
    public int Order { get; set; }
}
