using GenHub.Core.Models.Enums;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for Generals Online integration.
/// Contains provider metadata, URLs, content types, and default values.
/// </summary>
public static class GeneralsOnlineConstants
{
    // ===== Provider Metadata =====

    /// <summary>Publisher identifier for Generals Online.</summary>
    public const string PublisherName = "GeneralsOnline";

    /// <summary>Publisher type identifier used in manifest IDs and routing.</summary>
    public const string PublisherType = PublisherTypeConstants.GeneralsOnline;

    /// <summary>Publisher type enum value for typed comparisons.</summary>
    public const PublisherType PublisherTypeValue = Models.Enums.PublisherType.GeneralsOnline;

    /// <summary>Display name for the publisher shown in UI.</summary>
    public const string PublisherDisplayName = "Generals Online";

    /// <summary>Content name for Generals Online content items.</summary>
    public const string ContentName = "Generals Online";

    /// <summary>Short description of Generals Online.</summary>
    public const string ShortDescription = "Community-driven multiplayer platform for C&C Generals: Zero Hour with 60 FPS support, modern networking, and active matchmaking.";

    /// <summary>Full description of Generals Online features.</summary>
    public const string Description = "Generals Online is a modernized version of Command & Conquer Generals: Zero Hour featuring smooth 60 FPS gameplay, reliable peer-to-peer and relayed networking via GameNetworkingSockets, integrated QuickMatch matchmaking, and active anti-cheat protection. Built by the community for competitive and casual play.";

    // ===== URLs and Endpoints =====

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

    // ===== UI and Branding =====

    /// <summary>Theme color for Generals Online branding (hex format, GO orange/gold).</summary>
    public const string ThemeColor = "#FF8C00";

    /// <summary>Path to publisher logo asset.</summary>
    public const string LogoSource = "avares://GenHub/Assets/Images/Publishers/generalsonline_logo.png";

    /// <summary>Path to publisher cover asset.</summary>
    public const string CoverSource = "avares://GenHub/Assets/Images/Publishers/generalsonline_cover.png";

    // ===== Versioning and Sync =====

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

    /// <summary>Prefix used for Generals Online content IDs from the API.</summary>
    public const string ContentIdPrefix = "generalsonline_";

    // ===== Game Client Variants =====

    /// <summary>Variant suffix for the 60Hz high-performance client.</summary>
    public const string Variant60HzSuffix = "60hz";

    /// <summary>Variant suffix for the Test Environment client (direct execution, no EAC).</summary>
    public const string VariantTestEnvironmentSuffix = "test";

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

    // ===== Content Tags =====

    /// <summary>Content tags for search and categorization.</summary>
    public static readonly string[] Tags = ["multiplayer", "online", "community", "enhancement"];

    /// <summary>Default tags for MapPack manifests.</summary>
    public static readonly string[] MapPackTags = ["mappack", "generalsonline", "quickmatch", "competitive"];

    /// <summary>Default tags for GameData patch manifests.</summary>
    public static readonly string[] GameDataTags = ["patch", "generalsonline"];
}
