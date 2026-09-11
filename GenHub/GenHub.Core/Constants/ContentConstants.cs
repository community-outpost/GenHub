using GenHub.Core.Models.Enums;

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
    /// Metadata key for referencing a parent content item ID (e.g. associating child releases/addons with their parent content).
    /// </summary>
    public const string ParentContentIdMetadataKey = "parentContentId";

    /// <summary>
    /// Number of recent releases and addons to eagerly preload extended details for.
    /// </summary>
    public const int PreloadRecentItemsLimit = 5;

    /// <summary>
    /// Maximum concurrent background requests when preloading recent item details.
    /// </summary>
    public const int PreloadConcurrencyLimit = 3;

    /// <summary>
    /// Sidebar section title when content has required dependencies.
    /// </summary>
    public const string RequiresSectionTitle = "Requires";

    /// <summary>
    /// Sidebar section title when content bundles included items.
    /// </summary>
    public const string IncludesSectionTitle = "Includes";

    /// <summary>
    /// Category label for downloadable releases.
    /// </summary>
    public const string ReleaseCategory = "Release";

    /// <summary>
    /// Category label for downloadable addons.
    /// </summary>
    public const string AddonCategory = "Addon";

    /// <summary>
    /// Default fallback identifier used when synthesizing variant manifest IDs or missing publishers.
    /// </summary>
    public const string DefaultContentFallbackId = "content";

    /// <summary>
    /// Prefix used for synthetic file content IDs when a dedicated manifest ID is not available.
    /// </summary>
    public const string FileContentIdPrefix = "file:";

    /// <summary>
    /// Status message displayed when content download is initiated.
    /// </summary>
    public const string StartingDownloadStatusMessage = "Starting download...";

    /// <summary>
    /// Status message displayed when content download finishes successfully.
    /// </summary>
    public const string DownloadCompleteStatusMessage = "Download complete!";

    /// <summary>
    /// Dialog title displayed when attempting to launch or profile un-downloaded content.
    /// </summary>
    public const string ContentNotDownloadedTitle = "Content Not Downloaded";

    /// <summary>
    /// Release categorization keyword indicating a patch release.
    /// </summary>
    public const string PatchKeyword = "patch";

    /// <summary>
    /// Release categorization keyword indicating a hotfix release.
    /// </summary>
    public const string HotfixKeyword = "hotfix";

    /// <summary>
    /// Release categorization keyword indicating an update release.
    /// </summary>
    public const string UpdateKeyword = "update";

    /// <summary>
    /// Release categorization keyword indicating a full version release.
    /// </summary>
    public const string FullVersionKeyword = "full version";

    /// <summary>
    /// Release categorization keyword indicating a full release.
    /// </summary>
    public const string FullKeyword = "full";

    /// <summary>
    /// Release categorization keyword indicating a standalone release.
    /// </summary>
    public const string StandaloneKeyword = "standalone";

    /// <summary>
    /// Global default game type when no target game is explicitly specified.
    /// </summary>
    public const GameType DefaultGameType = GameType.ZeroHour;

    /// <summary>
    /// Category identifier for static publisher feeds.
    /// </summary>
    public const string CategoryStatic = "static";

    /// <summary>
    /// Category identifier for dynamic publisher feeds.
    /// </summary>
    public const string CategoryDynamic = "dynamic";

    /// <summary>
    /// Game identifier segment for Generals in catalog manifests and variant resolution.
    /// </summary>
    public const string GeneralsGameSegment = "generals";

    /// <summary>
    /// Game identifier segment for Zero Hour in catalog manifests and variant resolution.
    /// </summary>
    public const string ZeroHourGameSegment = "zerohour";

    /// <summary>
    /// Default display title for MD5 checksum.
    /// </summary>
    public const string Md5ChecksumTitle = "MD5 Checksum";

    /// <summary>
    /// Default display title for SHA-256 checksum.
    /// </summary>
    public const string Sha256ChecksumTitle = "SHA-256 Checksum";

    /// <summary>
    /// Fallback release name when file name is missing.
    /// </summary>
    public const string UnknownReleaseName = "Unknown Release";

    /// <summary>
    /// Fallback addon name when file name is missing.
    /// </summary>
    public const string UnknownAddonName = "Unknown Addon";

    /// <summary>
    /// Status message displayed when all selected items have already been downloaded.
    /// </summary>
    public const string AllSelectedContentLoadedStatusMessage = "All selected content is already downloaded";

    /// <summary>
    /// Status message displayed when attempting an operation before content is downloaded.
    /// </summary>
    public const string PleaseDownloadFirstStatusMessage = "Please download first";

    /// <summary>
    /// Status message displayed when content download was cancelled.
    /// </summary>
    public const string DownloadCancelledStatusMessage = "Download cancelled";

    /// <summary>
    /// Status message displayed when content download failed.
    /// </summary>
    public const string DownloadFailedStatusMessage = "Download failed";

    /// <summary>
    /// Status message displayed while selecting a profile.
    /// </summary>
    public const string SelectingProfileStatusMessage = "Selecting profile...";

    /// <summary>
    /// Status message displayed when no application window is available to host a dialog.
    /// </summary>
    public const string ErrorNoWindowStatusMessage = "Error: No window";

    /// <summary>
    /// Status prefix used for reporting download errors.
    /// </summary>
    public const string ErrorStatusPrefix = "Error: ";

    /// <summary>
    /// Status prefix used for reporting in-progress downloads.
    /// </summary>
    public const string DownloadingStatusPrefix = "Downloading ";

    /// <summary>
    /// Status prefix used when content has been added to a profile.
    /// </summary>
    public const string AddedToProfileStatusPrefix = "Added to ";

    /// <summary>
    /// Status prefix used when an operation failed.
    /// </summary>
    public const string FailedStatusPrefix = "Failed: ";
}
