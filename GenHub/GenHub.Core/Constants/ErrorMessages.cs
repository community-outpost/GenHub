namespace GenHub.Core.Constants;

/// <summary>
/// Error message constants.
/// </summary>
public static class ErrorMessages
{
    /// <summary>
    /// Error message for missing UploadThing token.
    /// </summary>
    public const string UploadThingTokenMissing = "UploadThing V7 Token is missing. Ensure UPLOADTHING_TOKEN is in your .env file.";

    /// <summary>
    /// Error message for ZIP validation failure.
    /// </summary>
    public const string ZipValidationFailed = "ZIP validation failed for upload: {Error}";

    /// <summary>
    /// Error message for file exceeding size limit.
    /// </summary>
    public const string FileExceedsSizeLimit = "File exceeds size limit: {Path}";

    /// <summary>
    /// Error message for failed prepare upload.
    /// </summary>
    public const string V7PrepareUploadFailed = "V7 PrepareUpload failed: {StatusCode} - {Error}";

    /// <summary>
    /// Error message for missing required fields in UploadThing response.
    /// </summary>
    public const string UploadThingMissingFields = "UploadThing V7 returned 200 OK but missing required fields. Response: {Response}";

    /// <summary>
    /// Error message for failed binary upload.
    /// </summary>
    public const string V7BinaryUploadFailed = "V7 PUT Binary Upload failed: {StatusCode} - {Error}";

    /// <summary>
    /// Error message for exception in UploadThing flow.
    /// </summary>
    public const string ExceptionInUploadThingFlow = "Exception in UploadThing V7 flow";

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
}
