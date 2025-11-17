namespace GenHub.Core.Models.GitHub;

/// <summary>
/// Represents a GitHub release asset.
/// </summary>
public class GitHubReleaseAsset
{
    /// <summary>
    /// Gets or sets the asset ID.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the asset name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the asset label.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Gets or sets the content type.
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the asset size in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the download count.
    /// </summary>
    public int DownloadCount { get; set; }

    /// <summary>
    /// Gets or sets the browser download URL.
    /// </summary>
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the creation date.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last updated date.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}