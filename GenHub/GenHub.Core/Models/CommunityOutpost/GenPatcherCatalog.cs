using System.Collections.Generic;

namespace GenHub.Core.Models.CommunityOutpost;

/// <summary>
/// Represents the parsed content catalog from dl.dat.
/// </summary>
public class GenPatcherCatalog
{
    /// <summary>
    /// Gets or sets the catalog version from the header line.
    /// </summary>
    public string CatalogVersion { get; set; } = "unknown";

    /// <summary>
    /// Gets or sets the list of content items.
    /// </summary>
    public List<GenPatcherContentItem> Items { get; set; } = [];
}