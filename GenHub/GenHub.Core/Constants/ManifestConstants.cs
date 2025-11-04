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
    public const string DefaultManifestVersion = "1.0";

    /// <summary>
    /// Prefix for publisher content IDs.
    /// </summary>
    public const string PublisherContentIdPrefix = "publisher";

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
    /// </summary>
    public const int MinManifestSegments = 5;

    /// <summary>
    /// Regex pattern for validating 5-segment publisher content IDs (schemaVersion.userVersion.publisher.contentType.contentName).
    /// All manifest IDs must match this format.
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
}
