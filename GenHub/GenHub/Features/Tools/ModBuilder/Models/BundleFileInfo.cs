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
    /// Gets the icon geometry path for the file type.
    /// </summary>
    public string IconData => IconKey switch
    {
        "IconImageFile" => "M8.5,13.5L11,16.5L14.5,12L19,18H5M21,19V5C21,3.89 20.1,3 19,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19Z",
        "IconArchiveFile" => "M14,17H12V15H10V13H12V11H10V9H12V7H14V9H12V11H14V13H12V15H14V17M19,3H5C3.89,3 3,3.89 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5C21,3.89 20.1,3 19,3Z",
        _ => "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z"
    };

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
