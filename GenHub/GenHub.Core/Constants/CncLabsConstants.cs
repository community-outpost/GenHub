namespace GenHub.Core.Constants;

/// <summary>
/// Constants specific to CNC Labs and its content pipeline components.
/// </summary>
public static class CNCLabsConstants
{
    /// <summary>
    /// CSS selector for search result containers on CNC Labs.
    /// </summary>
    public const string ResultSelector = "#search-results div.gsc-webResult.gsc-result";

    /// <summary>
    /// CSS selector for map title links within search results.
    /// </summary>
    public const string LinkSelector = "div.gs-webResult.gs-result div.gsc-thumbnail-inside div.gs-title a.gs-title";

    /// <summary>
    /// Attribute name for canonical href in CNC Labs search results.
    /// </summary>
    public const string CanonicalHrefAttr = "data-ctorig";

    /// <summary>
    /// Path marker for details pages on CNC Labs.
    /// </summary>
    public const string DetailsPathMarker = "details.aspx";

    /// <summary>
    /// Path marker for Generals maps on CNC Labs.
    /// </summary>
    public const string GeneralsPathMarker = "maps/generals";

    /// <summary>
    /// Default author name for CNC Labs content.
    /// </summary>
    public const string AuthorName = "C&C Labs";

    /// <summary>
    /// Base URL for CNC Labs search.
    /// </summary>
    public const string SearchUrlBase = "http://search.cnclabs.com/?cse=labs&q=";

    /// <summary>
    /// Base URL for CNC Labs search.
    /// </summary>
    public const string SearchMapsUrlBase = "https://www.cnclabs.com/maps/generals/";

    /// <summary>
    /// Source name for CNC Labs map discoverer.
    /// </summary>
    public const string SourceName = "CNC Labs Maps";

    /// <summary>
    /// Description for CNC Labs map discoverer.
    /// </summary>
    public const string Description = "Discovers maps from CNC Labs website";

    /// <summary>
    /// Resolver ID for CNC Labs maps.
    /// </summary>
    public const string ResolverId = "CNCLabsMap";

    /// <summary>
    /// Metadata key for map ID.
    /// </summary>
    public const string MapIdMetadataKey = "mapId";

    /// <summary>
    /// Error message for null query.
    /// </summary>
    public const string QueryNullErrorMessage = "Query cannot be null";

    /// <summary>
    /// Template for map description.
    /// </summary>
    public const string MapDescriptionTemplate = "Map from CNC Labs - full details available after resolution";

    /// <summary>
    /// Template for discovery failure error.
    /// </summary>
    public const string DiscoveryFailedErrorTemplate = "Discovery failed: {0}";

    /// <summary>
    /// Log message for discovery failure.
    /// </summary>
    public const string DiscoveryFailureLogMessage = "Failed to discover maps from CNC Labs";

    /// <summary>
    /// Format for CNC Labs map ID.
    /// </summary>
    public const string MapIdFormat = "cnclabs.map.{0}";

    /// <summary>
    /// Error message used when a provided URL string
    /// fails validation as a valid absolute URI.
    /// </summary>
    public const string InvalidAbsoluteUri = "The provided URL is not a valid absolute URI.";

    /// <summary>
    /// Attribute name for anchor element href values within search results.
    /// </summary>
    public const string HrefAttribute = "href";

    /// <summary>
    /// Query string parameter name for the ID.
    /// </summary>
    public const string QueryStringIdParameter = "id";

    /// <summary>
    /// CSS selector for a single downloadable item container on list pages.
    /// </summary>
    public const string ListItemSelector = "div.DownloadItem";

    /// <summary>
    /// CSS selector for the hidden input that carries the map's numeric File Id.
    /// </summary>
    public const string FileIdHiddenSelector = "input[type='hidden'][id$='FileIdField']";

    /// <summary>
    /// CSS selector for the anchor with the display name of the map.
    /// </summary>
    public const string DisplayNameAnchorSelector = "a.DisplayName";

    /// <summary>
    /// CSS selector for the element that contains the short description.
    /// </summary>
    public const string DescriptionSelector = "span[id$='DescriptionLabel']";

    /// <summary>
    /// CSS selector for bold labels inside the item description cell (used to locate the "Author:" label).
    /// </summary>
    public const string DescriptionCellStrongSelector = ".DescriptionCell strong";

    /// <summary>
    /// Literal text appearing in the UI preceding the author value.
    /// </summary>
    public const string AuthorLabelText = "Author:";

    /// <summary>
    /// Default author name used when an author cannot be parsed from the page.
    /// </summary>
    public const string DefaultAuthorName = "Unknown";

    /// <summary>
    /// Error message used when <c>ContentSearchQuery.SearchTerm</c> is null, empty, or whitespace.
    /// </summary>
    public const string SearchTermEmptyErrorMessage = "Search term must be non-empty.";

    /// <summary>
    /// Standard HTML attribute name used to read an element's value.
    /// </summary>
    public const string ValueAttribute = "value";

    /// <summary>
    /// Relative path for the Command &amp; Conquer: Generals maps list page.
    /// </summary>
    public const string MapsPagePath = "maps.aspx";

    /// <summary>
    /// Relative path for the Command &amp; Conquer: Generals missions list page.
    /// </summary>
    public const string MissionsPagePath = "missions.aspx";

    /// <summary>
    /// Relative path for the Command &amp; Conquer: Zero Hour maps list page.
    /// </summary>
    public const string ZeroHourMapsPagePath = "zerohour-maps.aspx";

    /// <summary>
    /// Relative path for the Command &amp; Conquer: Zero Hour missions list page.
    /// </summary>
    public const string ZeroHourMissionsPagePath = "zerohour-missions.aspx";

    /// <summary>
    /// Query parameter name for the page index (1-based).
    /// </summary>
    public const string PageQueryParam = "page";

    /// <summary>
    /// Query parameter name for filtering by number of players.
    /// </summary>
    public const string PlayersQueryParam = "players";

    /// <summary>
    /// Query parameter name for filtering by tags (comma-separated).
    /// </summary>
    public const string TagsQueryParam = "tags";

    /// <summary>
    /// Separator used when joining multiple tag values for the <see cref="TagsQueryParam"/> parameter.
    /// </summary>
    public const string CommaSeparator = ",";

    /// <summary>
    /// CSS selector for the map title on the details page.
    /// Example element: <c>&lt;span class="DisplayName"&gt;…&lt;/span&gt;</c>.
    /// </summary>
    public const string NameSelector = ".DownloadItem .DisplayName";

    /// <summary>
    /// CSS selector for the breadcrumb/header element that contains the full trail
    /// (used as a fallback source for the name if <see cref="NameSelector"/> is missing).
    /// </summary>
    public const string BreadcrumbHeaderSelector = "h1";

    /// <summary>
    /// The character used by the site to separate parts of the breadcrumb trail in the
    /// <see cref="BreadcrumbHeaderSelector"/> (we take the last segment as the map name).
    /// </summary>
    public const char BreadcrumbSeparator = '»';

    /// <summary>
    /// CSS selector for the description span whose id ends with <c>_DescriptionLabel</c>.
    /// This is the raw HTML we pass to <c>CNCLabsHelper.FormatDescription</c> to normalize.
    /// </summary>
    public const string DetailsPageDescriptionSelector = ".DownloadItem span[id$='DescriptionLabel']";

    /// <summary>
    /// CSS selector that finds all <c>&lt;strong&gt;</c> nodes within the description cell.
    /// We search these for a node whose text is equal to <see cref="AuthorLabelText"/>.
    /// </summary>
    public const string AuthorLabelContainerSelector = ".DownloadItem .DescriptionCell strong";

    /// <summary>
    /// Error message thrown when the caller provides a null/empty details page URL.
    /// </summary>
    public const string UrlRequiredMessage = "The details page URL must not be null or empty.";

    /// <summary>Zero-based index of the “category” item in the breadcrumb (Downloads=0, CNC Generals=1, Category=2, Name=3).</summary>
    public const int BreadcrumbCategoryIndex = 2;

    /// <summary>
    /// Query parameter name for sorting by selected value.
    /// </summary>
    public const string Sort = "sort";
}