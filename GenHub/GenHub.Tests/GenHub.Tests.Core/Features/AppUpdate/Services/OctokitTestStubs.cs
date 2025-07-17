using System;
using System.Collections.Generic;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Minimal stub for ReleaseAsset.
/// </summary>
public class ReleaseAsset
{
    /// <summary>
    /// Gets or sets the asset name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the browser download URL.
    /// </summary>
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the asset size.
    /// </summary>
    public int Size { get; set; }
}

/// <summary>
/// Minimal stub for Release.
/// </summary>
public class Release
{
    /// <summary>
    /// Gets or sets the tag name.
    /// </summary>
    public string TagName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the assets.
    /// </summary>
    public IReadOnlyList<ReleaseAsset> Assets { get; set; } = new List<ReleaseAsset>();

    /// <summary>
    /// Gets or sets the published date.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>
    /// Gets or sets the release notes/body.
    /// </summary>
    public string Body { get; set; } = string.Empty;
}
