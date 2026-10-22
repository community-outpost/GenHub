using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the Steam Workshop content pipeline (Generals and Zero Hour maps).
/// </summary>
[SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Centralized URI constants / mock demo paths")]
public static class SteamWorkshopConstants
{
    /// <summary>Gets the publisher type identifier for Steam Workshop.</summary>
    public const string PublisherType = PublisherTypeConstants.SteamWorkshop;

    /// <summary>Gets the publisher prefix used in manifest IDs.</summary>
    public const string PublisherPrefix = PublisherTypeConstants.SteamWorkshop;

    /// <summary>Gets the publisher ID used by the manifest factory.</summary>
    public const string PublisherId = PublisherTypeConstants.SteamWorkshop;

    /// <summary>Gets the source name for the Steam Workshop discoverer.</summary>
    public const string DiscovererSourceName = ContentSourceNames.SteamWorkshopDiscoverer;

    /// <summary>Gets the discoverer description.</summary>
    public const string DiscovererDescription = "Discovers Generals and Zero Hour maps from Steam Workshop";

    /// <summary>Gets the resolver ID for Steam Workshop.</summary>
    public const string ResolverId = ContentSourceNames.SteamWorkshopResolverId;

    /// <summary>Gets the source name for the Steam Workshop deliverer.</summary>
    public const string DelivererSourceName = ContentSourceNames.SteamWorkshopDeliverer;

    /// <summary>Gets the deliverer description.</summary>
    public const string DelivererDescription = "Delivers maps from locally subscribed Steam Workshop content";

    /// <summary>Gets the Steam Workshop AppID for Command and Conquer Generals (2024 Steam release).</summary>
    public const int GeneralsAppId = 2229870;

    /// <summary>Gets the Steam Workshop AppID for Command and Conquer Generals Zero Hour (2024 Steam release).</summary>
    public const int ZeroHourAppId = 2732960;

    /// <summary>Gets the Steam community workshop browse page base URL.</summary>
    public const string BrowseBaseUrl = "https://steamcommunity.com/workshop/browse/";

    /// <summary>Gets the Steam community shared-file details page base URL.</summary>
    public const string FileDetailsBaseUrl = "https://steamcommunity.com/sharedfiles/filedetails/";

    /// <summary>Gets the Steam community user profile base URL.</summary>
    public const string CreatorProfileBaseUrl = "https://steamcommunity.com/profiles/";

    /// <summary>Gets the Steam Web API published-file details endpoint.</summary>
    public const string GetPublishedFileDetailsEndpoint = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

    /// <summary>Gets the credential store provider ID for the connected Steam account.</summary>
    public const string SteamAccountCredentialProviderId = "steam_account_auth";

    /// <summary>Gets the default host for the SteamPipe CDN.</summary>
    public const string SteamPipeCdnHost = "steampipe.akamaized.net";

    /// <summary>Gets the default port for the SteamPipe CDN.</summary>
    public const int SteamPipeCdnPort = 80;

    /// <summary>Gets the workshop browse section listing usable items.</summary>
    public const string BrowseSection = "readytouseitems";

    /// <summary>Gets the query string parameter for the application ID.</summary>
    public const string AppIdQueryParam = "appid";

    /// <summary>Gets the query string parameter for the free-text search.</summary>
    public const string SearchTextQueryParam = "searchtext";

    /// <summary>Gets the query string parameter for the browse sort order.</summary>
    public const string BrowseSortQueryParam = "browsesort";

    /// <summary>Gets the query string parameter mirroring the browse sort order.</summary>
    public const string ActualSortQueryParam = "actualsort";

    /// <summary>Gets the query string parameter for the browse section.</summary>
    public const string SectionQueryParam = "section";

    /// <summary>Gets the query string parameter for the 1-based page index on browse pages.</summary>
    public const string PageQueryParam = "p";

    /// <summary>Gets the query string parameter for the published file ID on details pages.</summary>
    public const string FileIdQueryParam = "id";

    /// <summary>Gets the query string parameter for the child published file ID on browse pages.</summary>
    public const string ChildFileIdQueryParam = "childpublishedfileid";

    /// <summary>Gets the browse query parameter carrying required tag filters.</summary>
    public const string RequiredTagsQueryParam = "requiredtags[]";

    /// <summary>Gets the metadata key carrying the creator avatar URL for display.</summary>
    public const string CreatorAvatarUrlMetadataKey = "creatorAvatarUrl";

    /// <summary>Gets the metadata key carrying the creator's Steam profile URL.</summary>
    public const string AuthorProfileUrlMetadataKey = "authorProfileUrl";

    /// <summary>Gets the default avatar image URL for Steam profiles without a custom avatar.</summary>
    public const string DefaultAvatarUrl = "https://avatars.fastly.steamstatic.com/fef49e7fa7e1997310d705b2a6158ff8dc1cdfeb_full.jpg";

    /// <summary>Gets the avatar CDN image URL format for Steam profile avatars.</summary>
    public const string AvatarUrlFormat = "https://avatars.fastly.steamstatic.com/{0}_full.jpg";

    /// <summary>Gets the maximum concurrent profile requests during anonymous browse avatar resolution.</summary>
    public const int MaxConcurrentProfileRequests = 4;

    /// <summary>Gets the per-profile fetch timeout in seconds during anonymous browse avatar resolution.</summary>
    public const int ProfileFetchTimeoutSeconds = 5;

    /// <summary>Gets the maximum number of cached creator avatar URLs.</summary>
    public const int MaxAvatarCacheEntries = 1000;

    /// <summary>Gets the Web API parameter carrying the item count for details lookups.</summary>
    public const string ItemCountParam = "itemcount";

    /// <summary>Gets the Web API parameter format for published file IDs in details lookups.</summary>
    public const string PublishedFileIdParamFormat = "publishedfileids[{0}]";

    /// <summary>Gets the number of items rendered per workshop browse page.</summary>
    public const int BrowsePageSize = 30;

    /// <summary>Gets the HTTP timeout in seconds for workshop requests.</summary>
    public const int HttpTimeoutSeconds = 30;

    /// <summary>Gets the sort value for trending items on browse pages.</summary>
    public const string SortTrend = "trend";

    /// <summary>Gets the sort value for most recent items on browse pages.</summary>
    public const string SortMostRecent = "mostrecent";

    /// <summary>Gets the sort value for last updated items on browse pages.</summary>
    public const string SortLastUpdated = "lastupdated";

    /// <summary>Gets the sort value for most subscribed items on browse pages.</summary>
    public const string SortMostSubscribed = "totaluniquesubscribers";

    /// <summary>Gets the sort value for top rated items on browse pages.</summary>
    public const string SortTopRated = "toprated";

    /// <summary>Gets the sort value for text search on browse pages.</summary>
    public const string SortTextSearch = "textsearch";

    /// <summary>Gets the default sort value.</summary>
    public const string DefaultSort = SortTrend;

    /// <summary>Gets the format for Steam Workshop content IDs (published file IDs).</summary>
    public const string ContentIdFormat = "steamworkshop.{0}";

    /// <summary>Gets the prefix for Steam Workshop content IDs.</summary>
    public const string ContentIdPrefix = "steamworkshop.";

    /// <summary>Gets the resolver metadata key for the published file ID.</summary>
    public const string PublishedFileIdMetadataKey = "publishedFileId";

    /// <summary>Gets the resolver metadata key for the workshop AppID.</summary>
    public const string AppIdMetadataKey = "workshopAppId";

    /// <summary>Gets the resolver metadata key for the creator Steam ID.</summary>
    public const string CreatorIdMetadataKey = "creatorSteamId";

    /// <summary>Gets the resolver metadata key indicating whether the description is a placeholder.</summary>
    public const string PlaceholderDescriptionMetadataKey = "placeholderDescription";

    /// <summary>Gets the default author name when the creator cannot be resolved.</summary>
    public const string DefaultAuthorName = "Steam Community";

    /// <summary>Gets the template description used before resolution.</summary>
    public const string MapDescriptionTemplate = "Map from Steam Workshop - full details available after resolution";

    /// <summary>Gets the fallback content name when slugification yields nothing.</summary>
    public const string DefaultContentName = "workshop-map";

    /// <summary>Gets the default filename used when no local file name can be determined.</summary>
    public const string DefaultDownloadFilename = "workshop-content.zip";

    /// <summary>Gets the release date format used for manifest versions.</summary>
    public const string ReleaseDateFormat = "yyyyMMdd";

    /// <summary>Gets the error message for a null search query.</summary>
    public const string QueryNullErrorMessage = "Query cannot be null";

    /// <summary>Gets the template for discovery failure errors.</summary>
    public const string DiscoveryFailedErrorTemplate = "Discovery failed: {0}";

    /// <summary>Gets the log message for discovery failures.</summary>
    public const string DiscoveryFailureLogMessage = "Failed to discover maps from Steam Workshop";

    /// <summary>Gets the canonical co-op tag value.</summary>
    public const string CoopTag = "Co-op";

    /// <summary>Gets the workshop directory name inside steamapps.</summary>
    public const string WorkshopDirectoryName = "workshop";

    /// <summary>Gets the workshop content directory name holding subscribed items.</summary>
    public const string WorkshopContentDirectoryName = "content";

    /// <summary>Gets the scheme prefix for opening workshop pages in the Steam client.</summary>
    public const string SteamUrlPrefix = "steam://url/CommunityFilePage/";

    /// <summary>Gets the file extension for Generals and Zero Hour map files.</summary>
    public const string MapFileExtension = ".map";

    /// <summary>Gets the staging file extension for atomic delivery writes.</summary>
    public const string StagingFileExtension = ".tmp";

    /// <summary>Gets the file extension used for backup files during delivery rollback.</summary>
    public const string BackupFileExtension = ".bak";

    /// <summary>Gets the request language pinning English workshop page markers for parsing.</summary>
    public const string BrowseRequestLanguage = "en";

    /// <summary>Gets the details-page stat label for the file size.</summary>
    public const string DetailsStatFileSize = "file size";

    /// <summary>Gets the details-page stat label for the posted date.</summary>
    public const string DetailsStatPosted = "posted";

    /// <summary>Gets the details-page stat label for the updated date.</summary>
    public const string DetailsStatUpdated = "updated";

    /// <summary>Gets the details-page stat label fragment for subscriber counts.</summary>
    public const string DetailsStatSubscriberFragment = "subscriber";

    /// <summary>Gets the maximum gallery screenshots kept from a details page.</summary>
    public const int MaxGalleryScreenshots = 10;

    /// <summary>Gets the details-page class marker identifying the creator avatar block.</summary>
    public const string CreatorAvatarBlockMarker = "friendBlockAvatar";

    /// <summary>Gets the character window searched for the creator avatar after its block marker.</summary>
    public const int CreatorAvatarSearchWindow = 1500;

    /// <summary>Gets the image host fragment for Steam user-content gallery images.</summary>
    public const string GalleryImageHostFragment = "steamusercontent";

    /// <summary>Gets the Steam image CDN query parameters that request a resized variant.</summary>
    public static readonly IReadOnlyList<string> SteamImageResizeParams =
    [
        "imw",
        "imh",
        "ima",
        "impolicy",
        "imcolor",
        "letterbox",
    ];

    /// <summary>Gets the default tags for Steam Workshop manifests.</summary>
    public static readonly IReadOnlyList<string> DefaultTags = ["steamworkshop"];

    /// <summary>Gets the workshop tag filters offered in the downloads filter panel.</summary>
    public static readonly IReadOnlyList<string> WorkshopTagFilters =
    [
        "Single Player",
        "Multiplayer",
        CoopTag,
        "Art of Defense",
        "Casino",
        "Balanced",
        "Detailed",
        "Large",
        "Medium",
        "Small",
        "1v1",
        "2v2",
        "3v3",
        "4v4",
    ];

    /// <summary>Gets the workshop tag aliases normalized to their canonical filter value.</summary>
    public static readonly IReadOnlyDictionary<string, string> WorkshopTagAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["coop"] = CoopTag,
            ["co op"] = CoopTag,
            ["aod"] = "Art of Defense",
            ["art of defence"] = "Art of Defense",
            ["1v1"] = "1v1",
            ["2v2"] = "2v2",
            ["3v3"] = "3v3",
            ["4v4"] = "4v4",
        };

    /// <summary>Gets the workshop tags implying a player-count badge value.</summary>
    public static readonly IReadOnlyDictionary<string, int> WorkshopTagPlayerCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Single Player"] = 1,
            ["1v1"] = 2,
            ["2v2"] = 4,
            ["3v3"] = 6,
            ["4v4"] = 8,
        };

    /// <summary>Gets the workshop tags classifying an item as a mission.</summary>
    public static readonly IReadOnlyList<string> MissionTags =
    [
        CoopTag,
    ];

    /// <summary>Gets the title keywords classifying a workshop item as a mission.</summary>
    public static readonly IReadOnlyList<string> MissionKeywords =
    [
        "mission",
        "operation",
        "campaign",
        "singleplayer",
        "single player",
        "single-player",
        "coop",
        "co-op",
        "challenge",
    ];

    /// <summary>Gets the title keywords classifying a workshop item as a map pack.</summary>
    public static readonly IReadOnlyList<string> MapPackKeywords =
    [
        "map pack",
        "mappack",
        "map-pack",
        "map collection",
        "maps collection",
    ];
}
