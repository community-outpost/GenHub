namespace GenHub.Core.Constants;

/// <summary>
/// Constants for AODMaps (Age of Defense Maps) provider.
/// </summary>
public static class AODMapsConstants
{
    /// <summary>Gets the publisher type identifier for AODMaps.</summary>
    public const string PublisherType = "aodmaps";

    /// <summary>Gets the publisher prefix for AODMaps.</summary>
    public const string PublisherPrefix = "aodmaps";

    /// <summary>Gets the source name for AODMaps discoverer.</summary>
    public const string DiscovererSourceName = "AODMaps";

    /// <summary>Gets the discoverer description.</summary>
    public const string DiscovererDescription = "Age of Defense Maps";

    /// <summary>Gets the resolver ID for AODMaps.</summary>
    public const string ResolverId = "AODMaps";

    /// <summary>Gets the base URL for AODMaps.</summary>
    public const string BaseUrl = "https://aodmaps.com";

    /// <summary>Gets the players directory path.</summary>
    public const string PlayersPath = "/Players";

    /// <summary>Gets the URL pattern for player count pages.</summary>
    public const string PlayerPagePattern = "https://aodmaps.com/Players/{0}_players{1}.html";

    /// <summary>Gets the AOA maps URL.</summary>
    public const string AoaMapsUrl = "https://aodmaps.com/AOA/aoamaps.html";

    /// <summary>Gets the race maps URL.</summary>
    public const string RaceMapsUrl = "https://aodmaps.com/race/racemaps.html";

    /// <summary>Gets the air maps URL.</summary>
    public const string AirMapsUrl = "https://aodmaps.com/air/airmaps.html";

    /// <summary>Gets the Contra AOD URL.</summary>
    public const string ContraAodUrl = "https://aodmaps.com/ContraAOD/ContraAOD.html";

    /// <summary>Gets the compstomp page pattern.</summary>
    public const string CompstompPagePattern = "https://aodmaps.com/compstomp/compstompmaps{0}.html";

    /// <summary>Gets the map packs page pattern.</summary>
    public const string MapPacksPagePattern = "https://aodmaps.com/packs/Map_Packs{0}.html";

    /// <summary>Gets the new maps page pattern.</summary>
    public const string NewMapsPagePattern = "https://aodmaps.com/NEW/new{0}.html";

    /// <summary>Gets the map makers URL.</summary>
    public const string MapMakersUrl = "https://aodmaps.com/mapmakers/MM_P/MM.html";

    /// <summary>Gets the map maker page pattern.</summary>
    public const string MapMakerPagePattern = "https://aodmaps.com/mapmakers/MM_P/{0}/{0}.html";

    /// <summary>Gets the Bunny page override URL.</summary>
    public const string BunnyPageOverride = "https://aodmaps.com/mapmakers/MM_P/Bunny/bunnymaps2.html";

    /// <summary>Gets the search URL base.</summary>
    public const string SearchUrlBase = "https://aodmaps.com/search";

    /// <summary>Gets the maps URL base.</summary>
    public const string MapsUrlBase = "https://aodmaps.com/maps";

    /// <summary>Gets the details path marker.</summary>
    public const string DetailsPathMarker = "details";

    /// <summary>Gets the query string parameter for page.</summary>
    public const string PageQueryParam = "page";

    /// <summary>Gets the query string parameter for search term.</summary>
    public const string SearchQueryParam = "q";

    /// <summary>Gets the query string parameter for game type.</summary>
    public const string GameQueryParam = "game";

    /// <summary>Gets the query string parameter for content type.</summary>
    public const string TypeQueryParam = "type";

    /// <summary>Gets the query string parameter for tags.</summary>
    public const string TagsQueryParam = "tags";

    /// <summary>Gets the query string parameter for sort order.</summary>
    public const string SortQueryParam = "sort";

    /// <summary>Gets the query string parameter for map ID.</summary>
    public const string MapIdQueryParam = "id";

    /// <summary>Gets the default author name when not specified.</summary>
    public const string DefaultAuthorName = "Unknown";

    /// <summary>Gets the default map description template.</summary>
    public const string MapDescriptionTemplate = "Map from AODMaps";

    /// <summary>Gets the invalid absolute URI error message.</summary>
    public const string InvalidAbsoluteUri = "Invalid absolute URI";

    /// <summary>Gets the search term empty error message.</summary>
    public const string SearchTermEmptyErrorMessage = "Search term cannot be empty";

    /// <summary>Gets the discovery failure error template.</summary>
    public const string DiscoveryFailedErrorTemplate = "Discovery failed: {0}";

    /// <summary>Gets the discovery failure log message.</summary>
    public const string DiscoveryFailureLogMessage = "Failed to discover AODMaps content";

    /// <summary>Gets the map ID metadata key.</summary>
    public const string MapIdMetadataKey = "mapId";

    /// <summary>Gets the download URL metadata key.</summary>
    public const string DownloadUrlMetadataKey = "downloadUrl";

    /// <summary>Gets the direct download metadata key.</summary>
    public const string DirectDownloadMetadataKey = "directDownload";

    /// <summary>Gets the file size metadata key.</summary>
    public const string FileSizeMetadataKey = "fileSize";

    /// <summary>Gets the download count metadata key.</summary>
    public const string DownloadCountMetadataKey = "downloadCount";

    /// <summary>Gets the last updated metadata key.</summary>
    public const string LastUpdatedMetadataKey = "lastUpdated";

    /// <summary>Gets the icon URL metadata key.</summary>
    public const string IconUrlMetadataKey = "iconUrl";

    /// <summary>Gets the map ID format string.</summary>
    public const string MapIdFormat = "{0}-map-{1}";

    /// <summary>Gets the comma separator for tags.</summary>
    public const string CommaSeparator = ",";

    /// <summary>Gets the value attribute name.</summary>
    public const string ValueAttribute = "value";

    /// <summary>Gets the href attribute name.</summary>
    public const string HrefAttribute = "href";

    /// <summary>Gets the canonical href attribute name.</summary>
    public const string CanonicalHrefAttr = "data-href";

    // Map maker specific selectors

    /// <summary>Gets the map maker container selector.</summary>
    public const string MapMakerContainerSelector = "main.hoc.container.clear";

    /// <summary>Gets the map maker content selector.</summary>
    public const string MapMakerContentSelector = ".content";

    /// <summary>Gets the map maker title selector.</summary>
    public const string MapMakerTitleSelector = "h1";

    /// <summary>Gets the map maker info selector.</summary>
    public const string MapMakerInfoSelector = "p1"; // From user HTML: <p1>- Type: Survival ...</p1>

    /// <summary>Gets the map maker image selector.</summary>
    public const string MapMakerImageSelector = "img.imgl.borderedbox";

    /// <summary>Gets the map maker download selector.</summary>
    public const string MapMakerDownloadSelector = "a[download]";

    /// <summary>Gets the map maker download count script selector.</summary>
    public const string MapMakerDownloadCountScriptSelector = "script";

    /// <summary>Gets the list item selector.</summary>
    public const string ListItemSelector = ".map-item, .map-card, .map-entry";

    /// <summary>Gets the title selector.</summary>
    public const string TitleSelector = "h2.title, h3.title, .map-title";

    /// <summary>Gets the description selector.</summary>
    public const string DescriptionSelector = ".description, .map-description, p.description";

    /// <summary>Gets the author selector.</summary>
    public const string AuthorSelector = ".author, .map-author, .by-author";

    /// <summary>Gets the image selector.</summary>
    public const string ImageSelector = ".map-image, .map-thumbnail, img.thumbnail";

    /// <summary>Gets the download count selector.</summary>
    public const string DownloadCountSelector = ".download-count, .downloads";

    /// <summary>Gets the file size selector.</summary>
    public const string FileSizeSelector = ".file-size, .size";

    /// <summary>Gets the last updated selector.</summary>
    public const string LastUpdatedSelector = ".last-updated, .date, .updated";

    /// <summary>Gets the pagination selector.</summary>
    public const string PaginationSelector = ".pagination";

    /// <summary>Gets the next page selector.</summary>
    public const string NextPageSelector = ".next-page, .pagination-next, a[rel='next']";

    /// <summary>Gets the previous page selector.</summary>
    public const string PrevPageSelector = ".prev-page, .pagination-prev, a[rel='prev']";

    /// <summary>Gets the page number selector.</summary>
    public const string PageNumberSelector = ".page-number, .current-page";

    /// <summary>Gets the total pages selector.</summary>
    public const string TotalPagesSelector = ".total-pages, .page-count";

    /// <summary>Gets the content ID metadata key.</summary>
    public const string ContentIdMetadataKey = "contentId";

    // Selectors

    /// <summary>Gets the name selector for resource header.</summary>
    public const string NameSelector = ".resource-header h1";

    /// <summary>Gets the breadcrumb header selector.</summary>
    public const string BreadcrumbHeaderSelector = ".breadcrumbs";

    /// <summary>Gets the breadcrumb separator character.</summary>
    public const char BreadcrumbSeparator = '/';

    /// <summary>Gets the description selector for details page.</summary>
    public const string DetailsPageDescriptionSelector = "#description";

    /// <summary>Gets the author label selector.</summary>
    public const string AuthorLabelSelector = "strong";

    /// <summary>Gets the author label text.</summary>
    public const string AuthorLabelText = "Author:";

    /// <summary>Gets the file size label text.</summary>
    public const string FileSizeLabelText = "File Size:";

    /// <summary>Gets the max players label text.</summary>
    public const string MaxPlayersLabelText = "Players:";

    /// <summary>Gets the submitted label text.</summary>
    public const string SubmittedLabelText = "Submitted:";

    /// <summary>Gets the downloads label text.</summary>
    public const string DownloadsLabelText = "Downloads:";

    /// <summary>Gets the rating label text.</summary>
    public const string RatingLabelText = "Rating:";

    // Gallery page selectors

    /// <summary>Gets the gallery container selector.</summary>
    public const string GallerySelector = "#gallery ul.nospace.clear";

    /// <summary>Gets the gallery item selector.</summary>
    public const string GalleryItemSelector = "li";

    /// <summary>Gets the download link selector within gallery items.</summary>
    public const string GalleryDownloadLinkSelector = "a[href*='ccount/click.php']";

    /// <summary>Gets the Youtube link selector within gallery items.</summary>
    public const string GalleryYoutubeLinkSelector = "a[href*='youtu']";

    /// <summary>Gets the thumbnail image selector within gallery items.</summary>
    public const string GalleryThumbnailSelector = "img";

    /// <summary>Gets the map name selector within gallery items.</summary>
    public const string GalleryMapNameSelector = "span.name";

    /// <summary>Gets the download count script selector.</summary>
    public const string DownloadCountScriptSelector = "script";

    /// <summary>Gets the pagination navigation selector.</summary>
    public const string PaginationNavSelector = "nav.pagination";

    /// <summary>Gets the pagination link selector.</summary>
    public const string PaginationLinkSelector = "a";

    /// <summary>Gets the src attribute name.</summary>
    public const string SrcAttribute = "src";

    /// <summary>Gets the download attribute name.</summary>
    public const string DownloadAttribute = "download";

    /// <summary>Gets the ccount click path marker.</summary>
    public const string CcountClickPath = "ccount/click.php";

    /// <summary>Gets the ID query parameter for ccount.</summary>
    public const string CcountIdParam = "id";

    /// <summary>Gets the recognized map makers.</summary>
    public static readonly string[] RecognizedMapMakers =
    [
        "Bunny", "Evanz1987", "ILoveMixery", "lolo", "KoenigB",
        "ONE", "Pasha", "RDB", "Twinsen", "rebel",
        "Vocux", "wWw", "Bassie655", "SaMPoSa",
    ];
}
