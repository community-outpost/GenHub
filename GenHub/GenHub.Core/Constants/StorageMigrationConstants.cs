namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the storage and installation migration feature.
/// </summary>
public static class StorageMigrationConstants
{
    /// <summary>
    /// Update script resource name for Windows.
    /// </summary>
    public const string WindowsUpdateScriptName = "update_genhub.ps1";

    /// <summary>
    /// Update script resource name for Linux.
    /// </summary>
    public const string LinuxUpdateScriptName = "update_genhub.sh";

    /// <summary>
    /// Log file name for the migration assistant.
    /// </summary>
    public const string MigrationLogFileName = "migration.log";

    /// <summary>
    /// Backup directory name for the migration assistant.
    /// </summary>
    public const string MigrationBackupDirectoryName = "backup";

    /// <summary>
    /// Safety margin in bytes added to disk space calculations during migration preflight (50 MB).
    /// </summary>
    public const long DiskSpaceSafetyMarginBytes = 50 * 1024 * 1024;

    /// <summary>
    /// Preflight stage name.
    /// </summary>
    public const string StagePreflight = "Preflight Validation";

    /// <summary>
    /// Staging data stage name.
    /// </summary>
    public const string StageStagingData = "Relocating Application Data";

    /// <summary>
    /// Relocating CAS storage and workspace stage name.
    /// </summary>
    public const string StageRelocatingStorage = "Relocating CAS and Workspaces";

    /// <summary>
    /// Preparing binary migration stage name.
    /// </summary>
    public const string StagePreparingBinaries = "Preparing Binary Migration";

    /// <summary>
    /// Launching migration assistant stage name.
    /// </summary>
    public const string StageLaunchingAssistant = "Launching Migration Assistant";

    /// <summary>
    /// Finalizing stage name.
    /// </summary>
    public const string StageFinalizing = "Finalizing Migration";

    /// <summary>
    /// Velopack current directory name.
    /// </summary>
    public const string CurrentDirectoryName = "current";

    /// <summary>
    /// Velopack versioned directory prefix.
    /// </summary>
    public const string AppDirectoryPrefix = "app-";

    /// <summary>
    /// Velopack updater executable name for Windows.
    /// </summary>
    public const string VelopackUpdateExe = "Update.exe";

    /// <summary>
    /// Velopack updater executable name for Unix/Linux.
    /// </summary>
    public const string VelopackUpdateUnix = "Update";

    /// <summary>
    /// Velopack packages directory name.
    /// </summary>
    public const string VelopackPackagesDirectoryName = "packages";

    /// <summary>
    /// Search pattern for Velopack versioned application directories.
    /// </summary>
    public const string VelopackAppDirectoryPattern = "app-*";

    /// <summary>
    /// Prefix for temporary migration staging directories.
    /// </summary>
    public const string MigrationTempDirectoryPrefix = "genhub_migrate_";

    /// <summary>
    /// Lowercase logs directory name.
    /// </summary>
    public const string LogsDirectoryName = "logs";

    /// <summary>
    /// Capitalized logs directory name.
    /// </summary>
    public const string LogsCapitalizedDirectoryName = DirectoryNames.Logs;

    /// <summary>
    /// Lowercase cache directory name.
    /// </summary>
    public const string CacheLowercaseDirectoryName = "cache";

    /// <summary>
    /// Capitalized mappacks directory name.
    /// </summary>
    public const string MapPacksCapitalizedDirectoryName = "MapPacks";

    /// <summary>
    /// Lowercase mappacks directory name.
    /// </summary>
    public const string MapPacksLowercaseDirectoryName = "mappacks";

    /// <summary>
    /// Upload history file name.
    /// </summary>
    public const string UploadHistoryFileName = "upload_history.json";

    /// <summary>
    /// CAS directory marker name.
    /// </summary>
    public const string DotGenHubCasDirectoryName = ".genhub-cas";

    /// <summary>
    /// Title for the duplicate installation detected notification.
    /// </summary>
    public const string DuplicateInstallationDetectedTitle = "Duplicate Installation Detected";

    /// <summary>
    /// Auto-dismiss duration in milliseconds for the duplicate installation notification (12 seconds).
    /// </summary>
    public const int DuplicateInstallationNotificationDismissMs = 12000;

    /// <summary>
    /// Marker file name placed in the installation root during user data adoption to track pending retries.
    /// </summary>
    public const string AdoptionPendingMarkerFileName = ".adoption-pending";

    /// <summary>
    /// Environment variable name for the configured application data path override.
    /// </summary>
    public const string AppDataPathEnvVar = "GENHUB_GenHub__AppDataPath";

    /// <summary>
    /// File name used to persist custom installation location markers across platforms.
    /// </summary>
    public const string CustomInstallPathFileName = "install-location";

    /// <summary>
    /// Configuration directory name within the user's home directory.
    /// </summary>
    public const string GenHubConfigDirectoryName = ".genhub";

    /// <summary>
    /// Notification message format when user data has been successfully adopted from a custom installation.
    /// </summary>
    public const string DuplicateInstallationAdoptedMessageFormat =
        "GenHub detected a custom installation at '{0}'. Your settings, profiles, and game manifests have been preserved in this installation.";

    /// <summary>
    /// Notification message format when a duplicate installation exists but data was not adopted.
    /// </summary>
    public const string DuplicateInstallationDetectedMessageFormat =
        "GenHub is running from the default directory, but an existing installation was detected at '{0}'.";
}
