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
    /// Filename for user subscription storage under application data.
    /// </summary>
    public const string SubscriptionFileName = "subscriptions.json";

    /// <summary>
    /// Sidebar / discoverer category for user-subscribed catalogs (vs built-in static/dynamic).
    /// </summary>
    public const string SubscribedPublisherCategory = "subscribed";

    /// <summary>
    /// Resolver / pipeline ID for the generic catalog pipeline (any GenHub-schema catalog).
    /// </summary>
    public const string GenericCatalogResolverId = "generic-catalog";

    /// <summary>
    /// Default catalog cache expiration in hours.
    /// </summary>
    public const int DefaultCatalogCacheExpirationHours = 24;

    /// <summary>
    /// Maximum catalog size in bytes (10 MB).
    /// </summary>
    public const long MaxCatalogSizeBytes = 10 * 1024 * 1024;

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
    public const string AnyPublisherId = "any";

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
    /// Known reference catalog URL for ModDB.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Known reference catalog endpoint")]
    public const string ModDbCatalogUrl = "https://api.moddb.com/catalog.json";

    /// <summary>
    /// Known reference catalog URL for CNC Labs.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Known reference catalog endpoint")]
    public const string CncLabsCatalogUrl = "https://github.com/CnC-Labs/mods-catalog/raw/main/catalog.json";
}
