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
    /// Standard JSON MIME content type.
    /// </summary>
    public const string JsonContentType = "application/json";

    /// <summary>
    /// Error message returned when Google Drive provider is not authenticated.
    /// </summary>
    public const string GoogleDriveNotAuthenticated = "Not authenticated with Google Drive";

    /// <summary>
    /// Default timeout in seconds for external catalog fetch operations.
    /// </summary>
    public const int CatalogFetchTimeoutSeconds = 30;
}
