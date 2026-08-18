using System.Collections.Generic;

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

    /// <summary>
    /// Name of the primary crunch tool executable.
    /// </summary>
    public const string CrunchExecutable = "crunch_x64.exe";

    /// <summary>
    /// Secondary fallback name of the crunch tool executable.
    /// </summary>
    public const string CrunchFallbackExecutable = "crunch.exe";

    /// <summary>
    /// DXT1 texture format identifier (no alpha).
    /// </summary>
    public const string Dxt1Format = "DXT1";

    /// <summary>
    /// DXT5 texture format identifier (with alpha).
    /// </summary>
    public const string Dxt5Format = "DXT5";

    /// <summary>
    /// Candidate search paths for the crunch tool executable.
    /// </summary>
    public static readonly IReadOnlyList<string> CrunchExecutableCandidates =
    [
        @".tools\crunch_x64.exe",
        @"tools\crunch_x64.exe",
        @".tools\crunch.exe",
        @"tools\crunch.exe",
    ];

    /// <summary>
    /// Supported texture format flags for crunch.
    /// </summary>
    public static readonly IReadOnlyList<string> CrunchTextureFormatFlags =
    [
        "-DXT1",
        "-DXT2",
        "-DXT3",
        "-DXT4",
        "-DXT5",
        "-3DC",
        "-DXN",
        "-DXT5A",
        "-DXT5_CCxY",
        "-DXT5_xGxR",
        "-DXT5_xGBR",
        "-DXT5_AGBR",
        "-DXT1A",
        "-ETC1",
        "-ETC2",
        "-ETC2A",
        "-ETC1S",
        "-ETC2AS",
        "-R8G8B8",
        "-L8",
        "-A8",
        "-A8L8",
        "-A8R8G8B8"
    ];
}

