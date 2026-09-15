using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Root model for a publisher's content catalog (Tier 2 — content listings).
/// </summary>
/// <remarks>
/// <para>
/// Creators host this JSON at any HTTPS URL (GitHub Releases/Pages, CDN, etc.). Users subscribe
/// via <c>genhub://subscribe?url=&lt;this-file&gt;</c>. Schema is validated by
/// <c>JsonPublisherCatalogParser</c>; discovery uses <c>GenericCatalogDiscoverer</c> so new
/// publishers need no GenHub code changes.
/// </para>
/// <para>
/// Distinct from <see cref="ProviderDefinition"/> (Tier 1 — static publisher config / catalog
/// endpoint). Bundled providers ship as <c>*.provider.json</c>; Publisher Studio will generate
/// user-hosted definitions that point at one or more catalogs of this shape.
/// </para>
/// </remarks>
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
    public DateTime LastUpdated { get; set; }

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

    /// <summary>
    /// Gets or sets custom tabs for content detail pages.
    /// Publishers can define custom tabs to display additional content.
    /// </summary>
    [JsonPropertyName("customTabs")]
    public List<CatalogTabDefinition> CustomTabs { get; set; } = [];
}
