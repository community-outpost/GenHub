namespace GenHub.Core.Models.GitHub;

/// <summary>
/// Represents a GitHub release.
/// </summary>
public class GitHubRelease
{
    /// <summary>
    /// Gets or sets the release ID.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the release tag name.
    /// </summary>
    public string TagName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release body/description.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the author of the release.
    /// </summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the HTML URL for the release.
    /// </summary>
    public string HtmlUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this is a prerelease.
    /// </summary>
    public bool IsPrerelease { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a draft.
    /// </summary>
    public bool IsDraft { get; set; }

    /// <summary>
    /// Gets or sets the release creation date.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the release publication date.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>
    /// Gets or sets the release assets.
    /// </summary>
    public List<GitHubReleaseAsset> Assets { get; set; } = new List<GitHubReleaseAsset>();
}