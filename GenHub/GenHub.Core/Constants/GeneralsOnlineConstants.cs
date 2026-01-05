namespace GenHub.Core.Constants;

/// <summary>
/// Constants specific to Generals Online content provider and multiplayer service.
/// </summary>
public static class GeneralsOnlineConstants
{
    // ===== Content Metadata =====

    /// <summary>Publisher name for manifests.</summary>
    public const string PublisherName = "Generals Online Team";

    /// <summary>Content name for manifests.</summary>
    public const string ContentName = "Generals Online";

    /// <summary>Full content description.</summary>
    public const string Description = "Community-driven multiplayer service for C&C Generals Zero Hour. Features 60Hz tick rate, automatic updates, and improved stability.";

    /// <summary>Short content description.</summary>
    public const string ShortDescription = "Community-driven multiplayer service for C&C Generals Zero Hour";

    /// <summary>Content icon URL.</summary>
    public const string IconUrl = "https://www.playgenerals.online/logo.png";

    /// <summary>Website URL for Generals Online.</summary>
    public const string WebsiteUrl = "https://www.playgenerals.online";

    /// <summary>Support URL for Generals Online.</summary>
    public const string SupportUrl = "https://www.playgenerals.online/support";

    /// <summary>Download page URL for Generals Online.</summary>
    public const string DownloadPageUrl = "https://www.playgenerals.online/download";

    /// <summary>
    /// Cover image source path for UI display.
    /// </summary>
    public const string CoverSource = "/Assets/Images/usa-poster.png";

    /// <summary>
    /// Theme color for Generals Online content.
    /// </summary>
    public const string ThemeColor = "#00A3FF";

    /// <summary>
    /// Publisher logo source path for UI display.
    /// </summary>
    public const string LogoSource = UriConstants.GeneralsOnlineLogoUri;

    // ===== Version Parsing =====

    /// <summary>Format for parsing version dates (MMddyy).</summary>
    public const string VersionDateFormat = "MMddyy";

    /// <summary>Separator between date and QFE number in versions.</summary>
    public const string QfeSeparator = "_QFE";

    /// <summary>Prefix for QFE markers in version strings.</summary>
    public const string QfeMarkerPrefix = "QFE";

    /// <summary>Version string used when version information is missing.</summary>
    public const string UnknownVersion = "unknown";

    // ===== File Extensions =====

    /// <summary>File extension for portable downloads.</summary>
    public const string PortableExtension = ".zip";

    // ===== Update Intervals =====

    /// <summary>Hours between update checks.</summary>
    public const int UpdateCheckIntervalHours = 24;

    // ===== Manifest Generation =====

    /// <summary>Publisher ID for the Generals Online service.</summary>
    public const string PublisherId = PublisherType;

    /// <summary>Publisher type identifier for GeneralsOnline.</summary>
    public const string PublisherType = "generalsonline";

    /// <summary>Content type for GeneralsOnline game clients.</summary>
    public const string ContentType = "gameclient";

    /// <summary>Manifest name suffix for 30Hz variant.</summary>
    public const string Variant30HzSuffix = "30hz";

    /// <summary>Manifest name suffix for 60Hz variant.</summary>
    public const string Variant60HzSuffix = "60hz";

    /// <summary>Manifest name suffix for QuickMatch MapPack.</summary>
    public const string QuickMatchMapPackSuffix = "quickmatch-maps";

    /// <summary>Display name for QuickMatch MapPack.</summary>
    public const string QuickMatchMapPackDisplayName = "GeneralsOnline QuickMatch Maps";

    /// <summary>Description for QuickMatch MapPack.</summary>
    public const string QuickMatchMapPackDescription = "Official map pack required for GeneralsOnline QuickMatch multiplayer. Contains competitively balanced maps.";

    /// <summary>Subdirectory within the portable ZIP containing maps.</summary>
    public const string MapsSubdirectory = "Maps";

    // ===== Component Identifiers =====

    /// <summary>Source name for Generals Online discoverer.</summary>
    public const string DiscovererSourceName = PublisherType;

    /// <summary>Resolver ID for Generals Online resolver.</summary>
    public const string ResolverId = "GeneralsOnline";

    /// <summary>Source name for Generals Online deliverer.</summary>
    public const string DelivererSourceName = "Generals Online Deliverer";

    /// <summary>Description for Generals Online deliverer.</summary>
    public const string DelivererDescription = "Delivers Generals Online content via ZIP extraction and CAS storage";

    // ===== Content Tags =====

    /// <summary>Content tags for search and categorization.</summary>
    public static readonly string[] Tags = ["multiplayer", "online", "community", "enhancement"];

    /// <summary>
    /// Default tags for MapPack manifests.
    /// </summary>
    public static readonly string[] MapPackTags = ["mappack", "generalsonline", "quickmatch", "competitive"];
}
