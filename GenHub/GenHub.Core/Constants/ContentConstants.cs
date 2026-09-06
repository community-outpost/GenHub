namespace GenHub.Core.Constants;

/// <summary>
/// Constants used throughout the content services.
/// </summary>
public static class ContentConstants
{
    /// <summary>
    /// Default cache expiration time in minutes for content caching.
    /// </summary>
    public const int DefaultCacheExpirationMinutes = 30;

    /// <summary>
    /// Default limit for queries that should return a single result.
    /// </summary>
    public const int SingleResultQueryLimit = 1;

    /// <summary>
    /// Expected number of parts when parsing GitHub repository strings (owner/repo).
    /// </summary>
    public const int GitHubRepoPartsCount = 2;

    /// <summary>
    /// Default file size value when size is unknown.
    /// </summary>
    public const long DefaultFileSize = 0;

    /// <summary>
    /// Default download count value when count is unknown.
    /// </summary>
    public const int DefaultDownloadCount = 0;

    /// <summary>
    /// Default rating value when rating is unknown.
    /// </summary>
    public const float DefaultRating = 0f;

    /// <summary>
    /// Default progress step count for single-step operations.
    /// </summary>
    public const int SingleStepTotal = 1;

    /// <summary>
    /// Default progress step count for three-step operations.
    /// </summary>
    public const int ThreeStepTotal = 3;

    /// <summary>
    /// First step index in progress reporting (0-based).
    /// </summary>
    public const int StepOne = 0;

    /// <summary>
    /// Second step index in progress reporting (0-based).
    /// </summary>
    public const int StepTwo = 1;

    /// <summary>
    /// Third step index in progress reporting (0-based).
    /// </summary>
    public const int StepThree = 2;

    /// <summary>
    /// Progress percentage for validating manifest step (20%).
    /// </summary>
    public const int ProgressStepValidatingManifest = 20;

    /// <summary>
    /// Progress percentage for downloading step (40%).
    /// </summary>
    public const int ProgressStepDownloading = 40;

    /// <summary>
    /// Progress percentage for validating files step (70%).
    /// </summary>
    public const int ProgressStepValidatingFiles = 70;

    /// <summary>
    /// Progress percentage for extracting/storing step (85%).
    /// </summary>
    public const int ProgressStepExtracting = 85;

    /// <summary>
    /// Progress percentage for storing content in CAS (90%).
    /// </summary>
    public const int ProgressStepStoring = 90;

    /// <summary>
    /// Progress percentage for completion (100%).
    /// </summary>
    public const int ProgressStepCompleted = 100;

    /// <summary>
    /// Maximum allowed size for the content catalog in bytes (10 MB).
    /// </summary>
    public const long MaxCatalogSizeBytes = 10 * ConversionConstants.BytesPerMegabyte;

    /// <summary>
    /// Shared resolver/display metadata key for map player counts.
    /// Builtin and catalog publishers should set this so download cards can render a consistent badge.
    /// </summary>
    public const string PlayerCountMetadataKey = "playerCount";

    /// <summary>
    /// Shared resolver/display metadata key for content categories (AOA, Compstomp, ModDB category, etc.).
    /// </summary>
    public const string CategoryMetadataKey = "category";

    /// <summary>
    /// Display metadata key for a comma-separated list of included/required content names
    /// (e.g. catalog ContentBundle dependencies resolved to friendly titles).
    /// </summary>
    public const string IncludesSummaryMetadataKey = "includesSummary";

    /// <summary>
    /// Number of recent releases and addons to eagerly preload extended details for.
    /// </summary>
    public const int PreloadRecentItemsLimit = 5;

    /// <summary>
    /// Maximum concurrent background requests when preloading recent item details.
    /// </summary>
    public const int PreloadConcurrencyLimit = 3;
}