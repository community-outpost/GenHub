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
    public const string LogsCapitalizedDirectoryName = "Logs";

    /// <summary>
    /// Lowercase cache directory name.
    /// </summary>
    public const string CacheLowercaseDirectoryName = "cache";

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
}
