namespace GenHub.Core.Constants;

/// <summary>
/// Constants for mod builder directory names, file names, default configurations, and pipeline stages.
/// </summary>
public static class ModBuilderConstants
{
    /// <summary>
    /// Default project file extension.
    /// </summary>
    public const string ProjectFileExtension = ".mbproj";

    /// <summary>
    /// File pattern for project selection dialogs.
    /// </summary>
    public const string ProjectFilePattern = "*.mbproj";

    /// <summary>
    /// Install manifest file name stored in target game directory.
    /// </summary>
    public const string InstallManifestFileName = ".modbuilder_install.json";

    /// <summary>
    /// Backup file extension used during file installation.
    /// </summary>
    public const string BackupFileExtension = ".modbuilder_backup";

    /// <summary>
    /// Default directory name for build output.
    /// </summary>
    public const string DefaultBuildDir = ".Build";

    /// <summary>
    /// Default directory name for release output.
    /// </summary>
    public const string DefaultReleaseDir = ".Release";

    /// <summary>
    /// Subdirectory name for raw bundle items within build directory.
    /// </summary>
    public const string RawBundleItemsSubdir = "raw_bundle_items";

    /// <summary>
    /// Subdirectory name for compiled big bundles within build directory.
    /// </summary>
    public const string BundlesSubdir = "bundles";

    /// <summary>
    /// Subdirectory name for bundle packs within build directory.
    /// </summary>
    public const string BundlePacksSubdir = "bundle_packs";

    /// <summary>
    /// Directory name for edited game source files.
    /// </summary>
    public const string GameFilesEditedDir = "GameFilesEdited";

    /// <summary>
    /// Directory name for project configuration files.
    /// </summary>
    public const string ConfigDir = "Configs";

    /// <summary>
    /// File name for bundle items configuration.
    /// </summary>
    public const string BundleItemsConfigFileName = "ModBundleItems.json";

    /// <summary>
    /// File name for bundle packs configuration.
    /// </summary>
    public const string BundlePacksConfigFileName = "ModBundlePacks.json";

    /// <summary>
    /// Directory name for uncompressed release files.
    /// </summary>
    public const string ReleaseFilesDir = "ReleaseFiles";

    /// <summary>
    /// Directory name for project resources.
    /// </summary>
    public const string ResourcesDir = "Resources";

    /// <summary>
    /// Subdirectory name for file hash registry files within resources.
    /// </summary>
    public const string FileHashRegistrySubdir = "FileHashRegistry";

    /// <summary>
    /// Default streaming threshold size in bytes (10MB).
    /// </summary>
    public const long DefaultStreamingThresholdBytes = 10 * 1024 * 1024;
}
