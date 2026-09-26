using System;
using System.Collections.Generic;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the modular publisher-catalog system.
/// </summary>
/// <remarks>
/// Layering (see Publisher Studio architecture):
/// <list type="bullet">
/// <item>
/// <b>Provider Definition</b> — static publisher metadata + catalog endpoint(s)
/// (bundled <c>*.provider.json</c> today; user-hosted definitions via Publisher Studio later).
/// </item>
/// <item>
/// <b>Catalog</b> — dynamic content listing (<c>catalog.json</c> / remote endpoint), updated on each release.
/// </item>
/// <item>
/// <b>Artifacts</b> — downloadable files referenced by catalog releases.
/// </item>
/// </list>
/// Anyone can author a GenHub-schema catalog, host it, and share
/// <c>genhub://subscribe?url=...</c>. Discovery uses <see cref="GenericCatalogResolverId"/>
/// for catalog-direct subscriptions without per-publisher code.
/// </remarks>
public static class CatalogConstants
{
    /// <summary>
    /// Current catalog schema version.
    /// </summary>
    public const int CatalogSchemaVersion = 1;

    /// <summary>
    /// Current publisher definition schema version ($schemaVersion).
    /// </summary>
    public const int DefinitionSchemaVersion = 2;

    /// <summary>
    /// Maximum number of catalog mirror URLs attempted when fetching a catalog from a definition.
    /// </summary>
    public const int MaxCatalogMirrorAttempts = 3;

    /// <summary>
    /// Filename for user subscription storage under application data.
    /// </summary>
    public const string SubscriptionFileName = "subscriptions.json";

    /// <summary>
    /// Well-known publisher ID for TheSuperHackers.
    /// </summary>
    public const string SuperHackersPublisherId = SuperHackersConstants.PublisherId;

    /// <summary>
    /// Sidebar / discoverer category for user-subscribed catalogs (vs built-in static/dynamic).
    /// </summary>
    public const string SubscribedPublisherCategory = "subscribed";

    /// <summary>
    /// Synthetic catalog entry ID used when a subscription's active catalog URL no longer
    /// matches any catalog listed in the publisher definition. Keeps the current feed
    /// selectable in the catalog switcher instead of silently jumping to another catalog.
    /// </summary>
    public const string CurrentCatalogEntryId = "current";

    /// <summary>
    /// Default catalog identifier used when no specific catalog ID is selected.
    /// </summary>
    public const string DefaultCatalogId = "default";

    /// <summary>
    /// Resolver / pipeline ID for the generic catalog pipeline (any GenHub-schema catalog).
    /// </summary>
    public const string GenericCatalogResolverId = "generic-catalog";

    /// <summary>
    /// Source name of the generic catalog content provider that acquires content
    /// from any subscribed publisher catalog.
    /// </summary>
    public const string GenericCatalogProviderName = "GenericCatalog";

    /// <summary>
    /// Default catalog cache expiration in hours.
    /// </summary>
    public const int DefaultCatalogCacheExpirationHours = 24;

    /// <summary>
    /// Maximum catalog size in bytes (10 MB).
    /// </summary>
    public const long MaxCatalogSizeBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Named HTTP client for catalog downloads configured with SSRF protection and manual redirect validation.
    /// </summary>
    public const string CatalogHttpClientName = "CatalogHttpClient";

    /// <summary>
    /// Maximum allowed HTTP redirects when fetching remote catalogs.
    /// </summary>
    public const int MaxCatalogRedirects = 5;

    /// <summary>
    /// Cumulative timeout in seconds for fetching remote catalogs across all redirects.
    /// </summary>
    public const int DefaultCatalogTimeoutSeconds = 30;

    /// <summary>
    /// Default fallback filename for downloads when parsing or sanitizing fails.
    /// </summary>
    public const string DefaultDownloadFilename = "download.zip";

    /// <summary>
    /// Default discoverer source name when a subscription is unconfigured.
    /// </summary>
    public const string DefaultDiscovererSourceName = "Generic Catalog";

    /// <summary>
    /// Default discoverer description when a subscription is unconfigured.
    /// </summary>
    public const string DefaultDiscovererDescription = "Generic catalog-based content source";

    /// <summary>
    /// Notification title for a removed subscription.
    /// </summary>
    public const string SubscriptionRemovedNotificationTitle = "Subscription Removed";

    /// <summary>
    /// Notification title for refreshed catalogs.
    /// </summary>
    public const string CatalogsRefreshedNotificationTitle = "Catalogs Refreshed";

    /// <summary>
    /// Error notification title when loading subscriptions fails.
    /// </summary>
    public const string LoadSubscriptionsFailedTitle = "Failed to load subscriptions";

    /// <summary>
    /// Maximum number of entries allowed when extracting publisher catalog archives.
    /// </summary>
    public const int MaxZipEntryCount = 50_000;

    /// <summary>
    /// Maximum cumulative uncompressed size allowed when extracting publisher catalog archives (5 GB).
    /// </summary>
    public const long MaxZipUncompressedSizeBytes = 5L * 1024 * 1024 * 1024;

    /// <summary>
    /// Resolver metadata key for serialized publisher profile JSON.
    /// </summary>
    public const string PublisherProfileJsonMetadataKey = "publisherProfileJson";

    /// <summary>
    /// Resolver metadata key for serialized catalog item JSON.
    /// </summary>
    public const string CatalogItemJsonMetadataKey = "catalogItemJson";

    /// <summary>
    /// Resolver metadata key for serialized release JSON.
    /// </summary>
    public const string ReleaseJsonMetadataKey = "releaseJson";

    /// <summary>
    /// Resolver metadata key for the stable catalog content id (not the display name).
    /// </summary>
    public const string CatalogContentIdMetadataKey = "catalogContentId";

    /// <summary>
    /// Resolver metadata key for serialized bundle component descriptors.
    /// </summary>
    public const string BundleComponentsJsonMetadataKey = "bundleComponentsJson";

    /// <summary>
    /// Resolver metadata key for serialized publisher referrals JSON.
    /// </summary>
    public const string CatalogReferralsJsonMetadataKey = "catalogReferralsJson";

    /// <summary>
    /// Resolver metadata key for storing the selected variant ID.
    /// </summary>
    public const string SelectedVariantMetadataKey = "selectedVariant";

    /// <summary>
    /// Badge text for subscribed catalog publishers.
    /// </summary>
    public const string SubscribedCatalogPublisherBadge = "Subscribed Catalog Publisher";

    /// <summary>
    /// Badge text for official providers.
    /// </summary>
    public const string OfficialProviderBadge = "Official Provider";

    /// <summary>
    /// Base game content ID for Command &amp; Conquer Generals.
    /// </summary>
    public const string GeneralsContentId = "generals";

    /// <summary>
    /// Base game content ID for Command &amp; Conquer Generals: Zero Hour.
    /// </summary>
    public const string ZeroHourContentId = "zerohour";

    /// <summary>
    /// Publisher ID for Electronic Arts base game installations.
    /// </summary>
    public const string EaPublisherId = "ea";

    /// <summary>
    /// Publisher wildcard for base game installations satisfied by any publisher.
    /// </summary>
    public const string AnyPublisherId = ManifestConstants.AnyPublisherToken;

    /// <summary>
    /// Fallback publisher type for content without publisher metadata.
    /// </summary>
    public const string GenericPublisherType = "generic";

    /// <summary>
    /// Variant axis name for target game discrimination (Generals vs Zero Hour).
    /// </summary>
    public const string GameTypeVariantAxis = "game-type";

    /// <summary>
    /// Variant axis name for display resolution.
    /// </summary>
    public const string ResolutionVariantAxis = "resolution";

    /// <summary>
    /// Variant label for Command &amp; Conquer Generals.
    /// </summary>
    public const string GeneralsVariantLabel = "Generals";

    /// <summary>
    /// Variant label for Command &amp; Conquer Generals: Zero Hour.
    /// </summary>
    public const string ZeroHourVariantLabel = "Zero Hour";

    /// <summary>
    /// Compact variant label for Command &amp; Conquer Generals: Zero Hour (without spaces).
    /// </summary>
    public const string ZeroHourCompactVariantLabel = "ZeroHour";

    /// <summary>
    /// Version constraint keyword indicating the latest available release.
    /// </summary>
    public const string LatestVersionToken = "latest";

    /// <summary>
    /// Minimum year recognized for date-based versions (YYYYMMDD or YYYY.MM.DD).
    /// </summary>
    public const int MinDateVersionYear = 1990;

    /// <summary>
    /// Maximum year recognized for date-based versions (YYYYMMDD or YYYY.MM.DD).
    /// </summary>
    public const int MaxDateVersionYear = 2100;

    /// <summary>
    /// Standard 1080p resolution variant label.
    /// </summary>
    public const string Resolution1080pLabel = "1080p";

    /// <summary>
    /// Standard 1920x1080 resolution variant label.
    /// </summary>
    public const string Resolution1920x1080Label = "1920x1080";

    /// <summary>
    /// Known reference catalog URL for ModDB.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Known reference catalog endpoint")]
    public const string ModDbCatalogUrl = "https://api.moddb.com/catalog.json";

    /// <summary>
    /// Known reference catalog URL for CNC Labs.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Known reference catalog endpoint")]
    public const string CncLabsCatalogUrl = "https://github.com/CnC-Labs/mods-catalog/raw/main/catalog.json";

    /// <summary>
    /// Known direct downloads catalog URL for CNC Labs.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Known reference catalog endpoint")]
    public const string CncLabsDownloadsCatalogUrl = "https://www.cnclabs.com/downloads/catalog.json";

    /// <summary>
    /// Known catalog URL for GeneralsOnline.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Known reference catalog endpoint")]
    public const string GeneralsOnlineCatalogUrl = "https://cdn.playgenerals.online/catalog.json";

    /// <summary>
    /// Known catalog URL for Community Outpost.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Known reference catalog endpoint")]
    public const string CommunityOutpostCatalogUrl = "https://raw.githubusercontent.com/community-outpost/genhub-catalog/main/catalog.json";

    /// <summary>
    /// Status badge color for an unreleased or unpublished catalog (#6B7280).
    /// </summary>
    public const string CatalogStatusNotPublishedColor = "#6B7280";

    /// <summary>
    /// Status badge color for a catalog with pending changes (#F59E0B).
    /// </summary>
    public const string CatalogStatusPendingColor = "#F59E0B";

    /// <summary>
    /// Status badge color for an up-to-date published catalog (#10B981).
    /// </summary>
    public const string CatalogStatusPublishedColor = "#10B981";

    /// <summary>
    /// Well-known upstream sync provider identifiers.
    /// </summary>
    public static class UpstreamProviders
    {
        /// <summary>
        /// TheSuperHackers dynamic releases provider.
        /// </summary>
        public const string TheSuperHackers = "TheSuperHackers";

        /// <summary>
        /// GeneralsOnline ladder releases provider.
        /// </summary>
        public const string GeneralsOnline = "GeneralsOnline";

        /// <summary>
        /// CommunityOutpost GenPatcher releases provider.
        /// </summary>
        public const string CommunityOutpost = "CommunityOutpost";

        /// <summary>
        /// Generic GitHub Releases provider.
        /// </summary>
        public const string GitHubReleases = "GitHubReleases";

        /// <summary>
        /// Normalizes provider aliases to canonical upstream provider identifiers.
        /// </summary>
        /// <param name="provider">The provider name or alias to normalize.</param>
        /// <returns>The canonical provider identifier, or <c>null</c> if unsupported.</returns>
        public static string? Normalize(string? provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                return null;
            }

            if (string.Equals(provider, TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(provider, SuperHackersConstants.PublisherId, StringComparison.OrdinalIgnoreCase))
            {
                return TheSuperHackers;
            }

            if (string.Equals(provider, GeneralsOnline, StringComparison.OrdinalIgnoreCase))
            {
                return GeneralsOnline;
            }

            if (string.Equals(provider, CommunityOutpost, StringComparison.OrdinalIgnoreCase))
            {
                return CommunityOutpost;
            }

            if (string.Equals(provider, GitHubReleases, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(provider, PublisherTypeConstants.GitHub, StringComparison.OrdinalIgnoreCase))
            {
                return GitHubReleases;
            }

            return null;
        }

        /// <summary>
        /// Determines whether the specified provider name represents a supported upstream provider.
        /// </summary>
        /// <param name="provider">The provider name to check.</param>
        /// <returns><c>true</c> if supported; otherwise <c>false</c>.</returns>
        public static bool IsSupported(string? provider) => Normalize(provider) != null;
    }
}
