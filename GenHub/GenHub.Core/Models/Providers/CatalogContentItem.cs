using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// A content item entry within a publisher catalog.
/// Represents a mod, map, addon, or other content with one or more releases.
/// </summary>
public class CatalogContentItem : ObservableObject
{
    private string _name = string.Empty;
    private string _description = string.Empty;
    private ContentType _contentType = ContentType.Mod;
    private ContentRichMetadata? _metadata;
    private string? _catalogIconUrl;
    private string? _publisherAvatarUrl;
    private List<ContentRelease> _releases = [];
    private List<string> _tags = [];
    private List<CatalogDependency> _bundledItems = [];
    private List<CatalogDependency> _addons = [];
    private List<ContentRelease> _addonReleases = [];

    /// <summary>
    /// Gets or sets the unique content identifier within this publisher's catalog.
    /// Combined with publisher ID to form the full manifest ID.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable content name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(EffectiveIconUrl));
            }
        }
    }

    /// <summary>
    /// Gets or sets the content description.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    /// <summary>
    /// Gets or sets the content type (Mod, Map, Addon, etc.).
    /// </summary>
    [JsonPropertyName("contentType")]
    public ContentType ContentType
    {
        get => _contentType;
        set => SetProperty(ref _contentType, value);
    }

    /// <summary>
    /// Gets or sets the target game for this content.
    /// </summary>
    [JsonPropertyName("targetGame")]
    public GameType TargetGame { get; set; } = GameType.ZeroHour;

    /// <summary>
    /// Gets or sets the entry point relative path for this content item (e.g., "generals.exe" or "game.dat").
    /// </summary>
    [JsonPropertyName("entryPoint")]
    public string? EntryPoint { get; set; }

    /// <summary>
    /// Gets or sets the list of releases (versions) for this content.
    /// </summary>
    [JsonPropertyName("releases")]
    public List<ContentRelease> Releases
    {
        get => _releases;
        set => _releases = value ?? [];
    }

    /// <summary>
    /// Gets or sets rich presentation metadata (banners, screenshots, videos).
    /// </summary>
    [JsonPropertyName("metadata")]
    public ContentRichMetadata? Metadata
    {
        get => _metadata;
        set
        {
            if (SetProperty(ref _metadata, value))
            {
                OnPropertyChanged(nameof(EffectiveIconUrl));
            }
        }
    }

    /// <summary>
    /// Gets or sets tags for categorization and search.
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags
    {
        get => _tags;
        set => _tags = value ?? [];
    }

    /// <summary>
    /// Gets or sets the list of items bundled in this content (for ContentBundle type).
    /// </summary>
    [JsonPropertyName("bundledItems")]
    public List<CatalogDependency> BundledItems
    {
        get => _bundledItems;
        set => _bundledItems = value ?? [];
    }

    /// <summary>
    /// Gets or sets explicit addons or addon dependencies associated with this content item.
    /// </summary>
    [JsonPropertyName("addons")]
    public List<CatalogDependency> Addons
    {
        get => _addons;
        set => _addons = value ?? [];
    }

    /// <summary>
    /// Gets or sets explicit addon releases (e.g., maps, patches, UI changes, AI changes) associated with this content item.
    /// Each addon release behaves like a release with its own version, artifacts, media, and dependencies.
    /// </summary>
    [JsonPropertyName("addonReleases")]
    public List<ContentRelease> AddonReleases
    {
        get => _addonReleases;
        set => _addonReleases = value ?? [];
    }

    /// <summary>
    /// Gets the combined number of legacy addons and addon releases for tab badges.
    /// </summary>
    [JsonIgnore]
    public int AddonCount => Addons.Count + AddonReleases.Count;

    /// <summary>
    /// Gets or sets the content ID that this addon extends (for Addon type).
    /// Format: "contentId" for same catalog, or "publisherId/contentId" for cross-publisher.
    /// </summary>
    [JsonPropertyName("extendsContentId")]
    public string? ExtendsContentId { get; set; }

    /// <summary>
    /// Gets or sets native pipeline / publisher type that must process this item after download.
    /// When omitted, generic catalog factory handles extraction.
    /// </summary>
    [JsonPropertyName("publisherType")]
    public string? PublisherType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this item is presented as a standalone card in catalog discovery.
    /// Default is <c>true</c>. When set to <c>false</c>, the item is retained as a catalog component for bundle composition but hidden from the main downloads grid.
    /// </summary>
    [JsonPropertyName("isStandalone")]
    public bool IsStandalone { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether this content item is featured.
    /// </summary>
    [JsonPropertyName("isFeatured")]
    public bool IsFeatured { get; set; }

    /// <summary>
    /// Gets or sets a custom badge label shown on featured cards.
    /// </summary>
    [JsonPropertyName("featuredBadge")]
    public string? FeaturedBadge { get; set; }

    /// <summary>
    /// Gets or sets upstream synchronization configuration for autonomous releases.
    /// </summary>
    [JsonPropertyName("upstreamSync")]
    public CatalogUpstreamSync? UpstreamSync { get; set; }

    /// <summary>
    /// Gets or sets the inherited catalog icon URL fallback for this content item.
    /// </summary>
    [JsonIgnore]
    public string? CatalogIconUrl
    {
        get => _catalogIconUrl;
        set
        {
            if (SetProperty(ref _catalogIconUrl, value))
            {
                OnPropertyChanged(nameof(EffectiveIconUrl));
            }
        }
    }

    /// <summary>
    /// Gets or sets the inherited publisher avatar URL fallback for this content item.
    /// </summary>
    [JsonIgnore]
    public string? PublisherAvatarUrl
    {
        get => _publisherAvatarUrl;
        set
        {
            if (SetProperty(ref _publisherAvatarUrl, value))
            {
                OnPropertyChanged(nameof(EffectiveIconUrl));
            }
        }
    }

    /// <summary>
    /// Gets the effective icon URL for this content item, falling back to catalog icon, publisher avatar, or deterministic placeholder.
    /// </summary>
    [JsonIgnore]
    public string EffectiveIconUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Metadata?.IconUrl) &&
                !ImageCacheConstants.IsPicsumUrl(Metadata.IconUrl))
            {
                return Metadata.IconUrl;
            }

            if ((Id != null && Id.Contains("dominator", StringComparison.OrdinalIgnoreCase)) ||
                (Name != null && Name.Contains("dominator", StringComparison.OrdinalIgnoreCase)))
            {
                return PublisherInfoConstants.Dominator.LogoSource;
            }

            if (!string.IsNullOrWhiteSpace(CatalogIconUrl) &&
                !ImageCacheConstants.IsPicsumUrl(CatalogIconUrl))
            {
                return CatalogIconUrl;
            }

            if (!string.IsNullOrWhiteSpace(PublisherAvatarUrl) &&
                !ImageCacheConstants.IsPicsumUrl(PublisherAvatarUrl))
            {
                return PublisherAvatarUrl;
            }

            if (!string.IsNullOrWhiteSpace(Metadata?.IconUrl))
            {
                return Metadata.IconUrl;
            }

            if (!string.IsNullOrWhiteSpace(CatalogIconUrl))
            {
                return CatalogIconUrl;
            }

            if (!string.IsNullOrWhiteSpace(PublisherAvatarUrl))
            {
                return PublisherAvatarUrl;
            }

            return ImageCacheConstants.GetPicsumUrl($"{Id}-icon", 128, 128);
        }
    }

    /// <summary>
    /// Explicitly notifies that the presentation properties (such as effective icon, name, or description) have changed.
    /// </summary>
    public void NotifyPresentationChanged()
    {
        OnPropertyChanged(nameof(EffectiveIconUrl));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
    }
}
