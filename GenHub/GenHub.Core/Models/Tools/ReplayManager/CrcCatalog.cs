using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ReplayManager;

/// <summary>
/// Represents the root container for the replay game client CRC mapping catalog.
/// </summary>
public sealed record CrcCatalog
{
    /// <summary>
    /// Gets the schema version of the CRC mapping catalog.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// Gets the timestamp when the catalog was last updated.
    /// </summary>
    public DateTime? LastUpdated { get; init; }

    /// <summary>
    /// Gets the total number of mapping entries.
    /// </summary>
    public int TotalEntries { get; init; }

    /// <summary>
    /// Gets the list of CRC mappings.
    /// </summary>
    public IReadOnlyList<CrcMappingEntry> Mappings { get; init; } = [];
}
