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
    /// Progress percentage for completion (100%).
    /// </summary>
    public const int ProgressStepCompleted = 100;
}