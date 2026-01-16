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
    /// The hosting provider ID (e.g., "google_drive", "github").
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// The folder ID on the hosting provider.
    /// </summary>
    public string FolderId { get; set; } = string.Empty;

    /// <summary>
    /// URL to the publisher's folder on the hosting provider.
    /// </summary>
    public string FolderUrl { get; set; } = string.Empty;

    /// <summary>
    /// Hosting info for the publisher definition file.
    /// </summary>
    public HostedFileInfo? Definition { get; set; }

    /// <summary>
    /// Hosting info for each catalog file.
    /// </summary>
    public List<CatalogHostingInfo> Catalogs { get; set; } = [];

    /// <summary>
    /// Hosting info for each uploaded artifact.
    /// </summary>
    public List<ArtifactHostingInfo> Artifacts { get; set; } = [];

    /// <summary>
    /// When the project was last published.
    /// </summary>
    public DateTime LastPublished { get; set; }
}

/// <summary>
/// Base class for hosted file information.
/// </summary>
public class HostedFileInfo
{
    /// <summary>
    /// The remote file ID (e.g., Google Drive file ID).
    /// </summary>
    public string FileId { get; set; } = string.Empty;

    /// <summary>
    /// The public download URL for this file.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// When this file was last updated.
    /// </summary>
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// Hosting info for a catalog file.
/// </summary>
public class CatalogHostingInfo : HostedFileInfo
{
    /// <summary>
    /// The catalog ID this hosting info corresponds to.
    /// </summary>
    public string CatalogId { get; set; } = string.Empty;
}

/// <summary>
/// Hosting info for an uploaded artifact.
/// </summary>
public class ArtifactHostingInfo : HostedFileInfo
{
    /// <summary>
    /// The content ID this artifact belongs to.
    /// </summary>
    public string ContentId { get; set; } = string.Empty;

    /// <summary>
    /// The version of the content.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// The filename of the artifact.
    /// </summary>
    public string FileName { get; set; } = string.Empty;
}
