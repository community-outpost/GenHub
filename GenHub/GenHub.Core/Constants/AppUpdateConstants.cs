namespace GenHub.Core.Constants;

/// <summary>
/// Constants related to application updates and Velopack.
/// </summary>
public static class AppUpdateConstants
{
    /// <summary>
    /// Maximum number of HTTP retries for failed requests.
    /// </summary>
    public const int MaxHttpRetries = 3;

    /// <summary>
    /// Velopack directory name.
    /// </summary>
    public const string VelopackDirectory = "velopack";

    /// <summary>
    /// Artifact name prefix for Windows builds.
    /// </summary>
    public const string ArtifactPrefixWindows = "genhub-velopack-windows-";

    /// <summary>
    /// Artifact name prefix for Linux builds.
    /// </summary>
    public const string ArtifactPrefixLinux = "genhub-velopack-linux-";

    /// <summary>
    /// Artifact name for release builds.
    /// </summary>
    public const string ArtifactNameRelease = "GenHub-Release";

    /// <summary>
    /// Platform string for Windows.
    /// </summary>
    public const string PlatformWindows = "windows";

    /// <summary>
    /// Platform string for Linux.
    /// </summary>
    public const string PlatformLinux = "linux";

    /// <summary>
    /// Update checking message.
    /// </summary>
    public const string CheckingForUpdatesMessage = "Checking...";

    /// <summary>
    /// Update available title format string.
    /// </summary>
    public const string UpdateAvailableTitleFormat = "Update available: v{0}";

    /// <summary>
    /// Update up to date message.
    /// </summary>
    public const string UpdateUpToDateMessage = "You're up to date!";

    /// <summary>
    /// Update check failed message.
    /// </summary>
    public const string UpdateCheckFailedMessage = "Update check failed";

    /// <summary>
    /// Installing message.
    /// </summary>
    public const string InstallingMessage = "Installing...";

    /// <summary>
    /// Install update action text.
    /// </summary>
    public const string InstallUpdateAction = "Install Update";

    /// <summary>
    /// Initializing message.
    /// </summary>
    public const string InitializingMessage = "Initializing...";

    /// <summary>
    /// Ready to restart message.
    /// </summary>
    public const string ReadyToRestartMessage = "Ready to restart";

    /// <summary>
    /// Downloading format string.
    /// </summary>
    public const string DownloadingFormat = "Downloading... {0}%";

    /// <summary>
    /// Update downloaded and restarting message.
    /// </summary>
    public const string UpdateDownloadedRestartingMessage = "Update downloaded! Restarting application...";

    /// <summary>
    /// Update complete and restarting message.
    /// </summary>
    public const string UpdateCompleteRestartingMessage = "Update complete! Restarting...";

    /// <summary>
    /// Downloading update status message.
    /// </summary>
    public const string DownloadingUpdateMessage = "Downloading update...";

    /// <summary>
    /// Cannot install from location status message.
    /// </summary>
    public const string CannotInstallFromLocationMessage = "Cannot install from this location";

    /// <summary>
    /// Update failed status message.
    /// </summary>
    public const string UpdateFailedMessage = "Update failed";

    /// <summary>
    /// Installation failed status message.
    /// </summary>
    public const string InstallationFailedMessage = "Installation failed";

    /// <summary>
    /// No artifact available status message.
    /// </summary>
    public const string NoArtifactAvailableMessage = "No artifact available";

    /// <summary>
    /// No versions found dropdown placeholder.
    /// </summary>
    public const string NoVersionsFoundMessage = "No versions found";

    /// <summary>
    /// Loading versions dropdown placeholder.
    /// </summary>
    public const string LoadingVersionsMessage = "Loading versions...";

    /// <summary>
    /// Select a version dropdown placeholder.
    /// </summary>
    public const string SelectVersionMessage = "Select a version";

    /// <summary>
    /// Not available string (N/A).
    /// </summary>
    public const string NotAvailable = "N/A";

    /// <summary>
    /// Update installation requires app installed message format.
    /// {0}: BaseDirectory, {1}: LatestVersion.
    /// </summary>
    public const string UpdateInstallationRequiresAppInstalledMessage =
        "Update installation requires the app to be installed.\n\n" +
        "You are running from: {0}\n\n" +
        "To enable updates:\n" +
        "1. Download GenHub-win-Setup.exe from GitHub releases\n" +
        "2. Run Setup.exe to install GenHub properly\n" +
        "3. Launch the installed version (will be in %LOCALAPPDATA%\\GenHub)\n\n" +
        "Update available: v{1}";

    /// <summary>
    /// Delay before exit after applying update (5 seconds).
    /// </summary>
    public static readonly TimeSpan PostUpdateExitDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Cache duration for update checks (1 hour).
    /// </summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
}