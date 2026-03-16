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
    public List<string> MirrorPreference { get; set; } = [];

    /// <summary>
    /// Gets or sets default content tags applied to all content from this provider.
    /// </summary>
    [JsonPropertyName("defaultTags")]
    public List<string> DefaultTags { get; set; } = [];

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
