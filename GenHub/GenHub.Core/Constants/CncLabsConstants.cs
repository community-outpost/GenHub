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
    /// Base URL for CNC Labs maps search.
    /// </summary>
    public const string SearchMapsUrlBase = "https://www.cnclabs.com/maps/generals/";

    /// <summary>
    /// Base URL for CNC Labs mods search.
    /// </summary>
    public const string SearchModsUrlBase = "https://www.cnclabs.com/mods/generals/";

    /// <summary>
    /// Base URL for CNC Labs downloads (patches, skins, videos, etc.).
    /// </summary>
    public const string SearchDownloadsUrlBase = "https://www.cnclabs.com/downloads/generals/";

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
    public const string ResolverId = ContentSourceNames.CNCLabsResolverId;

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
    public const string DefaultAuthorName = GameClientConstants.UnknownVersion;

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

    /// <summary>
    /// Publisher prefix used in manifest IDs and publisher identifiers.
    /// </summary>
    public const string PublisherPrefix = "cnclabs";

    /// <summary>
    /// Publisher type identifier for CNCLabs content pipeline.
    /// </summary>
    public const string PublisherType = "cnclabs";

    /// <summary>
    /// Publisher ID for the CNC Labs service.
    /// </summary>
    public const string PublisherId = PublisherPrefix;

    /// <summary>
    /// Official CNC Labs website URL.
    /// </summary>
    public const string PublisherWebsite = "https://www.cnclabs.com";

    /// <summary>
    /// Publisher display name for UI elements.
    /// </summary>
    public const string PublisherName = "CNC Labs";

    /// <summary>
    /// Publisher logo source path for UI display.
    /// </summary>
    public const string LogoSource = "/Assets/Logos/cnclabs-logo.png";

    /// <summary>Short description for publisher card display.</summary>
    public const string ShortDescription = "Maps, mods, and community content from CNC Labs";

    /// <summary>
    /// Default filename for downloads when parsing fails.
    /// </summary>
    public const string DefaultDownloadFilename = "download.zip";

    /// <summary>
    /// Default name for CNC Labs content when title is missing.
    /// </summary>
    public const string DefaultContentName = "untitled";

    /// <summary>
    /// Manifest version for CNC Labs content. Always 0 per specification.
    /// </summary>
    public const int ManifestVersion = 0;

    /// <summary>
    /// Relative path for the Winamp skins list page.
    /// </summary>
    public const string WinampSkinsPagePath = "winamp-skins.aspx";

    /// <summary>
    /// Relative path for the modding and mapping tools list page.
    /// </summary>
    public const string ModdingMappingPagePath = "modding-and-mapping.aspx";

    /// <summary>
    /// Relative path for the general downloads list page.
    /// </summary>
    public const string DownloadsPagePath = "downloads.aspx";

    /// <summary>
    /// Relative path for the patches list page.
    /// </summary>
    public const string PatchesPagePath = "patches.aspx";

    /// <summary>
    /// Relative path for the screensavers list page.
    /// </summary>
    public const string ScreensaversPagePath = "screensavers.aspx";

    /// <summary>
    /// Relative path for the videos list page.
    /// </summary>
    public const string VideosPagePath = "videos.aspx";

    /// <summary>Relative path for the Zero Hour replays list page.</summary>
    public const string ZeroHourReplaysPagePath = "zerohour-replays.aspx";

    /// <summary>Version string used when version information is missing.</summary>
    public const string UnknownVersion = "unknown";

    /// <summary>Display name for the 'Any' player option.</summary>
    public const string PlayerOptionAny = "Any";

    /// <summary>Display name for the '1 Player' option.</summary>
    public const string PlayerOption1Player = "1 Player";

    /// <summary>Display name for the '2 Players' option.</summary>
    public const string PlayerOption2Players = "2 Players";

    /// <summary>Display name for the '3 Players' option.</summary>
    public const string PlayerOption3Players = "3 Players";

    /// <summary>Display name for the '4 Players' option.</summary>
    public const string PlayerOption4Players = "4 Players";

    /// <summary>Display name for the '5 Players' option.</summary>
    public const string PlayerOption5Players = "5 Players";

    /// <summary>Display name for the '6 Players' option.</summary>
    public const string PlayerOption6Players = "6 Players";

    /// <summary>Display name for Maps content type.</summary>
    public const string ContentTypeMaps = "Maps";

    /// <summary>Display name for Missions content type.</summary>
    public const string ContentTypeMissions = "Missions";

    /// <summary>Display name for Patches content type.</summary>
    public const string ContentTypePatches = "Patches";

    /// <summary>Display name for Tools content type.</summary>
    public const string ContentTypeTools = "Tools";

    /// <summary>Map tag: Cramped.</summary>
    public const string TagCramped = "Cramped";

    /// <summary>Map tag: Spacious.</summary>
    public const string TagSpacious = "Spacious";

    /// <summary>Map tag: Well-balanced.</summary>
    public const string TagWellBalanced = "Well-balanced";

    /// <summary>Map tag: Money Map.</summary>
    public const string TagMoneyMap = "Money Map";

    /// <summary>Map tag: Detailed.</summary>
    public const string TagDetailed = "Detailed";

    /// <summary>Map tag: Custom Scripted.</summary>
    public const string TagCustomScripted = "Custom Scripted";

    /// <summary>Map tag: Symmetric.</summary>
    public const string TagSymmetric = "Symmetric";

    /// <summary>Map tag: Art of Defense.</summary>
    public const string TagArtOfDefense = "Art of Defense";

    /// <summary>Map tag: Multiplayer-only.</summary>
    public const string TagMultiplayerOnly = "Multiplayer-only";

    /// <summary>Map tag: Asymmetric.</summary>
    public const string TagAsymmetric = "Asymmetric";

    /// <summary>Map tag: Noob-Friendly.</summary>
    public const string TagNoobFriendly = "Noob-Friendly";

    /// <summary>Map tag: Veteran Suitable.</summary>
    public const string TagVeteranSuitable = "Veteran Suitable";

    /// <summary>Map tag: Fun Map.</summary>
    public const string TagFunMap = "Fun Map";

    /// <summary>Map tag: Art of Attack.</summary>
    public const string TagArtOfAttack = "Art of Attack";

    /// <summary>Map tag: ShellMap.</summary>
    public const string TagShellMap = "ShellMap";

    /// <summary>Map tag: Ported-Mission To ZH.</summary>
    public const string TagPortedMissionToZH = "Ported-Mission To ZH";

    /// <summary>Map tag: Custom Coded.</summary>
    public const string TagCustomCoded = "Custom Coded";

    /// <summary>Map tag: Coop Mission.</summary>
    public const string TagCoopMission = "Coop Mission";

    /// <summary>
    /// Format for parsing release dates for CNC Labs (M/d/yyyy).
    /// </summary>
    public const string ReleaseDateFormat = "M/d/yyyy";

    /// <summary>
    /// Default tags for CNC Labs manifests.
    /// </summary>
    public static readonly string[] DefaultTags = ["cnclabs"];
}
