namespace GenHub.Core.Constants;

/// <summary>
/// Constants related to manifest ID generation, validation, and file operations.
/// </summary>
public static class ManifestConstants
{
    /// <summary>
    /// Default manifest format version.
    /// </summary>
    public const int DefaultManifestFormatVersion = 1;

    /// <summary>
    /// Default manifest format version as string.
    /// </summary>
    public const string DefaultManifestVersion = "1";

    /// <summary>
    /// Prefix for publisher content IDs.
    /// </summary>
    public const string PublisherContentIdPrefix = "publisher";

    /// <summary>
    /// Tag for content validation status.
    /// </summary>
    public const string ValidationStatusTag = "ValidationStatus";

    /// <summary>
    /// Prefix for game installation IDs.
    /// </summary>
    public const string BaseGameIdPrefix = "gameinstallation";

    /// <summary>
    /// Maximum length for manifest IDs.
    /// </summary>
    public const int MaxManifestIdLength = 256;

    /// <summary>
    /// Minimum length for manifest IDs.
    /// </summary>
    public const int MinManifestIdLength = 3;

    /// <summary>
    /// Maximum number of segments in manifest ID.
    /// </summary>
    public const int MaxManifestSegments = 5;

    /// <summary>
    /// Minimum number of segments in manifest ID (must be exactly 5).
    /// Format: schemaVersion.userVersion.publisher.contentType.contentName
    /// Examples:
    /// - 1.0.ea.gameinstallation.generals
    /// - 1.108.steam.mod.communitymaps
    /// - 1.104.origin.patch.officialpatch
    /// This 5-segment structure ensures:
    /// - Consistent parsing and validation across the system
    /// - Hierarchical organization: schema versioning → user versioning → publisher → content type → content name
    /// - Unique identification across publishers and content types
    /// - Schema versioning support for future format changes
    /// - Efficient indexing and querying capabilities.
    /// </summary>
    public const int MinManifestSegments = 5;

    /// <summary>
    /// Regex pattern for validating 5-segment publisher content IDs (schemaVersion.userVersion.publisher.contentType.contentName).
    /// All manifest IDs must match this format to ensure consistent identification and categorization.
    /// Format breakdown:
    /// - schemaVersion: Numeric schema version (e.g., "1")
    /// - userVersion: User-specified version without dots (e.g., "108" for v1.08, "0" for default)
    /// - publisher: Publisher identifier (e.g., "ea", "steam", "cnclabs")
    /// - contentType: Content type enum value (e.g., "gameinstallation", "mod", "patch")
    /// - contentName: Content identifier (e.g., "generals", "zerohour", "communitymaps")
    /// Examples: "1.0.ea.gameinstallation.generals", "1.108.steam.mod.communitymaps".
    /// </summary>
    public const string PublisherContentRegexPattern = @"^\d+\.\d+\.[a-z0-9]+\.(gameinstallation|gameclient|mod|patch|addon|mappack|languagepack|contentbundle|publisherreferral|contentreferral|mission|map|unknown)\.[a-z0-9-]+$";

    /// <summary>
    /// Timeout for manifest validation operations in milliseconds.
    /// </summary>
    public const int ManifestValidationTimeoutMs = 1000;

    /// <summary>
    /// Maximum concurrent manifest operations.
    /// </summary>
    public const int MaxConcurrentManifestOperations = 10;

    /// <summary>
    /// Default ID string for content dependencies (fallback for model instantiation).
    /// </summary>
    public const string DefaultContentDependencyId = "1.0.genhub.content.defaultdependency";

    /// <summary>
    /// Version string for Generals game installation manifests.
    /// This represents the executable version 1.08.
    /// Note: When used in manifest IDs, dots are removed to create "108" for schema compliance.
    /// </summary>
    public const string GeneralsManifestVersion = "1.08";

    /// <summary>
    /// Version string for Zero Hour game installation manifests.
    /// This represents the executable version 1.04.
    /// Note: When used in manifest IDs, dots are removed to create "104" for schema compliance.
    /// </summary>
    public const string ZeroHourManifestVersion = "1.04";

    /// <summary>Tag for unknown authors.</summary>
    public const string UnknownAuthor = "unknown";

    /// <summary>Tag for unknown versions.</summary>
    public const string UnknownVersion = "unknown";

    // ===== Content Type Tags =====

    /// <summary>Tag for Map content.</summary>
    public const string MapTag = "Map";

    /// <summary>Tag for Map Pack content.</summary>
    public const string MapPackTag = "Map Pack";

    /// <summary>Tag for Mission content.</summary>
    public const string MissionTag = "Mission";

    /// <summary>Tag for Mod content.</summary>
    public const string ModTag = "Mod";

    /// <summary>Tag for Patch content.</summary>
    public const string PatchTag = "Patch";

    /// <summary>Tag for Skin content.</summary>
    public const string SkinTag = "Skin";

    /// <summary>Tag for Video content.</summary>
    public const string VideoTag = "Video";

    /// <summary>Tag for Modding Tool content.</summary>
    public const string ModdingToolTag = "Modding Tool";

    /// <summary>Tag for Language Pack content.</summary>
    public const string LanguagePackTag = "Language Pack";

    /// <summary>Tag for Addon content.</summary>
    public const string AddonTag = "Addon";

    /// <summary>Tag for Screensaver content.</summary>
    public const string ScreensaverTag = "Screensaver";

    /// <summary>Tag for Replay content.</summary>
    public const string ReplayTag = "Replay";

    /// <summary>Tag for other content types.</summary>
    public const string OtherTag = "Other";
}
