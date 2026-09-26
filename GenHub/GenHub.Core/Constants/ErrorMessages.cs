namespace GenHub.Core.Constants;

/// <summary>
/// Error message constants.
/// </summary>
public static class ErrorMessages
{
    /// <summary>
    /// Error message for ZIP validation failure.
    /// </summary>
    public const string ZipValidationFailed = "ZIP validation failed for upload: {Error}";

    /// <summary>
    /// Error message for file exceeding size limit.
    /// </summary>
    public const string FileExceedsSizeLimit = "File exceeds size limit: {Path}";

    /// <summary>
    /// Error message for could not extract download URL.
    /// </summary>
    public const string CouldNotExtractDownloadUrl = "Could not extract download URL from the provided source.";

    /// <summary>
    /// Error message for download failed.
    /// </summary>
    public const string DownloadFailed = "Download failed.";

    /// <summary>
    /// Error message for replay exceeding size.
    /// </summary>
    public const string ReplayExceedsMaxSize = "Replay file exceeds maximum size of 1 MB ({0:F1} KB).";

    /// <summary>
    /// Error message for failed to process ZIP.
    /// </summary>
    public const string FailedToProcessZip = "Failed to process ZIP: {0}";

    /// <summary>
    /// Error message when a profile requires a game installation.
    /// </summary>
    public const string ProfileRequiresGameInstallation = "• '{0}' requires a Game Installation";

    /// <summary>
    /// Error message when a profile requires a dependency.
    /// </summary>
    public const string ProfileRequiresDependency = "• '{0}' requires '{1}'";
}
