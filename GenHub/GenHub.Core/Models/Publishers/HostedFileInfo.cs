using System;

namespace GenHub.Core.Models.Publishers;

/// <summary>
/// Base class for hosted file information.
/// </summary>
public class HostedFileInfo
{
    /// <summary>
    /// Gets or sets the remote file ID (e.g., Google Drive file ID).
    /// </summary>
    public string FileId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the public download URL for this file.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when this file was last updated.
    /// </summary>
    public DateTime LastUpdated { get; set; }
}
