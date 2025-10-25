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
    /// Prefix for simple test IDs.
    /// </summary>
    public const string SimpleIdPrefix = "simple";

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
    /// Minimum number of segments in manifest ID.
    /// </summary>
    public const int MinManifestSegments = 1;

    /// <summary>
    /// Regex pattern for publisher content IDs.
    /// </summary>
    public const string PublisherIdRegexPattern = @"^\d+(?:\.\d+)*\.[a-zA-Z0-9\-]+(?:\.[a-zA-Z0-9\-]+)*$";

    /// <summary>
    /// Regex pattern for game installation IDs.
    /// </summary>
    public const string GameInstallationIdRegexPattern = @"^\d+(?:\.\d+)*\.(unknown|steam|eaapp|origin|thefirstdecade|rgmechanics|cdiso|wine|retail)\.(generals|zerohour)$";

    /// <summary>
    /// Regex pattern for simple IDs.
    /// </summary>
    public const string SimpleIdRegexPattern = @"^[a-zA-Z0-9\-\.]+$";

    /// <summary>
    /// Timeout for manifest ID generation operations in milliseconds.
    /// </summary>
    public const int ManifestIdGenerationTimeoutMs = 5000;

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
