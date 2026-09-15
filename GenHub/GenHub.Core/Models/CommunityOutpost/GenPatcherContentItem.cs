using System.Collections.Generic;

namespace GenHub.Core.Models.CommunityOutpost;

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
    public List<GenPatcherMirror> Mirrors { get; set; } = [];
}