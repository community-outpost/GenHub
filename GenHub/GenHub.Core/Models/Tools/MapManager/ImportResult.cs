using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.MapManager;

/// <summary>
/// Result of a map import operation.
/// </summary>
public sealed class ImportResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the import was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the number of files imported.
    /// </summary>
    public int FilesImported { get; set; }

    /// <summary>
    /// Gets or sets the list of error messages.
    /// </summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>
    /// Gets the list of imported map files.
    /// </summary>
    public List<MapFile> ImportedMaps { get; } = [];
}
