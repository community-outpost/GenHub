using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Defines a content provider's static configuration (Tier 1 — publisher metadata + endpoints).
/// </summary>
/// <remarks>
/// <para>
/// Loaded today from bundled <c>Providers/*.provider.json</c> (GeneralsOnline, CommunityOutpost, …)
/// and drives built-in discoverers/parsers (catalog format, timeouts, version scheme).
/// </para>
/// <para>
/// This is <b>not</b> what <c>genhub://subscribe</c> persists today — subscribe stores a
/// <see cref="PublisherSubscription"/> to a <see cref="PublisherCatalog"/> URL. Publisher Studio
/// will later publish user-hosted definitions so users subscribe to a stable definition URL that
/// references catalog endpoint(s), keeping the same Downloads/generic-catalog pipeline for content.
/// </para>
/// </remarks>
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
    /// Gets or sets the version scheme used to order this provider's versions
    /// (e.g. "mmddyy-qfe", "iso-date", "numeric").
    /// </summary>
    [JsonPropertyName("versionScheme")]
    public string VersionScheme { get; set; } = VersionSchemeConstants.Default;

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

    /// <summary>
    /// Gets or sets the host platforms this provider's content can run on.
    /// <para>
    /// An empty list means the content is not tied to a platform. A provider whose content
    /// only runs on Windows, such as Generals Online behind Easy Anti-Cheat, lists
    /// <see cref="ExecutablePlatform.Windows"/> so it is not offered on macOS or Linux.
    /// </para>
    /// </summary>
    [JsonPropertyName("supportedPlatforms")]
    public List<ExecutablePlatform> SupportedPlatforms { get; set; } = [];

    /// <summary>
    /// Determines whether this provider's content can run on <paramref name="platform"/>.
    /// </summary>
    /// <param name="platform">The host platform to test.</param>
    /// <returns><c>true</c> when no platforms are declared or <paramref name="platform"/> is one of them.</returns>
    public bool SupportsPlatform(ExecutablePlatform platform)
    {
        return SupportedPlatforms is not { Count: > 0 } || SupportedPlatforms.Contains(platform);
    }

    /// <summary>
    /// Determines whether this provider's content can run on the current host.
    /// </summary>
    /// <returns><c>true</c> when the current host is supported.</returns>
    public bool SupportsHostPlatform() => SupportsPlatform(HostPlatform.Current);
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
