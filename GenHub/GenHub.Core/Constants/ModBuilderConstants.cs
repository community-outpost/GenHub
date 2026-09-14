using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for mod builder directory names, file names, default configurations, and pipeline stages.
/// </summary>
public static class ModBuilderConstants
{
    /// <summary>
    /// Default project version string.
    /// </summary>
    public const string DefaultProjectVersion = "1.0.0";

    /// <summary>
    /// Default project name for untitled projects.
    /// </summary>
    public const string UntitledProjectName = "Untitled Project";

    /// <summary>
    /// Subdirectory name for user sample projects.
    /// </summary>
    public const string SamplesDirectoryName = "Samples";

    /// <summary>
    /// Installation type identifier for Generals.
    /// </summary>
    public const string GeneralsInstallationType = "Generals";

    /// <summary>
    /// Installation type identifier for Zero Hour.
    /// </summary>
    public const string ZeroHourInstallationType = "ZeroHour";

    /// <summary>
    /// Display name for Generals installation.
    /// </summary>
    public const string GeneralsDisplayName = "Generals";

    /// <summary>
    /// Display name for Zero Hour installation.
    /// </summary>
    public const string ZeroHourDisplayName = "Zero Hour";

    /// <summary>
    /// Build configuration name for Debug.
    /// </summary>
    public const string BuildConfigurationDebug = "Debug";

    /// <summary>
    /// Build configuration name for Release.
    /// </summary>
    public const string BuildConfigurationRelease = "Release";

    /// <summary>
    /// Default project file extension.
    /// </summary>
    public const string ProjectFileExtension = ".mbproj";

    /// <summary>
    /// File pattern for project selection dialogs.
    /// </summary>
    public const string ProjectFilePattern = "*.mbproj";

    /// <summary>
    /// File name for recent projects metadata.
    /// </summary>
    public const string RecentProjectsFileName = "recent_projects.json";

    /// <summary>
    /// Directory name for ModBuilder files in application data.
    /// </summary>
    public const string ModBuilderDirName = "ModBuilder";

    /// <summary>
    /// Directory name for bundled sample projects.
    /// </summary>
    public const string SampleProjectsDirectoryName = "SampleProjects";

    /// <summary>
    /// Install manifest file name stored in target game directory.
    /// </summary>
    public const string InstallManifestFileName = ".modbuilder_install.json";

    /// <summary>
    /// File name for generated manifest JSON.
    /// </summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>
    /// Fallback directory name in temporary storage for ModBuilder builds.
    /// </summary>
    public const string FallbackTempDirName = "GenHub_ModBuilder";

    /// <summary>
    /// Backup file extension used during file installation.
    /// </summary>
    public const string BackupFileExtension = ".modbuilder_backup";

    /// <summary>
    /// Title displayed when no project is loaded.
    /// </summary>
    public const string NoProjectTitle = "No Project";

    /// <summary>
    /// Title displayed when an operation is already in progress.
    /// </summary>
    public const string OperationInProgressTitle = "Operation in Progress";

    /// <summary>
    /// Message displayed when no project is loaded.
    /// </summary>
    public const string NoProjectMessage = "Please load or create a project first";

    /// <summary>
    /// Status message for ready state.
    /// </summary>
    public const string ReadyStatus = "Ready";

    /// <summary>
    /// Fallback error message for unknown build errors.
    /// </summary>
    public const string UnknownError = "Unknown error";

    /// <summary>
    /// Error message when project path is empty.
    /// </summary>
    public const string ProjectPathEmptyError = "Project path cannot be empty";

    /// <summary>
    /// Default directory name for build output.
    /// </summary>
    public const string DefaultBuildDir = ".Build";

    /// <summary>
    /// Default directory name for release output.
    /// </summary>
    public const string DefaultReleaseDir = ".Release";

    /// <summary>
    /// Directory prefix for staging directories.
    /// </summary>
    public const string StagingDirectoryPrefix = ".staging";

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
    /// Legacy or alternate lowercase directory name for project configuration files.
    /// </summary>
    public const string LowercaseConfigDir = "config";

    /// <summary>
    /// Alternate lowercase directory name for plural configs directory.
    /// </summary>
    public const string LowercaseConfigsDir = "configs";

    /// <summary>
    /// Subdirectory name for caching sample project downloads.
    /// </summary>
    public const string SampleCacheDirName = "ModBuilderSampleCache";

    /// <summary>
    /// File name for ModFolders configuration.
    /// </summary>
    public const string ModFoldersFileName = "ModFolders.json";

    /// <summary>
    /// File name for ModJsonFiles configuration.
    /// </summary>
    public const string ModJsonFilesFileName = "ModJsonFiles.json";

    /// <summary>
    /// File name for bundles configuration.
    /// </summary>
    public const string BundlesConfigFileName = "bundles.json";

    /// <summary>
    /// Directory name for ModBuilder cache.
    /// </summary>
    public const string CacheDirectoryName = ".modbuilder_cache";

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
    /// Default bundle item name for imported game files.
    /// </summary>
    public const string DefaultImportedGameFilesItemName = "ImportedGameFiles";

    /// <summary>
    /// Default streaming threshold size in bytes (10MB).
    /// </summary>
    public const long DefaultStreamingThresholdBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Default timeout for external tool execution in seconds.
    /// </summary>
    public const int ExternalToolTimeoutSeconds = 120;

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
        Path.Combine(".tools", CrunchExecutable),
        Path.Combine("tools", CrunchExecutable),
        Path.Combine(".tools", CrunchFallbackExecutable),
        Path.Combine("tools", CrunchFallbackExecutable),
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

    /// <summary>
    /// Names of deprecated sample mod projects that should be pruned or ignored.
    /// </summary>
    public static readonly IReadOnlyList<string> DeprecatedSampleNames =
    [
        "BasicMod",
        "BalancePatch",
        "TextureOverhaul",
        "CustomIcons"
    ];

    /// <summary>
    /// Names of allowed and provisioned sample project templates.
    /// </summary>
    public static readonly IReadOnlyList<string> AllowedSampleTemplateNames =
    [
        "GeneralsGamePatch2",
        "ImprovedMenus",
        "LemonControlBar",
        "LeikezeHotkeys",
        "Hotkeys"
    ];

    /// <summary>
    /// Constants for ModBuilder sample project downloads and verification.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Centralized static URL constants repository")]
    public static class SampleProjects
    {
        /// <summary>
        /// Download URL for Generals Community Patch 2.0 core INI sample assets.
        /// </summary>
        public const string GeneralsGamePatch2Url = "https://github.com/TheSuperHackers/GeneralsGamePatch2/releases/download/1.0.1/500_900_CommunityPatch_CoreINI.zip";

        /// <summary>
        /// Expected SHA256 hash for Generals Community Patch 2.0 core INI BIG archive.
        /// </summary>
        public const string GeneralsGamePatch2Sha256 = "6a02aca9aebe6602b3e4bb76bf6e2cf35086a33fec7c6f000d8e7a4048629775";

        /// <summary>
        /// Download URL for Improved Menus widescreen sample assets.
        /// </summary>
        public const string ImprovedMenusUrl = "https://github.com/ElTioRata/ImprovedMenus/releases/download/v1.3/0_ImprovedMenusEnglish.zip";

        /// <summary>
        /// Expected SHA256 hash for Improved Menus widescreen BIG archive.
        /// </summary>
        public const string ImprovedMenusSha256 = "3280056a2d7cf9bc5cbe8d4ac18fb082846e6db11ad7bb5c60f7c4619353f0a4";

        /// <summary>
        /// Download URL for Lemon Control Bar (1080p) sample assets.
        /// </summary>
        public const string LemonControlBarUrl = "https://github.com/L3-M/GeneralsControlBar/releases/download/v1.3/ControlBarProLemonEditionZH_v1.3_1920x1080.zip";

        /// <summary>
        /// Expected SHA256 hash for Lemon Control Bar (1080p) BIG archive.
        /// </summary>
        public const string LemonControlBarSha256 = "ce169f207867aeb7594e799e1cc67abd8561a1d1b5c6cb2e59af88f4caeca828";

        /// <summary>
        /// Download URL for Leikeze Hotkeys sample assets.
        /// </summary>
        public const string LeikezeHotkeysUrl = "https://legi.cc/gp2/f/hlei.dat";

        /// <summary>
        /// Expected SHA256 hash for Leikeze Hotkeys BIG archive.
        /// </summary>
        public const string LeikezeHotkeysSha256 = "b06677d18c83c108aaa482d571c99a5aad3365c8a492067ef6eaf09364d3ab88";

        /// <summary>
        /// Download URL for Hotkeys hleg asset.
        /// </summary>
        public const string HotkeysHlegUrl = "https://legi.cc/gp2/f/hleg.dat";

        /// <summary>
        /// Download URL for Hotkeys hlen asset.
        /// </summary>
        public const string HotkeysHlenUrl = "https://legi.cc/gp2/f/hlen.dat";
    }
}
