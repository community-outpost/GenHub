using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Publishers;

/// <summary>
/// Per-provider hosting states for a publisher project.
/// Stored as hosting_state.json alongside the project file. Projects may publish
/// different assets to different providers (e.g. definition on Dropbox, files on
/// Google Drive), so each provider keeps its own definition, catalog, folder,
/// and artifact records instead of clobbering a single shared state.
/// </summary>
public class PublisherHostingStates
{
    /// <summary>
    /// Gets or sets the schema version of this container.
    /// </summary>
    public int Version { get; set; } = 2;

    /// <summary>
    /// Gets or sets the hosting states keyed by provider ID (e.g. "dropbox", "google_drive").
    /// </summary>
    public Dictionary<string, HostingState> States { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
