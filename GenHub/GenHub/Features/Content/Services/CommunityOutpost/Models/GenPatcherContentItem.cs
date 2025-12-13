using System.Collections.Generic;

namespace GenHub.Features.Content.Services.CommunityOutpost.Models;

/// <summary>
/// Represents a content item parsed from the GenPatcher dl.dat file.
/// </summary>
public class GenPatcherContentItem
{
    /// <summary>
    /// Gets or sets the 4-character content code (e.g., "108e", "gent", "cbbs").
    /// </summary>
    public string ContentCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Gets or sets the list of available download mirrors.
    /// </summary>
    public List<GenPatcherMirror> Mirrors { get; set; } = new();
}

/// <summary>
/// Represents a download mirror for a GenPatcher content item.
/// </summary>
public class GenPatcherMirror
{
    /// <summary>
    /// Gets or sets the mirror name (e.g., "gentool.net", "legi.cc", "drive.google.com").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the download URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;
}
