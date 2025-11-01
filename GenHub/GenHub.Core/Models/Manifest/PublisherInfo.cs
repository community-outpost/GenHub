using GenHub.Core.Constants;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Publisher information for content attribution and support.
/// </summary>
/// <remarks>
/// Publishers can be:
/// - Official platforms (EA/Steam detected from game installations)
/// - Community sources (GitHub repos, ModDB, HTTP endpoints)
/// - Custom sources (any IContentProvider implementation registered with ContentOrchestrator)
///
/// The PublisherType field contains the IContentProvider.SourceName for dynamic identification.
/// </remarks>
public class PublisherInfo
{
    /// <summary>
    /// Gets or sets the publisher name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the publisher type identifier (typically matches IContentProvider.SourceName).
    /// This is dynamically determined from the content provider that supplied the content.
    /// Examples: "GitHub", "ModDB", "Local Files", or any custom provider name.
    /// </summary>
    /// <remarks>
    /// For game installations, use <see cref="InstallationSourceConstants.FromInstallationType"/>
    /// to map GameInstallationType to a source string.
    /// </remarks>
    public string PublisherType { get; set; } = PublisherTypeConstants.Unknown;

    /// <summary>
    /// Gets or sets the publisher website.
    /// </summary>
    public string? Website { get; set; }

    /// <summary>
    /// Gets or sets the publisher support URL.
    /// </summary>
    public string? SupportUrl { get; set; }

    /// <summary>
    /// Gets or sets the publisher contact email.
    /// </summary>
    public string? ContactEmail { get; set; }

    /// <summary>
    /// Gets or sets the API endpoint URL for checking content updates.
    /// Used by GenHub to poll for new versions, weekly releases, or commit-based updates.
    /// Examples: GitHub API, custom REST endpoints, indexed manifest directories.
    /// </summary>
    public string? UpdateApiEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the content index URL for discovering available content.
    /// Points to a directory listing or API endpoint returning available manifest IDs.
    /// GenHub can poll this to discover new content from the publisher.
    /// </summary>
    public string? ContentIndexUrl { get; set; }

    /// <summary>
    /// Gets or sets the update check interval in hours.
    /// Determines how frequently GenHub should poll for updates from this publisher.
    /// Default: null (use system default). Set to 0 to disable automatic updates.
    /// Examples: 168 for weekly (Community-Outpost), 24 for daily, 1 for hourly (commit-based).
    /// </summary>
    public int? UpdateCheckIntervalHours { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this publisher supports incremental updates.
    /// When true, GenHub can download only changed files instead of full content packages.
    /// </summary>
    public bool SupportsIncrementalUpdates { get; set; } = false;

    /// <summary>
    /// Gets or sets the authentication method for accessing publisher content.
    /// Examples: "none", "api-key", "oauth", "github-token".
    /// </summary>
    public string? AuthenticationMethod { get; set; }
}
