using System.Collections.Generic;
using System.Text.Json.Serialization;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Defines a content provider loaded from external JSON configuration.
/// This model supports both "static" publishers (like GeneralsOnline, CommunityOutpost)
/// and "dynamic" author-based publishers (like GitHub topics, ModDB authors).
/// </summary>
public class ProviderDefinition
{
    /// <summary>
    /// Gets or sets the unique provider identifier (e.g., "generalsonline", "communityoutpost", "github").
    /// </summary>
    [JsonPropertyName("providerId")]
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the publisher type used in manifest IDs (e.g., "generalsonline", "communityoutpost").
    /// </summary>
    [JsonPropertyName("publisherType")]
    public string PublisherType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name shown in the UI (e.g., "Generals Online", "Community Outpost").
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a description of what this provider offers.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the icon color for UI display (hex color like "#4CAF50").
    /// </summary>
    [JsonPropertyName("iconColor")]
    public string IconColor { get; set; } = "#808080";

    /// <summary>
    /// Gets or sets the icon URL for the provider.
    /// </summary>
    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

    /// <summary>
    /// Gets or sets the provider type that determines discovery/resolution behavior.
    /// </summary>
    [JsonPropertyName("providerType")]
    public ProviderType ProviderType { get; set; } = ProviderType.Static;

    /// <summary>
    /// Gets or sets the catalog format used by this provider.
    /// Determines which parser to use for discovery (e.g., "genpatcher-dat", "github-releases", "json-api").
    /// </summary>
    [JsonPropertyName("catalogFormat")]
    public string CatalogFormat { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the endpoints configuration for this provider.
    /// </summary>
    [JsonPropertyName("endpoints")]
    public ProviderEndpoints Endpoints { get; set; } = new();

    /// <summary>
    /// Gets or sets the discovery configuration (for author-based providers).
    /// </summary>
    [JsonPropertyName("discovery")]
    public DiscoveryConfiguration? Discovery { get; set; }

    /// <summary>
    /// Gets or sets the mirror preference order for downloads.
    /// </summary>
    [JsonPropertyName("mirrorPreference")]
    public List<string> MirrorPreference { get; set; } = new();

    /// <summary>
    /// Gets or sets default content tags applied to all content from this provider.
    /// </summary>
    [JsonPropertyName("defaultTags")]
    public List<string> DefaultTags { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether this provider is enabled by default.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the target game for content from this provider (if fixed).
    /// </summary>
    [JsonPropertyName("targetGame")]
    public GameType? TargetGame { get; set; }

    /// <summary>
    /// Gets or sets timeouts for this provider.
    /// </summary>
    [JsonPropertyName("timeouts")]
    public ProviderTimeouts Timeouts { get; set; } = new();
}

/// <summary>
/// Defines endpoint URLs for a provider.
/// </summary>
public class ProviderEndpoints
{
    /// <summary>
    /// Gets or sets the catalog/API URL for discovering content.
    /// </summary>
    [JsonPropertyName("catalogUrl")]
    public string? CatalogUrl { get; set; }

    /// <summary>
    /// Gets or sets the base URL for downloads.
    /// </summary>
    [JsonPropertyName("downloadBaseUrl")]
    public string? DownloadBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the website URL for attribution.
    /// </summary>
    [JsonPropertyName("websiteUrl")]
    public string? WebsiteUrl { get; set; }

    /// <summary>
    /// Gets or sets the support/contact URL.
    /// </summary>
    [JsonPropertyName("supportUrl")]
    public string? SupportUrl { get; set; }

    /// <summary>
    /// Gets or sets the latest version URL (for single-release providers).
    /// </summary>
    [JsonPropertyName("latestVersionUrl")]
    public string? LatestVersionUrl { get; set; }

    /// <summary>
    /// Gets or sets the manifest API URL (for JSON API providers).
    /// </summary>
    [JsonPropertyName("manifestApiUrl")]
    public string? ManifestApiUrl { get; set; }

    /// <summary>
    /// Gets or sets additional named endpoints as key-value pairs.
    /// Allows providers to define custom endpoints beyond the standard ones.
    /// </summary>
    [JsonPropertyName("custom")]
    public Dictionary<string, string> Custom { get; set; } = new();

    /// <summary>
    /// Gets or sets additional mirror base URLs.
    /// </summary>
    [JsonPropertyName("mirrors")]
    public List<MirrorEndpoint> Mirrors { get; set; } = new();

    /// <summary>
    /// Gets an endpoint URL by name, checking both standard properties and custom endpoints.
    /// </summary>
    /// <param name="name">The endpoint name (case-insensitive).</param>
    /// <returns>The endpoint URL or null if not found.</returns>
    public string? GetEndpoint(string name)
    {
        // Check standard endpoints first
        var result = name.ToLowerInvariant() switch
        {
            "catalogurl" or "catalog" => this.CatalogUrl,
            "downloadbaseurl" or "downloadbase" => this.DownloadBaseUrl,
            "websiteurl" or "website" => this.WebsiteUrl,
            "supporturl" or "support" => this.SupportUrl,
            "latestversionurl" or "latestversion" => this.LatestVersionUrl,
            "manifestapiurl" or "manifestapi" => this.ManifestApiUrl,
            _ => null,
        };

        if (result != null)
        {
            return result;
        }

        // Check custom endpoints
        if (this.Custom.TryGetValue(name, out var customValue))
        {
            return customValue;
        }

        // Case-insensitive search in custom endpoints
        foreach (var kvp in this.Custom)
        {
            if (kvp.Key.Equals(name, System.StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return null;
    }
}

/// <summary>
/// Defines a mirror endpoint for downloads.
/// </summary>
public class MirrorEndpoint
{
    /// <summary>
    /// Gets or sets the mirror name for display.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mirror base URL.
    /// </summary>
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the priority (lower = higher priority).
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 100;
}

/// <summary>
/// Configuration for author-based content discovery.
/// Used by dynamic providers like GitHub, ModDB, CNCLabs.
/// </summary>
public class DiscoveryConfiguration
{
    /// <summary>
    /// Gets or sets the discovery method (e.g., "github-topic", "moddb-search", "cnclabs-api").
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets topics/tags to search for (for topic-based discovery).
    /// </summary>
    [JsonPropertyName("topics")]
    public List<string> Topics { get; set; } = new();

    /// <summary>
    /// Gets or sets the search query template.
    /// </summary>
    [JsonPropertyName("searchQueryTemplate")]
    public string? SearchQueryTemplate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether authors become dynamic publishers.
    /// </summary>
    [JsonPropertyName("authorsAsPublishers")]
    public bool AuthorsAsPublishers { get; set; } = true;

    /// <summary>
    /// Gets or sets the content type mapping rules.
    /// </summary>
    [JsonPropertyName("contentTypeRules")]
    public List<ContentTypeRule> ContentTypeRules { get; set; } = new();
}

/// <summary>
/// Rule for mapping discovered content to content types.
/// </summary>
public class ContentTypeRule
{
    /// <summary>
    /// Gets or sets the pattern to match (regex or simple match).
    /// </summary>
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the field to match against (name, description, tags).
    /// </summary>
    [JsonPropertyName("matchField")]
    public string MatchField { get; set; } = "name";

    /// <summary>
    /// Gets or sets the resulting content type.
    /// </summary>
    [JsonPropertyName("contentType")]
    public ContentType ContentType { get; set; } = ContentType.Mod;
}

/// <summary>
/// Timeout configuration for provider operations.
/// </summary>
public class ProviderTimeouts
{
    /// <summary>
    /// Gets or sets the catalog download timeout in seconds.
    /// </summary>
    [JsonPropertyName("catalogTimeoutSeconds")]
    public int CatalogTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the content download timeout in seconds.
    /// </summary>
    [JsonPropertyName("contentTimeoutSeconds")]
    public int ContentTimeoutSeconds { get; set; } = 300;
}

/// <summary>
/// Defines the type of content provider.
/// </summary>
public enum ProviderType
{
    /// <summary>
    /// Static provider with fixed publisher identity (GeneralsOnline, CommunityOutpost, TheSuperhackers).
    /// Discovers from a catalog/API, publishes under a single known identity.
    /// </summary>
    Static = 0,

    /// <summary>
    /// Dynamic provider where authors become publishers (GitHub, ModDB, CNCLabs).
    /// Discovers content from various authors, each author becomes a distinct publisher.
    /// </summary>
    Dynamic = 1,
}
