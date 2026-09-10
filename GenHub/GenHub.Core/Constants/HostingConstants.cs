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
    /// URL to Google Auth Platform Overview / Branding page for setting up Project Configuration and consent screen.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Official Google Auth Platform console URL")]
    public const string GoogleAuthPlatformUrl = "https://console.cloud.google.com/auth/overview";

    /// <summary>
    /// URL to Google Cloud Console Credentials page for creating OAuth 2.0 Client IDs.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Official Google Cloud developer console URL")]
    public const string GoogleCloudConsoleCredentialsUrl = "https://console.cloud.google.com/apis/credentials";

    /// <summary>
    /// URL to Dropbox Developer App Console for managing apps.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Official Dropbox developer console URL")]
    public const string DropboxAppConsoleUrl = "https://www.dropbox.com/developers/apps";

    /// <summary>
    /// URL to Dropbox Developer Create App page.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Official Dropbox create app URL")]
    public const string DropboxCreateAppUrl = "https://www.dropbox.com/developers/apps/create";

    /// <summary>
    /// Default publisher folder path on Dropbox.
    /// </summary>
    public const string DropboxDefaultPublisherFolder = "/GenHub_Publisher";

    /// <summary>
    /// URL to Dropbox web folder for publisher assets.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Official Dropbox web home URL")]
    public const string DropboxWebFolderUrl = $"https://www.dropbox.com/home{DropboxDefaultPublisherFolder}";

    /// <summary>
    /// URL to Google Drive web home.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Official Google Drive web URL")]
    public const string GoogleDriveWebHomeUrl = "https://drive.google.com/drive/my-drive";

    /// <summary>
    /// URL to GitHub Gist web home.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Official GitHub Gist web URL")]
    public const string GitHubGistWebHomeUrl = "https://gist.github.com";

    /// <summary>
    /// URL to GitHub Personal Access Token creation page pre-filled for Gists.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Official GitHub token generator URL")]
    public const string GitHubPersonalAccessTokensUrl = "https://github.com/settings/tokens/new?scopes=gist&description=GenHub+Publisher";

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
    /// Name of the directory for storing Google Drive OAuth tokens.
    /// </summary>
    public const string GoogleDriveTokenDirectoryName = "google-drive-tokens";

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

    /// <summary>
    /// Timeout in seconds for interactive browser-based OAuth authentication flows.
    /// </summary>
    public const int BrowserAuthTimeoutSeconds = 300;

    /// <summary>
    /// Storage badge/status string for external CDN assets.
    /// </summary>
    public const string StatusExternalCdn = "External CDN";

    /// <summary>
    /// Storage badge/status string for cloud hosted assets.
    /// </summary>
    public const string StatusCloudHosted = "Cloud Hosted";

    /// <summary>
    /// Storage badge/status string for assets pending upload.
    /// </summary>
    public const string StatusPendingUpload = "Pending Upload";

    /// <summary>
    /// Storage badge/status string for assets with no file or URL.
    /// </summary>
    public const string StatusNoFileOrUrl = "No File / URL";

    /// <summary>
    /// Storage badge/status string for live/online assets.
    /// </summary>
    public const string StatusLiveOnline = "Live / Online";

    /// <summary>
    /// Host patterns considered first-party cloud provider hosts.
    /// </summary>
    public static readonly string[] CloudProviderHostPatterns =
    [
        "drive.google.com",
        "docs.google.com",
        "googleusercontent.com",
        "github.com",
        "githubusercontent.com",
        "dropbox.com",
        "dropboxusercontent.com",
    ];
}
