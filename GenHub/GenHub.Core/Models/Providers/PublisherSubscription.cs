using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// A user's saved follow of a third-party content source, persisted in <c>subscriptions.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not the same as</b> <c>GenHub.Core.Models.Content.PublisherSubscription</c>, which stores
/// update-notification preferences for built-in publishers inside user settings.
/// </para>
/// <para>
/// <b>Catalog-direct (current):</b> <see cref="CatalogUrl"/> points at a hosted GenHub
/// <see cref="PublisherCatalog"/> JSON. Downloads uses the generic catalog pipeline to browse
/// and install that content — no GenHub code change per creator.
/// </para>
/// <para>
/// <b>Provider Definition (Publisher Studio, forthcoming):</b> when <see cref="DefinitionUrl"/>
/// is set, GenHub will fetch publisher metadata and resolve catalog endpoint(s) from the
/// definition (stable subscribe link; catalogs can move). <see cref="CatalogUrl"/> may then be
/// a resolved/cached endpoint rather than the share link itself.
/// </para>
/// </remarks>
public class PublisherSubscription : ObservableObject
{
    private TrustLevel _trustLevel = TrustLevel.Untrusted;

    /// <summary>
    /// Gets or sets the unique publisher identifier (from catalog / definition).
    /// </summary>
    [JsonPropertyName("publisherId")]
    public string PublisherId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable publisher name.
    /// </summary>
    [JsonPropertyName("publisherName")]
    public string PublisherName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL used to fetch the publisher's content catalog JSON.
    /// </summary>
    /// <remarks>
    /// For catalog-direct subscriptions this is the shared <c>genhub://subscribe?url=...</c> target.
    /// For definition-based subscriptions this is the catalog endpoint resolved from the definition.
    /// </remarks>
    [JsonPropertyName("catalogUrl")]
    public string CatalogUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional URL to a Provider Definition (publisher metadata + catalog endpoints).
    /// </summary>
    /// <remarks>
    /// Null for catalog-direct subscriptions. When Publisher Studio ships shareable definitions,
    /// this becomes the primary subscribe target; catalogs are discovered from the definition.
    /// </remarks>
    [JsonPropertyName("definitionUrl")]
    public string? DefinitionUrl { get; set; }

    /// <summary>
    /// Gets or sets when the subscription was added.
    /// </summary>
    [JsonPropertyName("added")]
    public DateTime Added { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the trust level for this publisher.
    /// </summary>
    [JsonPropertyName("trustLevel")]
    public TrustLevel TrustLevel
    {
        get => _trustLevel;
        set => SetProperty(ref _trustLevel, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether to auto-update content from this publisher.
    /// </summary>
    [JsonPropertyName("autoUpdate")]
    public bool AutoUpdate { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to notify on new releases.
    /// </summary>
    [JsonPropertyName("notifyNewReleases")]
    public bool NotifyNewReleases { get; set; } = true;

    /// <summary>
    /// Gets or sets the cached catalog hash for change detection.
    /// </summary>
    [JsonPropertyName("cachedCatalogHash")]
    public string? CachedCatalogHash { get; set; }

    /// <summary>
    /// Gets or sets when the catalog was last fetched.
    /// </summary>
    [JsonPropertyName("lastFetched")]
    public DateTime? LastFetched { get; set; }

    /// <summary>
    /// Gets or sets the publisher's avatar URL for sidebar display.
    /// </summary>
    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }
}
