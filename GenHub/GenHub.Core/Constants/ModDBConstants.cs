namespace GenHub.Core.Constants;

/// <summary>
/// Constants specific to ModDB and its content pipeline components.
/// </summary>
public static class ModDBConstants
{
    // ===== Base URLs =====

    /// <summary>Base URL for ModDB website.</summary>
    public const string BaseUrl = "https://www.moddb.com";

    /// <summary>Base URL for C&amp;C Generals content.</summary>
    public const string GeneralsBaseUrl = "https://www.moddb.com/games/cc-generals";

    /// <summary>Base URL for C&amp;C Generals Zero Hour content.</summary>
    public const string ZeroHourBaseUrl = "https://www.moddb.com/games/cc-generals-zero-hour";

    // ===== Section URLs =====

    /// <summary>Mods section for Generals.</summary>
    public const string GeneralsModsUrl = "https://www.moddb.com/games/cc-generals/mods";

    /// <summary>Mods section for Zero Hour.</summary>
    public const string ZeroHourModsUrl = "https://www.moddb.com/games/cc-generals-zero-hour/mods";

    /// <summary>Downloads section for Generals.</summary>
    public const string GeneralsDownloadsUrl = "https://www.moddb.com/games/cc-generals/downloads";

    /// <summary>Downloads section for Zero Hour.</summary>
    public const string ZeroHourDownloadsUrl = "https://www.moddb.com/games/cc-generals-zero-hour/downloads";

    /// <summary>Addons section for Generals.</summary>
    public const string GeneralsAddonsUrl = "https://www.moddb.com/games/cc-generals/addons";

    /// <summary>Addons section for Zero Hour.</summary>
    public const string ZeroHourAddonsUrl = "https://www.moddb.com/games/cc-generals-zero-hour/addons";

    // ===== Publisher Info =====

    /// <summary>Publisher prefix for ModDB content (combined with author: moddb-{author}).</summary>
    public const string PublisherPrefix = "moddb";

    /// <summary>Publisher name for manifests.</summary>
    public const string PublisherName = "ModDB";

    /// <summary>ModDB website URL.</summary>
    public const string PublisherWebsite = "https://www.moddb.com";

    /// <summary>Manifest version for ModDB content. Always 0 per specification.</summary>
    public const int ManifestVersion = 0;

    // ===== Component Identifiers =====

    /// <summary>Source name for ModDB discoverer.</summary>
    public const string DiscovererSourceName = "ModDB";

    /// <summary>Description for ModDB discoverer.</summary>
    public const string DiscovererDescription = "Discovers mods, maps, and content from ModDB";

    /// <summary>Resolver ID for ModDB resolver.</summary>
    public const string ResolverId = "ModDB";

    /// <summary>Source name for ModDB deliverer.</summary>
    public const string DelivererSourceName = "ModDB Deliverer";

    /// <summary>Description for ModDB deliverer.</summary>
    public const string DelivererDescription = "Delivers ModDB content via download extraction and CAS storage";

    // ===== CSS Selectors (Listing Pages) =====

    /// <summary>Selector for content items in listing pages.</summary>
    public const string ListItemSelector = "div.row.rowcontent";

    /// <summary>Selector for content title link.</summary>
    public const string TitleLinkSelector = "a[href*='/mods/'], a[href*='/downloads/'], a[href*='/addons/']";

    /// <summary>Selector for content preview image.</summary>
    public const string PreviewImageSelector = "img.image";

    /// <summary>Selector for content summary/description.</summary>
    public const string SummarySelector = "p";

    /// <summary>Selector for pagination links.</summary>
    public const string PaginationSelector = "div.pagination a";

    // ===== CSS Selectors (Detail Pages) =====

    /// <summary>Selector for mod/content name on detail page.</summary>
    public const string DetailNameSelector = "h1[itemprop='name'], div.heading h4";

    /// <summary>Selector for mod description.</summary>
    public const string DetailDescriptionSelector = "div.description, div.body, p[itemprop='description']";

    /// <summary>Selector for mod author/creator.</summary>
    public const string DetailAuthorSelector = "a[href*='/members/'], span.author, div.creator a";

    /// <summary>Selector for download button/link.</summary>
    public const string DownloadButtonSelector = "a.buttondownload, a[href*='/downloads/start/'], a.downloadslink";

    /// <summary>Selector for image gallery.</summary>
    public const string ImageGallerySelector = "div.mediarow img, div.screenshot img";

    /// <summary>Selector for video embeds.</summary>
    public const string VideoSelector = "iframe[src*='youtube'], iframe[src*='vimeo']";

    /// <summary>Selector for articles section.</summary>
    public const string ArticlesSelector = "div.table.article";

    /// <summary>Selector for addons section.</summary>
    public const string AddonsSelector = "div.table.addons";

    /// <summary>Selector for file size information.</summary>
    public const string FileSizeSelector = "span.size, dd.size";

    /// <summary>Selector for category/type information.</summary>
    public const string CategorySelector = "span.category, dd.category, a.category";

    // ===== Query Parameters =====

    /// <summary>Query parameter for search keyword.</summary>
    public const string KeywordParam = "kw";

    /// <summary>Query parameter for category filter.</summary>
    public const string CategoryParam = "category";

    /// <summary>Query parameter for addon category filter.</summary>
    public const string AddonCategoryParam = "categoryaddon";

    /// <summary>Query parameter for timeframe filter.</summary>
    public const string TimeframeParam = "timeframe";

    /// <summary>Query parameter for licence filter.</summary>
    public const string LicenceParam = "licence";

    /// <summary>Query parameter for filter toggle.</summary>
    public const string FilterParam = "filter";

    /// <summary>Query parameter for sorting.</summary>
    public const string SortParam = "sort";

    /// <summary>Query parameter for page number.</summary>
    public const string PageParam = "page";

    /// <summary>Value for filter parameter when enabled.</summary>
    public const string FilterEnabledValue = "t";

    // ===== Category Values =====

    // Downloads Section - Releases
    /// <summary>Category: Full Version (Mod).</summary>
    public const string CategoryFullVersion = "2";

    /// <summary>Category: Demo (Mod).</summary>
    public const string CategoryDemo = "3";

    /// <summary>Category: Patch.</summary>
    public const string CategoryPatch = "4";

    /// <summary>Category: Script.</summary>
    public const string CategoryScript = "28";

    /// <summary>Category: Trainer.</summary>
    public const string CategoryTrainer = "29";

    // Downloads Section - Media
    /// <summary>Category: Trailer (Video).</summary>
    public const string CategoryTrailer = "7";

    /// <summary>Category: Movie (Video).</summary>
    public const string CategoryMovie = "8";

    /// <summary>Category: Music.</summary>
    public const string CategoryMusic = "9";

    /// <summary>Category: Audio.</summary>
    public const string CategoryAudio = "25";

    /// <summary>Category: Wallpaper.</summary>
    public const string CategoryWallpaper = "10";

    // Downloads Section - Tools
    /// <summary>Category: Archive Tool.</summary>
    public const string CategoryArchiveTool = "20";

    /// <summary>Category: Graphics Tool.</summary>
    public const string CategoryGraphicsTool = "13";

    /// <summary>Category: Mapping Tool.</summary>
    public const string CategoryMappingTool = "14";

    /// <summary>Category: Modelling Tool.</summary>
    public const string CategoryModellingTool = "15";

    /// <summary>Category: Installer Tool.</summary>
    public const string CategoryInstallerTool = "16";

    /// <summary>Category: Server Tool.</summary>
    public const string CategoryServerTool = "17";

    /// <summary>Category: IDE.</summary>
    public const string CategoryIDE = "18";

    /// <summary>Category: SDK.</summary>
    public const string CategorySDK = "19";

    /// <summary>Category: Source Code.</summary>
    public const string CategorySourceCode = "26";

    // Downloads Section - Miscellaneous
    /// <summary>Category: Guide.</summary>
    public const string CategoryGuide = "22";

    /// <summary>Category: Tutorial.</summary>
    public const string CategoryTutorial = "23";

    /// <summary>Category: Language Pack.</summary>
    public const string CategoryLanguagePack = "30";

    /// <summary>Category: Other.</summary>
    public const string CategoryOther = "24";

    // Addons Section - Maps
    /// <summary>Addon Category: Multiplayer Map.</summary>
    public const string AddonMultiplayerMap = "101";

    /// <summary>Addon Category: Singleplayer Map.</summary>
    public const string AddonSingleplayerMap = "102";

    /// <summary>Addon Category: Prefab.</summary>
    public const string AddonPrefab = "103";

    // Addons Section - Models
    /// <summary>Addon Category: Player Model.</summary>
    public const string AddonPlayerModel = "106";

    /// <summary>Addon Category: Prop Model.</summary>
    public const string AddonPropModel = "132";

    /// <summary>Addon Category: Vehicle Model.</summary>
    public const string AddonVehicleModel = "107";

    /// <summary>Addon Category: Weapon Model.</summary>
    public const string AddonWeaponModel = "108";

    /// <summary>Addon Category: Model Pack.</summary>
    public const string AddonModelPack = "131";

    // Addons Section - Skins
    /// <summary>Addon Category: Player Skin.</summary>
    public const string AddonPlayerSkin = "112";

    /// <summary>Addon Category: Prop Skin.</summary>
    public const string AddonPropSkin = "133";

    /// <summary>Addon Category: Vehicle Skin.</summary>
    public const string AddonVehicleSkin = "113";

    /// <summary>Addon Category: Weapon Skin.</summary>
    public const string AddonWeaponSkin = "114";

    /// <summary>Addon Category: Skin Pack.</summary>
    public const string AddonSkinPack = "134";

    // Addons Section - Audio
    /// <summary>Addon Category: Music.</summary>
    public const string AddonMusic = "117";

    /// <summary>Addon Category: Player Audio.</summary>
    public const string AddonPlayerAudio = "119";

    /// <summary>Addon Category: Language Sounds.</summary>
    public const string AddonLanguageSounds = "138";

    /// <summary>Addon Category: Audio Pack.</summary>
    public const string AddonAudioPack = "118";

    // Addons Section - Graphics
    /// <summary>Addon Category: Decal.</summary>
    public const string AddonDecal = "124";

    /// <summary>Addon Category: Effects GFX.</summary>
    public const string AddonEffectsGFX = "136";

    /// <summary>Addon Category: GUI.</summary>
    public const string AddonGUI = "125";

    /// <summary>Addon Category: HUD.</summary>
    public const string AddonHUD = "126";

    /// <summary>Addon Category: Sprite.</summary>
    public const string AddonSprite = "128";

    /// <summary>Addon Category: Texture.</summary>
    public const string AddonTexture = "129";

    // ===== Metadata Keys =====

    /// <summary>Metadata key for ModDB content ID.</summary>
    public const string ContentIdMetadataKey = "moddbId";

    /// <summary>Metadata key for ModDB content URL.</summary>
    public const string ContentUrlMetadataKey = "moddbUrl";

    /// <summary>Metadata key for section name.</summary>
    public const string SectionMetadataKey = "moddbSection";

    /// <summary>Metadata key for original category.</summary>
    public const string OriginalCategoryMetadataKey = "moddbCategory";

    // ===== Error Messages =====

    /// <summary>Error message for invalid URL.</summary>
    public const string InvalidUrlError = "Invalid ModDB URL provided";

    /// <summary>Error message for failed scraping.</summary>
    public const string ScrapingFailedError = "Failed to scrape ModDB page";

    /// <summary>Error message for missing download URL.</summary>
    public const string MissingDownloadUrlError = "No download URL found for content";

    /// <summary>Error message for unsupported category.</summary>
    public const string UnsupportedCategoryError = "Unsupported ModDB category";

    // ===== Default Values =====

    /// <summary>Default author name when author cannot be determined.</summary>
    public const string DefaultAuthor = "unknown";

    /// <summary>Default content name when title cannot be determined.</summary>
    public const string DefaultContentName = "untitled";

    /// <summary>Default description when none is available.</summary>
    public const string DefaultDescription = "Content from ModDB";

    // ===== Timeframe Values =====

    /// <summary>Timeframe: Past 24 hours.</summary>
    public const string TimeframePast24Hours = "1";

    /// <summary>Timeframe: Past week.</summary>
    public const string TimeframePastWeek = "2";

    /// <summary>Timeframe: Past month.</summary>
    public const string TimeframePastMonth = "3";

    /// <summary>Timeframe: Past year.</summary>
    public const string TimeframePastYear = "4";

    /// <summary>Timeframe: Year or older.</summary>
    public const string TimeframeYearOrOlder = "5";

    // ===== Content Tags =====

    /// <summary>Content tags for search and categorization.</summary>
    public static readonly string[] Tags = new[] { "ModDB", "Community", "Mods" };
}
