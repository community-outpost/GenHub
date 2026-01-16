namespace GenHub.Core.Models.Providers;

/// <summary>
/// Represents a publisher option for quick selection in referrals and dependencies.
/// </summary>
public class PublisherReferralOption
{
    /// <summary>
    /// Gets or sets the unique publisher identifier.
    /// </summary>
    public string PublisherId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable publisher name.
    /// </summary>
    public string PublisherName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL to the publisher's catalog or definition.
    /// </summary>
    public string CatalogUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the publisher's avatar URL (optional).
    /// </summary>
    public string? AvatarUrl { get; set; }
}
