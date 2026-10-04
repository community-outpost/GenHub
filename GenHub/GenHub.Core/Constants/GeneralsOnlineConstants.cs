using GenHub.Core.Models.Enums;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for Generals Online integration.
/// Contains provider metadata, URLs, content types, and default values.
/// </summary>
[SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Centralized URI constants / mock demo paths")]
public static class GeneralsOnlineConstants
{
    // ===== Provider Metadata =====

    /// <summary>Publisher identifier for Generals Online.</summary>
    public const string PublisherName = "GeneralsOnline";

    /// <summary>Publisher ID for the Generals Online service.</summary>
    public const string PublisherId = PublisherType;

    /// <summary>Publisher type identifier used in manifest IDs and routing.</summary>
    public const string PublisherType = PublisherTypeConstants.GeneralsOnline;

    /// <summary>Client name and identifier prefix for GeneralsOnline.</summary>
    public const string ClientName = "GeneralsOnline";

    /// <summary>Default executable file name for Generals Online clients.</summary>
    public const string DefaultExecutableFileName = "GeneralsOnline.exe";

    /// <summary>Content type for GeneralsOnline game clients.</summary>
    public const string ContentType = "gameclient";

    /// <summary>Display name for the publisher shown in UI.</summary>
    public const string PublisherDisplayName = "Generals Online";

    /// <summary>Content name for Generals Online content items.</summary>
    public const string ContentName = "Generals Online";

    /// <summary>Short description of Generals Online.</summary>
    public const string ShortDescription = "Community-driven multiplayer platform for C&C Generals: Zero Hour with 60 FPS support, modern networking, and active matchmaking.";

    /// <summary>Full description of Generals Online features.</summary>
    public const string Description = "Generals Online is a modernized version of Command & Conquer Generals: Zero Hour featuring smooth 60 FPS gameplay, reliable peer-to-peer and relayed networking via GameNetworkingSockets, integrated QuickMatch matchmaking, and active anti-cheat protection. Built by the community for competitive and casual play.";

    // ===== URLs and Endpoints =====

    /// <summary>Content icon URL.</summary>
    public const string IconUrl = UriConstants.GeneralsOnlineLogoUri;

    /// <summary>Main website URL.</summary>
    public const string WebsiteUrl = "https://generalsonline.com";

    /// <summary>Support and Discord community URL.</summary>
    public const string SupportUrl = "https://discord.gg/generalsonline";

    /// <summary>Download and releases page URL.</summary>
    public const string DownloadPageUrl = "https://generalsonline.com/download";

    /// <summary>API endpoint for release data.</summary>
    public const string ApiEndpoint = "https://generalsonline.com/api/releases";

    /// <summary>Changelog and release notes URL template.</summary>
    public const string ChangelogUrl = "https://generalsonline.com/changelog";

    /// <summary>Base website URL for Play Generals Online.</summary>
    public const string PlayGeneralsOnlineBaseUrl = "https://www.playgenerals.online";

    /// <summary>Play Generals Online domain host name.</summary>
    public const string PlayGeneralsOnlineDomain = "www.playgenerals.online";

    /// <summary>Play Generals Online apex domain host name.</summary>
    public const string PlayGeneralsOnlineApexDomain = "playgenerals.online";

    /// <summary>Play Generals Online legacy .com domain host name.</summary>
    public const string PlayGeneralsOnlineComDomain = "playgeneralsonline.com";

    /// <summary>Play Generals Online legacy www .com domain host name.</summary>
    public const string PlayGeneralsOnlineWwwComDomain = "www.playgeneralsonline.com";

    /// <summary>Generals Online website domain host name.</summary>
    public const string GeneralsOnlineDomain = "generalsonline.com";

    /// <summary>Generals Online www website domain host name.</summary>
    public const string GeneralsOnlineWwwDomain = "www.generalsonline.com";

    /// <summary>Patch notes URL for Generals Online.</summary>
    public const string PatchNotesUrl = "https://www.playgenerals.online/patchnotes";

    /// <summary>Community Discord invite URL for Generals Online.</summary>
    public const string DiscordUrl = "https://discord.playgenerals.online";

    /// <summary>Code of conduct URL for Generals Online.</summary>
    public const string CodeOfConductUrl = "https://www.playgenerals.online/codeofconduct";

    /// <summary>QuickMatch ladder URL hosted on GameReplays Strata.</summary>
    public const string LaddersUrl = "https://strata.gamereplays.org/zh/league/quickmatch";

    /// <summary>Recent matches URL hosted on GameReplays Strata.</summary>
    public const string MatchesUrl = "https://strata.gamereplays.org/zh/matches";

    /// <summary>Public service status page URL for Generals Online.</summary>
    public const string ServiceStatusUrl = "https://status.playgenerals.online";

    /// <summary>Default releases endpoint URL for Generals Online portable downloads.</summary>
    public const string ReleasesUrl = "https://cdn.playgenerals.online/releases";

    // ===== UI and Branding =====

    /// <summary>Theme color for Generals Online branding (hex format, GO sky blue).</summary>
    public const string ThemeColor = "#00A3FF";

    /// <summary>Path to publisher logo asset.</summary>
    public const string LogoSource = UriConstants.GeneralsOnlineLogoUri;

    /// <summary>Path to publisher cover asset.</summary>
    public const string CoverSource = "/Assets/Covers/usa-cover.jpg";

    /// <summary>Portable release description suffix.</summary>
    public const string PortableReleaseSuffix = " portable release";

    // ===== Versioning and Sync =====

    /// <summary>Format for parsing version dates (MMddyy).</summary>
    public const string VersionDateFormat = "MMddyy";

    /// <summary>Separator between date and QFE number in versions.</summary>
    public const string QfeSeparator = "_QFE";

    /// <summary>Prefix for QFE markers in version strings.</summary>
    public const string QfeMarkerPrefix = "QFE";

    /// <summary>Prefix for portable archive filenames.</summary>
    public const string PortableFilePrefix = "GeneralsOnline_portable_";

    /// <summary>File extension for portable downloads.</summary>
    public const string PortableExtension = ".zip";

    /// <summary>CRC catalog content name for Easy Anti-Cheat Zero Hour game clients.</summary>
    public const string EacZeroHourContentName = "eac-zerohour";

    /// <summary>Content name suffix for compound detector game client ids such as "zerohour-generalsonline-60hz".</summary>
    public const string Compound60HzContentNameSuffix = "-" + PublisherType + "-" + Variant60HzSuffix;

    /// <summary>Update check interval in hours.</summary>
    public const int UpdateCheckIntervalHours = 24;

    /// <summary>Fallback version string when parsing fails.</summary>
    public const string UnknownVersion = "Unknown";

    /// <summary>Minimum expected version string length for validation.</summary>
    public const int MinimumVersionLength = 6;

    /// <summary>Number of characters in the MMDDYY date portion of a Generals Online version.</summary>
    public const int DateComponentLength = 6;

    // ===== Content ID Prefixes =====

    /// <summary>Prefix used for Generals Online manifest IDs.</summary>
    public const string ManifestIdPrefix = "generalsonline-";

    /// <summary>Legacy typed-group segment for game client variant groups.</summary>
    public const string LegacyGameClientGroupSegment = "gameclient-";

    /// <summary>Legacy typed-group segment for patch variant groups.</summary>
    public const string LegacyPatchGroupSegment = "patch-";

    /// <summary>Legacy typed-group segment for mappack variant groups.</summary>
    public const string LegacyMapPackGroupSegment = "mappack-";

    /// <summary>Prefix used for Generals Online content IDs from the API.</summary>
    public const string ContentIdPrefix = "GeneralsOnline_";

    // ===== Game Client Variants =====

    /// <summary>Variant suffix for the 60Hz high-performance client.</summary>
    public const string Variant60HzSuffix = "60hz";

    /// <summary>Variant suffix for the Test Environment client (direct execution, no EAC).</summary>
    public const string VariantTestEnvironmentSuffix = "test";

    /// <summary>Legacy variant suffix for the Test Environment client.</summary>
    public const string LegacyVariantTestEnvironmentSuffix = "testenvironment";

    /// <summary>Variant suffix for the QuickMatch MapPack.</summary>
    public const string QuickMatchMapPackSuffix = "quickmatchmaps";

    /// <summary>Display name for the QuickMatch MapPack.</summary>
    public const string QuickMatchMapPackDisplayName = "Generals Online QuickMatch MapPack";

    /// <summary>Description for the QuickMatch MapPack.</summary>
    public const string QuickMatchMapPackDescription = "Official map pool for Generals Online QuickMatch multiplayer.";

    /// <summary>Variant suffix for the GeneralsOnlineGameData data patch.</summary>
    public const string GameDataPatchSuffix = "gamedata";

    /// <summary>Display name for the GeneralsOnlineGameData data patch.</summary>
    public const string GameDataDisplayName = "Generals Online Game Data";

    /// <summary>Description for the GeneralsOnlineGameData data patch.</summary>
    public const string GameDataDescription = "Community balance patch and core INI configuration for Generals Online.";

    /// <summary>Default variant suffix when none specified.</summary>
    public const string DefaultVariantSuffix = Variant60HzSuffix;

    /// <summary>Display name for GeneralsOnline Test Environment variant.</summary>
    public const string TestEnvironmentDisplayName = GameClientConstants.GeneralsOnlineTestEnvironmentDisplayName;

    /// <summary>Description for GeneralsOnline Test Environment variant.</summary>
    public const string TestEnvironmentDescription = "Direct execution client for testing and debugging without Easy Anti-Cheat.";

    /// <summary>Display name for GeneralsOnline 30Hz variant (legacy, not shipped in current portable).</summary>
    public const string ThirtyHzDisplayName = "GeneralsOnline 30Hz";

    /// <summary>Subdirectory within the portable ZIP containing GeneralsOnline maps.</summary>
    public const string MapsSubdirectory = "Maps";

    /// <summary>Subdirectory within the portable ZIP containing GeneralsOnline game data.</summary>
    public const string GameDataSubdirectory = "GeneralsOnlineGameData";

    // ===== Component Identifiers =====

    /// <summary>Source name for Generals Online discoverer.</summary>
    public const string DiscovererSourceName = PublisherType;

    /// <summary>Resolver ID for Generals Online resolver.</summary>
    public const string ResolverId = "GeneralsOnline";

    /// <summary>Source name for Generals Online deliverer.</summary>
    public const string DelivererSourceName = "Generals Online Deliverer";

    /// <summary>Description for Generals Online deliverer.</summary>
    public const string DelivererDescription = "Delivers Generals Online content via ZIP extraction and CAS storage";

    // ===== Easy Anti-Cheat Installation =====

    /// <summary>Manifest-relative path of the Easy Anti-Cheat bootstrapper settings file.</summary>
    public const string EacSettingsRelativePath = "EasyAntiCheat/Settings.json";

    /// <summary>Settings key naming the game binary the bootstrapper starts.</summary>
    public const string EacSettingsExecutableKey = "executable";

    /// <summary>Settings key carrying the Epic Online Services product ID.</summary>
    public const string EacSettingsProductIdKey = "productid";

    /// <summary>Maximum accepted size of the bootstrapper settings file in bytes.</summary>
    public const int EacSettingsMaxSizeBytes = 65536;

    /// <summary>Product ID registered with Epic Online Services Easy Anti-Cheat for Generals Online.</summary>
    public const string EacProductId = "fc1cc0d936424212b645105f084d08b0";

    /// <summary>Setup command passed to EasyAntiCheat_EOS_Setup.exe.</summary>
    public const string EacInstallCommand = "install";

    /// <summary>Display name for the Easy Anti-Cheat installation step.</summary>
    public const string EacStepName = "Install Easy Anti-Cheat";

    /// <summary>Status message displayed to the user during Easy Anti-Cheat installation.</summary>
    public const string EacStatusMessage = "Installing AntiCheat";

    /// <summary>Unique step key identifying Easy Anti-Cheat installation for Generals Online.</summary>
    public const string EacStepKey = PublisherType + ":eac:" + EacProductId;

    // ===== Multiplayer REST API =====

    /// <summary>Environment variable overriding the Generals Online REST API base URL.</summary>
    public const string RestApiBaseUrlEnvVar = "GENHUB_GO_API_URL";

    /// <summary>Default base URL for the Generals Online REST API.</summary>
    public const string DefaultRestApiBaseUrl = "https://api.playgenerals.online";

    /// <summary>Backend environment segment in REST routes.</summary>
    public const string RestApiEnvironment = "prod";

    /// <summary>Backend contract version segment in REST routes.</summary>
    public const string RestApiContractVersion = "1";

    /// <summary>Route prefix combining the environment and contract version.</summary>
    public const string RestApiRoutePrefix = "/env/" + RestApiEnvironment + "/contract/" + RestApiContractVersion;

    /// <summary>Endpoint issuing browser-login codes.</summary>
    public const string LoginCodeEndpoint = RestApiRoutePrefix + "/LoginCode";

    /// <summary>Endpoint polling browser-login completion.</summary>
    public const string CheckLoginEndpoint = RestApiRoutePrefix + "/CheckLogin";

    /// <summary>Endpoint exchanging a refresh token for a new session.</summary>
    public const string LoginWithTokenEndpoint = RestApiRoutePrefix + "/LoginWithToken";

    /// <summary>Endpoint listing active multiplayer lobbies.</summary>
    public const string LobbiesEndpoint = RestApiRoutePrefix + "/Lobbies";

    /// <summary>Endpoint listing network rooms. The first room shows all matches.</summary>
    public const string RoomsEndpoint = RestApiRoutePrefix + "/Rooms";

    /// <summary>Endpoint for a single lobby. Format with the lobby id.</summary>
    public const string LobbyByIdFormat = RestApiRoutePrefix + "/Lobby/{0}";

    /// <summary>Unauthenticated endpoint with total online players and lobbies.</summary>
    public const string MonitoringBasicStatsEndpoint = RestApiRoutePrefix + "/Monitoring/BasicStats";

    /// <summary>Unauthenticated endpoint with the service start time and uptime.</summary>
    public const string MonitoringUptimeEndpoint = RestApiRoutePrefix + "/Monitoring/Uptime";

    /// <summary>Endpoint with today's per-faction match and win totals.</summary>
    public const string GlobalStatsEndpoint = RestApiRoutePrefix + "/GlobalStats";

    /// <summary>Endpoint for one player's statistics. Format with the user id.</summary>
    public const string PlayerStatsByIdFormat = RestApiRoutePrefix + "/PlayerStats/{0}";

    /// <summary>Endpoint with the community message of the day.</summary>
    public const string MotdEndpoint = RestApiRoutePrefix + "/MOTD";

    /// <summary>Endpoint listing friends and pending requests.</summary>
    public const string SocialFriendsEndpoint = RestApiRoutePrefix + "/Social/Friends";

    /// <summary>Endpoint listing blocked users.</summary>
    public const string SocialBlockedEndpoint = RestApiRoutePrefix + "/Social/Blocked";

    /// <summary>Endpoint for friend requests. Format with the target user id.</summary>
    public const string SocialFriendRequestFormat = RestApiRoutePrefix + "/Social/Friends/Requests/{0}";

    /// <summary>Endpoint for removing a friend. Format with the target user id.</summary>
    public const string SocialFriendFormat = RestApiRoutePrefix + "/Social/Friends/{0}";

    /// <summary>Endpoint for blocking a user. Format with the target user id.</summary>
    public const string SocialBlockedUserFormat = RestApiRoutePrefix + "/Social/Blocked/{0}";

    /// <summary>Endpoint listing active users for launcher sessions.</summary>
    public const string UsersActiveEndpoint = RestApiRoutePrefix + "/Users/Active";

    /// <summary>Endpoint for bulk player stats lookups.</summary>
    public const string PlayerStatsBatchEndpoint = RestApiRoutePrefix + "/PlayerStats/Batch";

    /// <summary>Endpoint returning the signed-in user's id and display name.</summary>
    public const string UsersMeEndpoint = RestApiRoutePrefix + "/Users/Me";

    // ===== Message Of The Day =====

    /// <summary>Prefix introducing an inline MOTD color code.</summary>
    public const char MotdColorCodePrefix = '\\';

    /// <summary>Hex digits in an MOTD color code (AARRGGBB).</summary>
    public const int MotdColorCodeHexDigits = 8;

    /// <summary>Hex digits of the RGB part surfaced from an MOTD color code.</summary>
    public const int MotdColorRgbHexDigits = 6;

    /// <summary>Total characters of an MOTD color code including the prefix.</summary>
    public const int MotdColorCodeLength = MotdColorCodeHexDigits + 1;

    /// <summary>Prefix for parsed MOTD colors surfaced as #RRGGBB.</summary>
    public const string MotdColorHexPrefix = "#";

    /// <summary>
    /// Client identifier sent as client_id during login. Must match the backend
    /// KnownClients enum name exactly (case-insensitive); the backend maps
    /// genhub to a GameLauncher session.
    /// </summary>
    public const string ClientId = "genhub";

    /// <summary>Base URL of the browser login page.</summary>
    public const string LoginPageBaseUrl = "https://www.playgenerals.online/login/";

    /// <summary>Login page URL format. Format with the game code.</summary>
    public const string LoginPageUrlFormat = LoginPageBaseUrl + "?gamecode={0}";

    /// <summary>HTTP timeout in seconds for Generals Online REST calls.</summary>
    public const int HttpTimeoutSeconds = 15;

    /// <summary>Delay in seconds between browser-login status polls.</summary>
    public const int LoginPollIntervalSeconds = 2;

    /// <summary>Browser-login polling timeout in minutes.</summary>
    public const int LoginPollTimeoutMinutes = 3;

    /// <summary>WebSocket message id selecting the network room for lobby filtering.</summary>
    public const int WebSocketNetworkRoomChangeId = 3;

    /// <summary>Room flag marking the room that shows matches from all rooms.</summary>
    public const int RoomFlagsShowAllMatches = 1;

    /// <summary>How long selecting the network room waits for an open socket, in milliseconds.</summary>
    public const int WebSocketRoomSelectTimeoutMs = 10000;

    /// <summary>Poll interval while waiting for an open socket before selecting the room, in milliseconds.</summary>
    public const int WebSocketRoomSelectPollMs = 100;

    /// <summary>Poll interval while waiting for an open socket, in milliseconds.</summary>
    public const int WebSocketOpenPollIntervalMs = WebSocketRoomSelectPollMs;

    /// <summary>WebSocket message id signalling the lobby list changed.</summary>
    public const int WebSocketLobbyListUpdateId = 7;

    /// <summary>WebSocket message id signalling the current lobby changed.</summary>
    public const int WebSocketCurrentLobbyUpdateId = 6;

    /// <summary>WebSocket message id for sending room chat.</summary>
    public const int WebSocketRoomChatSendId = 1;

    /// <summary>WebSocket message id for incoming room chat.</summary>
    public const int WebSocketRoomChatReceiveId = 2;

    /// <summary>WebSocket message id signalling the room member list changed.</summary>
    public const int WebSocketRoomMembersUpdateId = 4;

    /// <summary>WebSocket message id signalling a new friend request.</summary>
    public const int WebSocketFriendRequestId = 29;

    /// <summary>WebSocket message id signalling friend presence changed.</summary>
    public const int WebSocketFriendPresenceId = 32;

    /// <summary>WebSocket message id signalling the friends list is dirty.</summary>
    public const int WebSocketFriendsDirtyId = 37;

    /// <summary>WebSocket message id for sending friend chat.</summary>
    public const int WebSocketFriendChatSendId = 30;

    /// <summary>WebSocket message id for incoming friend chat.</summary>
    public const int WebSocketFriendChatReceiveId = 31;

    /// <summary>WebSocket message id for subscribing to realtime social updates.</summary>
    public const int WebSocketSocialSubscribeId = 33;

    /// <summary>WebSocket message id carrying the friends online/pending counts.</summary>
    public const int WebSocketFriendsStatusId = 35;

    /// <summary>WebSocket message id signalling a request was accepted.</summary>
    public const int WebSocketFriendRequestAcceptedId = 36;

    /// <summary>WebSocket message id carrying a moderation notice.</summary>
    public const int WebSocketModerationNoticeId = 46;

    /// <summary>Minimum interval between hint-driven background refreshes.</summary>
    public const int HintRefreshMinIntervalMs = 5000;

    /// <summary>Maximum direct messages kept per friend thread.</summary>
    public const int FriendChatMaxMessages = 100;

    /// <summary>Maximum room chat messages kept in the view model.</summary>
    public const int RoomChatMaxMessages = 200;

    /// <summary>Maximum room chat message length in characters.</summary>
    public const int RoomChatMaxLength = 512;

    /// <summary>Search debounce in milliseconds for the lobby filter.</summary>
    public const int LobbySearchDebounceMs = 250;

    /// <summary>Debounce in milliseconds before acting on a WebSocket lobby hint.</summary>
    public const int WebSocketRefreshDebounceMs = 1000;

    /// <summary>Maximum reassembled WebSocket message size in bytes before the connection is closed.</summary>
    public const int WebSocketMaxMessageBytes = 1048576;

    /// <summary>Error code prefix for Generals Online failures.</summary>
    public const string ErrorCodePrefix = "generalsonline.";

    /// <summary>Error code for an unreachable Generals Online backend.</summary>
    public const string ErrorServiceUnavailable = "generalsonline.service-unavailable";

    /// <summary>Error code for a failed or expired login code.</summary>
    public const string ErrorLoginFailed = "generalsonline.login-failed";

    /// <summary>Error code for a banned account.</summary>
    public const string ErrorAccountBanned = "generalsonline.account-banned";

    /// <summary>Error code when authentication is required.</summary>
    public const string ErrorAuthRequired = "generalsonline.auth-required";

    /// <summary>Error code when the lobby list cannot be read.</summary>
    public const string ErrorLobbiesUnavailable = "generalsonline.lobbies-unavailable";

    /// <summary>Error code when the backend denies lobby listing for this session type.</summary>
    public const string ErrorLobbiesForbidden = "generalsonline.lobbies-forbidden";

    /// <summary>Maximum error body characters kept in failure diagnostics.</summary>
    public const int ErrorBodyPreviewLength = 512;

    /// <summary>File name of the persisted Generals Online refresh token.</summary>
    public const string TokenFileName = ".gotoken";

    /// <summary>Salt for the machine-bound refresh token encryption key.</summary>
    public const string TokenFileKeySalt = "GenHub.GeneralsOnline.Token.v1";

    // ===== Content Tags =====

    /// <summary>Content tags for search and categorization.</summary>
    public static readonly string[] Tags = ["multiplayer", "online", "community", "enhancement"];

    /// <summary>Default tags for MapPack manifests.</summary>
    public static readonly string[] MapPackTags = ["mappack", "generalsonline", "quickmatch", "competitive"];

    /// <summary>Default tags for GameData patch manifests.</summary>
    public static readonly string[] GameDataTags = ["patch", "generalsonline"];

    // ===== Known Domains =====

    /// <summary>Known domain host names for Generals Online services and websites.</summary>
    public static readonly string[] KnownDomains =
    [
        PlayGeneralsOnlineDomain,
        PlayGeneralsOnlineApexDomain,
        PlayGeneralsOnlineComDomain,
        PlayGeneralsOnlineWwwComDomain,
        GeneralsOnlineDomain,
        GeneralsOnlineWwwDomain,
    ];
}
