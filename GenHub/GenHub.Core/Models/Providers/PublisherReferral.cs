using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Represents a referral to another publisher's catalog.
/// Enables cross-publisher discovery and recommendations.
/// </summary>
public class PublisherReferral
{
    /// <summary>
    /// Gets or sets the referred publisher's ID.
    /// </summary>
    [JsonPropertyName("publisherId")]
    public string PublisherId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL to the referred publisher's catalog.
    /// </summary>
    [JsonPropertyName("catalogUrl")]
    public string CatalogUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a descriptive note about the referral.
    /// </summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
