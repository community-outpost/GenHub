using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Represents a discoverable GenHub build or fork from a publisher catalog that a user can subscribe to for updates.
/// </summary>
public class CustomBuildSubscriptionItem
{
    /// <summary>
    /// Gets or sets the publisher ID that published this build.
    /// </summary>
    public string PublisherId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the publisher name.
    /// </summary>
    public string PublisherName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content item ID.
    /// </summary>
    public string ContentId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the build or fork.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of the build or fork.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the latest release version.
    /// </summary>
    public string? LatestVersion { get; set; }

    /// <summary>
    /// Gets or sets the release category (e.g., Release, Test, Dev, CustomFork).
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this build is a prerelease or test build.
    /// </summary>
    public bool IsPrerelease { get; set; }

    /// <summary>
    /// Gets or sets tags associated with the build.
    /// </summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the release date of the latest release.
    /// </summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the download URL or primary artifact URL if available.
    /// </summary>
    public string? DownloadUrl { get; set; }
}
