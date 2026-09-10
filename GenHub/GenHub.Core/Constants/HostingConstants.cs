namespace GenHub.Core.Constants;

/// <summary>
/// Constants for publisher hosting providers and settings.
/// </summary>
public static class HostingConstants
{
    /// <summary>
    /// GitHub provider ID.
    /// </summary>
    public const string GitHub = "github";

    /// <summary>
    /// Dropbox provider ID.
    /// </summary>
    public const string Dropbox = "dropbox";

    /// <summary>
    /// Google Drive provider ID.
    /// </summary>
    public const string GoogleDrive = "google_drive";

    /// <summary>
    /// Manual hosting provider ID.
    /// </summary>
    public const string Manual = "manual";

    /// <summary>
    /// Default provider definition file name.
    /// </summary>
    public const string DefaultDefinitionFileName = "publisher.json";

    /// <summary>
    /// Default catalog file name.
    /// </summary>
    public const string DefaultCatalogFileName = "catalog.json";

    /// <summary>
    /// Base URL placeholder for pending local artifact uploads during catalog validation.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Placeholder URI constant")]
    public const string PendingUploadBaseUrl = "https://pending-upload.genhub.local/";

    /// <summary>
    /// Default URL prefix for mirror URLs.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Default URL prefix")]
    public const string DefaultMirrorUrlPrefix = "https://";

    /// <summary>
    /// URL to Google Cloud Console Credentials page for creating OAuth 2.0 Client IDs.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Official Google Cloud developer console URL")]
    public const string GoogleCloudConsoleCredentialsUrl = "https://console.cloud.google.com/apis/credentials";

    /// <summary>
    /// URL to Dropbox Developer App Console for creating apps and access tokens.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Official Dropbox developer console URL")]
    public const string DropboxAppConsoleUrl = "https://www.dropbox.com/developers/apps";

    /// <summary>
    /// Standard JSON MIME content type.
    /// </summary>
    public const string JsonContentType = "application/json";

    /// <summary>
    /// MIME content type for arbitrary binary data.
    /// </summary>
    public const string BinaryContentType = "application/octet-stream";

    /// <summary>
    /// URL template for Google Drive direct file download.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "URL template constant")]
    public const string GoogleDriveDownloadUrlTemplate = "https://drive.google.com/uc?export=download&id={0}";

    /// <summary>
    /// Buffer size for stream copy operations in bytes.
    /// </summary>
    public const int StreamCopyBufferSize = 8192;

    /// <summary>
    /// Error message returned when Google Drive provider is not authenticated.
    /// </summary>
    public const string GoogleDriveNotAuthenticated = "Not authenticated with Google Drive";

    /// <summary>
    /// Default timeout in seconds for external catalog fetch operations.
    /// </summary>
    public const int CatalogFetchTimeoutSeconds = 30;
}
