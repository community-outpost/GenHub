using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Represents a user's subscription to a publisher's catalog.
/// Stored locally in the user's application data.
/// </summary>
public class PublisherSubscription : ObservableObject
{
    private TrustLevel _trustLevel = TrustLevel.Untrusted;
    private bool _autoUpdate = true;
    private bool _notifyNewReleases = true;

    /// <summary>
    /// Gets or sets the unique publisher identifier.
    /// </summary>
    [JsonPropertyName("publisherId")]
    public string PublisherId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable publisher name.
    /// </summary>
    [JsonPropertyName("publisherName")]
    public string PublisherName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL to the publisher's catalog JSON.
    /// </summary>
    [JsonPropertyName("catalogUrl")]
    public string CatalogUrl { get; set; } = string.Empty;

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
    public bool AutoUpdate
    {
        get => _autoUpdate;
        set => SetProperty(ref _autoUpdate, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether to notify on new releases.
    /// </summary>
    [JsonPropertyName("notifyNewReleases")]
    public bool NotifyNewReleases
    {
        get => _notifyNewReleases;
        set => SetProperty(ref _notifyNewReleases, value);
    }

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

    /// <summary>
    /// Creates a defensive copy of this subscription.
    /// </summary>
    /// <returns>A new <see cref="PublisherSubscription"/> instance with identical values.</returns>
    public PublisherSubscription Clone()
    {
        return new PublisherSubscription
        {
            PublisherId = PublisherId,
            PublisherName = PublisherName,
            CatalogUrl = CatalogUrl,
            Added = Added,
            TrustLevel = TrustLevel,
            AutoUpdate = AutoUpdate,
            NotifyNewReleases = NotifyNewReleases,
            CachedCatalogHash = CachedCatalogHash,
            LastFetched = LastFetched,
            AvatarUrl = AvatarUrl,
        };
    }
}
