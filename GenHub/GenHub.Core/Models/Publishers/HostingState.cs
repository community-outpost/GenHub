using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Publishers;

/// <summary>
/// Persisted hosting state for a publisher project.
/// Stored as hosting_state.json alongside the project file.
/// </summary>
public class HostingState
{
    /// <summary>
    /// Gets or sets the hosting provider ID (e.g., "google_drive", "github").
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the folder ID on the hosting provider.
    /// </summary>
    public string FolderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL to the publisher's folder on the hosting provider.
    /// </summary>
    public string FolderUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the hosting info for the publisher definition file.
    /// </summary>
    public HostedFileInfo? Definition { get; set; }

    /// <summary>
    /// Gets or sets the hosting info for each catalog file.
    /// </summary>
    public List<CatalogHostingInfo> Catalogs { get; set; } = [];

    /// <summary>
    /// Gets or sets the hosting info for each uploaded artifact.
    /// </summary>
    public List<ArtifactHostingInfo> Artifacts { get; set; } = [];

    /// <summary>
    /// Gets or sets when the project was last published.
    /// </summary>
    public DateTime LastPublished { get; set; }

    /// <summary>
    /// Gets or sets the encrypted or stored authentication token for the hosting provider.
    /// </summary>
    public string? AuthToken { get; set; }

    /// <summary>
    /// Gets or sets the display name or username from the authenticated provider.
    /// </summary>
    public string? AuthDisplayName { get; set; }
}
