namespace GenHub.Core.Constants;

/// <summary>
/// Log message constants.
/// </summary>
public static class LogMessages
{
    /// <summary>
    /// Log message for identifying URL source.
    /// </summary>
    public const string IdentifyingUrlSource = "Identifying source for URL: {Url}, Source: {Source}";

    /// <summary>
    /// Log message for failed URL extraction.
    /// </summary>
    public const string FailedToExtractDownloadUrl = "Failed to extract download URL from: {Url}";

    /// <summary>
    /// Log message for missing replay link on Generals Online.
    /// </summary>
    public const string CouldNotFindReplayLinkGeneralsOnline = "Could not find replay link on Generals Online page: {Url}";

    /// <summary>
    /// Log message for missing replay link on GenTool.
    /// </summary>
    public const string CouldNotFindReplayLinkGenTool = "Could not find replay link on GenTool page: {Url}";

    /// <summary>
    /// Log message for creating replay directory.
    /// </summary>
    public const string CreatingReplayDirectory = "Creating replay directory: {Path}";

    /// <summary>
    /// Log message for deleted replay.
    /// </summary>
    public const string DeletedReplay = "Deleted replay: {Path}";

    /// <summary>
    /// Log message for failed replay deletion.
    /// </summary>
    public const string FailedToDeleteReplay = "Failed to delete replay: {Path}";

    /// <summary>
    /// Log message for uploading to UploadThing.
    /// </summary>
    public const string UploadingToUploadThing = "Uploading to UploadThing V7: {Path}";

    /// <summary>
    /// Log message for successful UploadThing upload.
    /// </summary>
    public const string UploadThingSuccessful = "UploadThing V7 successful. Public URL: {Url}";

    /// <summary>
    /// Log message for failed ZIP creation.
    /// </summary>
    public const string FailedToCreateZip = "Failed to create ZIP: {Path}";

    /// <summary>
    /// Log message for detected ZIP file.
    /// </summary>
    public const string DetectedZipFile = "Detected ZIP file, extracting contents";

    /// <summary>
    /// Log message for failed import from ZIP.
    /// </summary>
    public const string FailedToImportFromZip = "Failed to import from ZIP: {Path}";

    /// <summary>
    /// Log message for failed stream import.
    /// </summary>
    public const string FailedToImportStream = "Failed to import stream for file: {FileName}";
}
