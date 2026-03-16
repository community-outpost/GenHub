using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Root model for a publisher's content catalog.
/// This JSON file is hosted by creators and fetched by GenHub to discover their content.
/// </summary>
public class PublisherCatalog
{
    /// <summary>
    /// Gets or sets the schema version for catalog format compatibility.
    /// </summary>
    [JsonPropertyName("$schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Gets or sets the publisher identity and branding information.
    /// </summary>
    [JsonPropertyName("publisher")]
    public PublisherProfile Publisher { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of content items available from this publisher.
    /// </summary>
    [JsonPropertyName("content")]
    public List<CatalogContentItem> Content { get; set; } = [];

    /// <summary>
    /// Gets or sets when the catalog was last updated.
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets an optional SHA256 signature for catalog integrity verification.
    /// </summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    /// <summary>
    /// Gets or sets referrals to other publishers (cross-publisher discovery).
    /// </summary>
    [JsonPropertyName("referrals")]
    public List<PublisherReferral> Referrals { get; set; } = [];
}
