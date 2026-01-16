using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Represents a publisher's definition file that contains identity,
/// catalog URLs, and self-update information.
/// This is the recommended subscription endpoint for users.
/// </summary>
public class PublisherDefinition
{
    /// <summary>
    /// Gets or sets the schema version for definition format compatibility.
    /// </summary>
    [JsonPropertyName("$schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Gets or sets the publisher identity and branding information.
    /// </summary>
    [JsonPropertyName("publisher")]
    public PublisherProfile Publisher { get; set; } = new();

    /// <summary>
    /// Gets or sets the collection of catalogs provided by this publisher.
    /// Each catalog can contain different types of content.
    /// </summary>
    [JsonPropertyName("catalogs")]
    public List<CatalogEntry> Catalogs { get; set; } = [];

    /// <summary>
    /// Gets or sets previous definition URLs for migration/tracking.
    /// </summary>
    [JsonPropertyName("previousDefinitionUrls")]
    public List<string> PreviousDefinitionUrls { get; set; } = [];

    /// <summary>
    /// Gets or sets the primary URL to the publisher's catalog JSON.
    /// Computed property for convenience - accesses Catalogs[0].
    /// </summary>
    [JsonPropertyName("catalogUrl")]
    public string CatalogUrl
    {
        get => Catalogs.Count > 0 ? Catalogs[0].Url : string.Empty;
        set
        {
            if (Catalogs.Count == 0)
            {
                Catalogs.Add(new CatalogEntry { Id = "default", Name = "Content" });
            }
            Catalogs[0].Url = value;
        }
    }

    /// <summary>
    /// Gets or sets alternate catalog URLs for redundancy.
    /// Computed property for convenience - accesses Catalogs[0].
    /// </summary>
    [JsonPropertyName("catalogMirrors")]
    public List<string> CatalogMirrors
    {
        get => Catalogs.Count > 0 ? Catalogs[0].Mirrors : [];
        set
        {
            if (Catalogs.Count == 0)
            {
                Catalogs.Add(new CatalogEntry { Id = "default", Name = "Content" });
            }
            Catalogs[0].Mirrors = value;
        }
    }

    /// <summary>
    /// Gets or sets the URL where this definition file is hosted.
    /// Used for self-updates (e.g. if the publisher moves their catalog hosting).
    /// </summary>
    [JsonPropertyName("definitionUrl")]
    public string? DefinitionUrl { get; set; }

    /// <summary>
    /// Gets or sets when this definition was last updated.
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets referrals to other publishers (cross-publisher discovery).
    /// </summary>
    [JsonPropertyName("referrals")]
    public List<PublisherReferral> Referrals { get; set; } = [];

    /// <summary>
    /// Gets or sets tags for publisher categorization.
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];
}
