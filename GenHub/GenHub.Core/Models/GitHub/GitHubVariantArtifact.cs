namespace GenHub.Core.Models.GitHub;

/// <summary>
/// Represents a simplified variant artifact from a GitHub release.
/// Used for displaying multiple release variants in the Files tab.
/// </summary>
public class GitHubVariantArtifact
{
    /// <summary>
    /// Gets or sets the file name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name (e.g., "1080p", "English").
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the download URL.
    /// </summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    public long SizeInBytes { get; set; }

    /// <summary>
    /// Gets or sets the GitHub asset ID.
    /// </summary>
    public long AssetId { get; set; }
}
