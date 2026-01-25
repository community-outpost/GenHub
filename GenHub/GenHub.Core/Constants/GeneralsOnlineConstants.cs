namespace GenHub.Core.Constants;

/// <summary>
/// Constants specific to Generals Online content provider and multiplayer service.
/// </summary>
public static class GeneralsOnlineConstants
{
    // ===== CDN API Endpoints =====

    /// <summary>Base URL for Generals Online CDN.</summary>
    public const string CdnBaseUrl = "https://cdn.playgenerals.online";

    /// <summary>API endpoint for JSON manifest with full release information.</summary>
    public const string ManifestApiUrl = "https://cdn.playgenerals.online/manifest.json";

    /// <summary>Endpoint for latest version information (plain text version string).</summary>
    public const string LatestVersionUrl = "https://cdn.playgenerals.online/latest.txt";

    /// <summary>Base URL for release downloads.</summary>
    public const string ReleasesUrl = "https://cdn.playgenerals.online/releases";

    // ===== Web URLs =====

    /// <summary>Official Generals Online website.</summary>
    public const string WebsiteUrl = "https://www.playgenerals.online/";

    /// <summary>Download page URL.</summary>
    public const string DownloadPageUrl = "https://www.playgenerals.online/#download";

    /// <summary>Support/discord URL.</summary>
    public const string SupportUrl = "https://discord.playgenerals.online/";

    // ===== Multiplayer API Endpoints =====

    /// <summary>Base URL for Generals Online web API.</summary>
    public const string ApiBaseUrl = "https://www.playgenerals.online";

    /// <summary>API endpoint for service statistics.</summary>
    public const string ServiceStatsEndpoint = "https://www.playgenerals.online/servicestats";

    /// <summary>API endpoint for active matches and lobbies.</summary>
    public const string MatchesEndpoint = "https://www.playgenerals.online/matches";

    /// <summary>API endpoint for player profiles.</summary>
    public const string PlayersEndpoint = "https://www.playgenerals.online/players";

    /// <summary>API endpoint for leaderboards.</summary>
    public const string LeaderboardsEndpoint = "https://www.playgenerals.online/leaderboards";

    /// <summary>API endpoint for match history.</summary>
    public const string MatchHistoryEndpoint = "https://www.playgenerals.online/matchhistory";

    /// <summary>API endpoint for viewing a specific match.</summary>
    public const string ViewMatchEndpoint = "https://www.playgenerals.online/viewmatch";

    /// <summary>Service status monitoring URL.</summary>
    public const string ServiceStatusUrl = "https://stats.uptimerobot.com/5OBCMJwv8P";

    /// <summary>FAQ page URL.</summary>
    public const string FaqUrl = ApiBaseUrl + "/faq";

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

    /// <summary>
    /// Publisher logo source path for UI display.
    /// </summary>
    public const string LogoSource = "/Assets/Logos/generalsonline-logo.png";

    /// <summary>
    /// Cover image source path for UI display.
    /// </summary>
    public const string CoverSource = "/Assets/Covers/zerohour-cover.png";

    // ===== Version Parsing =====

    /// <summary>Format for parsing version dates (DDMMYY).</summary>
    public const string VersionDateFormat = "ddMMyy";

    /// <summary>Separator between date and QFE number in versions.</summary>
    public const string QfeSeparator = "_QFE";

    // ===== File Extensions =====

    /// <summary>File extension for portable downloads.</summary>
    public const string PortableExtension = ".zip";

    // ===== Update Intervals =====

    /// <summary>Hours between update checks.</summary>
    public const int UpdateCheckIntervalHours = 24;

    // ===== Manifest Generation =====

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

    // ===== Authentication & API =====

    /// <summary>Base URL for Generals Online REST API (production environment, contract version 1).</summary>
    public const string RestApiBaseUrl = "https://api.playgenerals.online/env/prod/contract/1";

    /// <summary>API endpoint for checking browser login status by gamecode.</summary>
    public const string CheckLoginEndpoint = RestApiBaseUrl + "/CheckLogin";

    /// <summary>API endpoint for exchanging refresh token for session token.</summary>
    public const string LoginWithTokenEndpoint = RestApiBaseUrl + "/LoginWithToken";

    /// <summary>API endpoint for token verification.</summary>
    public const string VerifyTokenEndpoint = "https://api.playgenerals.online/v1/user/verify";

    /// <summary>API endpoint for user profile information.</summary>
    public const string UserProfileEndpoint = "https://api.playgenerals.online/v1/user/profile";

    /// <summary>API endpoint for lobby listings.</summary>
    public const string LobbiesEndpoint = "https://api.playgenerals.online/v1/lobbies";

    /// <summary>Client identifier for GenHub authentication.</summary>
    public const string ClientId = "genhub";

    /// <summary>Length of the gamecode for browser login.</summary>
    public const int GameCodeLength = 32;

    // ===== HTTP Headers =====

    /// <summary>HTTP Accept header name.</summary>
    public const string AcceptHeader = "Accept";

    /// <summary>HTTP Accept header value for Generals Online API.</summary>
    public const string AcceptHeaderValue = "text/html,application/json";

    /// <summary>HTTP header name for authentication token.</summary>
    public const string AuthTokenHeader = "X-Auth-Token";

    // ===== OAuth and Authentication Flow =====

    /// <summary>Base URL for the login page.</summary>
    public const string LoginUrlBase = "https://www.playgenerals.online/login";

    /// <summary>Custom URI scheme for OAuth callbacks.</summary>
    public const string CallbackScheme = "genhub";

    /// <summary>Path component for OAuth callback URI.</summary>
    public const string CallbackPath = "auth/callback";

    /// <summary>Full callback URI template: genhub://auth/callback.</summary>
    public const string CallbackUriTemplate = CallbackScheme + "://" + CallbackPath;

    // ===== Credentials File =====

    /// <summary>Name of the GeneralsOnline data folder.</summary>
    public const string DataFolderName = "GeneralsOnlineData";

    /// <summary>Name of the credentials JSON file.</summary>
    public const string CredentialsFileName = "credentials.json";

    /// <summary>Relative path to GeneralsOnlineData folder from Generals Zero Hour Data.</summary>
    public const string GeneralsOnlineDataPath = "Command and Conquer Generals Zero Hour Data" + "\\" + DataFolderName;

    // ===== Content Tags =====

    /// <summary>Content tags for search and categorization.</summary>
    public static readonly string[] Tags = ["multiplayer", "online", "community", "enhancement"];
}
