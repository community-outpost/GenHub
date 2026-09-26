using GenHub.Core.Constants;
using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// An in-memory window definition (.wnd) document.
/// </summary>
public sealed class WndDocument
{
    /// <summary>
    /// Gets or sets the file format version.
    /// </summary>
    public string FileVersion { get; set; } = WndConstants.File.KnownVersion;

    /// <summary>
    /// Gets or sets the source path this document was loaded from, when known.
    /// </summary>
    public string? SourcePath { get; set; }

    /// <summary>
    /// Gets the ordered layout block properties.
    /// </summary>
    public List<WndProperty> LayoutBlock { get; } = [];

    /// <summary>
    /// Gets the ordered top-level windows.
    /// </summary>
    public List<WndWindow> Windows { get; } = [];
}
