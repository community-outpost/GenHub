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
    /// Index for the Update tab in update notification views.
    /// </summary>
    public const int UpdateTabIndex = 0;

    /// <summary>
    /// Index for the Browse Builds tab in update notification views.
    /// </summary>
    public const int BrowseBuildsTabIndex = 1;

    /// <summary>
    /// Maximum valid tab index in update notification views.
    /// </summary>
    public const int MaxTabIndex = 1;

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
    /// Loading message.
    /// </summary>
    public const string LoadingMessage = "Loading...";

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
    /// Update available notification title for release channel.
    /// </summary>
    public const string UpdateAvailableNotificationTitle = "Update Available";

    /// <summary>
    /// Update available notification title for branch subscriptions.
    /// </summary>
    public const string BranchUpdateAvailableNotificationTitle = "Branch Update Available";

    /// <summary>
    /// Update available notification title for PR subscriptions.
    /// </summary>
    public const string PrUpdateAvailableNotificationTitle = "PR Update Available";

    /// <summary>
    /// Update action button text.
    /// </summary>
    public const string UpdateAction = "Update";

    /// <summary>
    /// Title for the update in progress notification.
    /// </summary>
    public const string UpdatingAppNotificationTitle = "Updating GenHub";

    /// <summary>
    /// Starting update progress message.
    /// </summary>
    public const string UpdateStartingMessage = "Starting update...";

    /// <summary>
    /// Title for update failed notification.
    /// </summary>
    public const string UpdateFailedNotificationTitle = "Update Failed";

    /// <summary>
    /// Update failed notification body format string ({0}: error message).
    /// </summary>
    public const string UpdateFailedNotificationFormat = "Failed to install update: {0}";

    /// <summary>
    /// View updates action button text.
    /// </summary>
    public const string ViewUpdatesAction = "View Updates";

    /// <summary>
    /// Release update notification body format string ({0}: version).
    /// </summary>
    public const string ReleaseUpdateNotificationFormat = "A new version ({0}) is available.";

    /// <summary>
    /// Branch update notification body format string ({0}: version, {1}: branch name).
    /// </summary>
    public const string BranchUpdateNotificationFormat = "A new build ({0}) is available on branch '{1}'.";

    /// <summary>
    /// PR update notification body format string ({0}: version, {1}: PR number).
    /// </summary>
    public const string PrUpdateNotificationFormat = "A new build ({0}) is available for PR #{1}.";

    /// <summary>
    /// Default development branch name for CI artifact fallback.
    /// </summary>
    public const string DevelopmentBranch = "development";

    /// <summary>
    /// Default main branch name for release updates.
    /// </summary>
    public const string MainBranch = "main";

    /// <summary>
    /// Log message used when a main-branch artifact check falls back to standard releases.
    /// </summary>
    public const string MainBranchReleaseFallbackLogMessage = "Main branch has no artifact update; checking standard releases instead";

    /// <summary>
    /// Update available notification title when a subscribed PR has been merged or closed.
    /// </summary>
    public const string PrMergedUpdateAvailableNotificationTitle = "PR Merged — Update Available";

    /// <summary>
    /// Update available notification title when a subscribed branch is stale or has no artifacts.
    /// </summary>
    public const string BranchStaleUpdateAvailableNotificationTitle = "Branch Fallback: Update Available";

    /// <summary>
    /// PR merged or closed fallback notification format string ({0}: version, {1}: PR number).
    /// </summary>
    public const string PrMergedUpdateNotificationFormat = "PR #{1} was merged or closed. A new build ({0}) is available on development.";

    /// <summary>
    /// PR merged or closed release fallback notification format string ({0}: version, {1}: PR number).
    /// </summary>
    public const string PrMergedReleaseNotificationFormat = "PR #{1} was merged or closed. A new release ({0}) is available.";

    /// <summary>
    /// Branch stale fallback notification format string ({0}: version, {1}: branch name).
    /// </summary>
    public const string BranchStaleUpdateNotificationFormat = "Branch '{1}' has no newer builds. A new build ({0}) is available on development.";

    /// <summary>
    /// Branch stale release fallback notification format string ({0}: version, {1}: branch name).
    /// </summary>
    public const string BranchStaleReleaseNotificationFormat = "Branch '{1}' has no newer builds. A new release ({0}) is available.";

    /// <summary>
    /// PR merged or closed status message format ({0}: PR number).
    /// </summary>
    public const string PrMergedStatusMessageFormat = "PR #{0} has been merged or closed. Select a new PR or switch to MAIN.";

    /// <summary>
    /// Branch stale status message format ({0}: branch name).
    /// </summary>
    public const string BranchStaleStatusMessageFormat = "Branch '{0}' has no available builds. Select a new branch or switch to MAIN.";

    /// <summary>
    /// Message displayed when checking branch/PR artifacts without a configured GitHub PAT.
    /// </summary>
    public const string PatRequiredForArtifactsMessage = "GitHub Personal Access Token (PAT) required to check branch/PR builds.";

    /// <summary>
    /// Identity prefix for PR update notification deduplication.
    /// </summary>
    public const string PrDedupePrefix = "pr:";

    /// <summary>
    /// Identity prefix for PR fallback update notification deduplication.
    /// </summary>
    public const string PrFallbackDedupePrefix = "pr-fallback:";

    /// <summary>
    /// Identity prefix for branch update notification deduplication.
    /// </summary>
    public const string BranchDedupePrefix = "branch:";

    /// <summary>
    /// Identity prefix for branch fallback update notification deduplication.
    /// </summary>
    public const string BranchFallbackDedupePrefix = "branch-fallback:";

    /// <summary>
    /// Identity prefix for release update notification deduplication.
    /// </summary>
    public const string ReleaseDedupePrefix = "release:";

    /// <summary>
    /// Identity prefix for GitHub API fallback update notification deduplication.
    /// </summary>
    public const string GitHubFallbackDedupePrefix = "github:";

    /// <summary>
    /// Log format string when skipping duplicate update notifications.
    /// </summary>
    public const string NotificationAlreadyShownLogFormat = "Update notification already shown for {Identity}, skipping duplicate notification";

    /// <summary>
    /// Sort option: sort by last updated date descending.
    /// </summary>
    public const string SortOptionLastUpdated = "Last Updated";

    /// <summary>
    /// Sort option: sort by pull request number descending.
    /// </summary>
    public const string SortOptionPrNumberDesc = "PR Number (Highest)";

    /// <summary>
    /// Sort option: sort by pull request number ascending.
    /// </summary>
    public const string SortOptionPrNumberAsc = "PR Number (Lowest)";

    /// <summary>
    /// Default interval in minutes for periodic update checks (30 minutes).
    /// </summary>
    public const int DefaultPeriodicUpdateCheckIntervalMinutes = 30;

    /// <summary>
    /// Minimum interval in minutes for periodic update checks (5 minutes).
    /// </summary>
    public const int MinPeriodicUpdateCheckIntervalMinutes = 5;

    /// <summary>
    /// Maximum interval in minutes for periodic update checks (10080 minutes / 7 days).
    /// </summary>
    public const int MaxPeriodicUpdateCheckIntervalMinutes = 10080;

    /// <summary>
    /// Increment step in minutes for periodic update check interval setting (5 minutes).
    /// </summary>
    public const int PeriodicUpdateCheckIntervalIncrementMinutes = 5;

    /// <summary>
    /// Default buffer size for stream operations (128KB).
    /// </summary>
    public const int DefaultStreamBufferSize = 131072;

    /// <summary>
    /// Chunk size in bytes for parallel range downloads (2MB).
    /// </summary>
    public const long DownloadChunkSizeBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Maximum number of concurrent connections for parallel downloads.
    /// </summary>
    public const int ParallelDownloadConcurrency = 8;

    /// <summary>
    /// Minimum file size threshold in bytes to trigger parallel chunked downloading (4MB).
    /// </summary>
    public const long ParallelDownloadThresholdBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Delay before exit after applying update (5 seconds).
    /// </summary>
    public static readonly TimeSpan PostUpdateExitDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Cache duration for update checks (1 hour).
    /// </summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
}
