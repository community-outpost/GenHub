using System;
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
    /// Staging directory prefix for individual bundle items.
    /// </summary>
    public const string StagingItemPrefix = ".staging_";

    /// <summary>
    /// Staging directory prefix or folder name for bundle packs.
    /// </summary>
    public const string StagingPackPrefix = ".staging_pack";

    /// <summary>
    /// Staging directory prefix or folder name for manifest generation.
    /// </summary>
    public const string StagingManifestPrefix = ".staging_manifest";

    /// <summary>
    /// File extension for JSON files.
    /// </summary>
    public const string JsonExtension = ".json";

    /// <summary>
    /// File extension for MessagePack binary cache files.
    /// </summary>
    public const string MsgPackExtension = ".msgpack";

    /// <summary>
    /// File extension for ZIP archive files.
    /// </summary>
    public const string ZipExtension = ".zip";

    /// <summary>
    /// File extension for BIG archive files.
    /// </summary>
    public const string BigExtension = ".big";

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
    /// Search pattern for BIG archive files.
    /// </summary>
    public const string BigFileSearchPattern = "*.big";

    /// <summary>
    /// Unpacked folder name used for asset staging.
    /// </summary>
    public const string UnpackedFolderName = "unpacked";

    /// <summary>
    /// Standard Window directory name.
    /// </summary>
    public const string WindowDirectoryName = "Window";

    /// <summary>
    /// Standard Art directory name.
    /// </summary>
    public const string ArtDirectoryName = "Art";

    /// <summary>
    /// Standard Data directory name.
    /// </summary>
    public const string DataDirectoryName = "Data";

    /// <summary>
    /// Standard GenTool directory name.
    /// </summary>
    public const string GenToolDirectoryName = "GenTool";

    /// <summary>
    /// Language name for English.
    /// </summary>
    public const string EnglishLanguageName = "English";

    /// <summary>
    /// Language name for German.
    /// </summary>
    public const string GermanLanguageName = "German";

    /// <summary>
    /// Language name for Russian.
    /// </summary>
    public const string RussianLanguageName = "Russian";

    /// <summary>
    /// Language name for Spanish.
    /// </summary>
    public const string SpanishLanguageName = "Spanish";

    /// <summary>
    /// Default Generals CSF file name.
    /// </summary>
    public const string GeneralsCsfFileName = "generals.csf";

    /// <summary>
    /// Default ControlBarPro documentation text file name.
    /// </summary>
    public const string ControlBarProTxtFileName = "ControlBarPro.txt";

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
    /// Constants for ModBuilder file extensions.
    /// </summary>
    public static class FileExtensions
    {
        /// <summary>File extension for BIG archive files.</summary>
        public const string Big = ".big";

        /// <summary>File extension for Blender 3D model files.</summary>
        public const string Blend = ".blend";

        /// <summary>File extension for Bitmap image files.</summary>
        public const string Bmp = ".bmp";

        /// <summary>File extension for CSF string table files.</summary>
        public const string Csf = ".csf";

        /// <summary>File extension for DirectDraw Surface texture files.</summary>
        public const string Dds = ".dds";

        /// <summary>File extension for Gzip compressed files.</summary>
        public const string Gz = ".gz";

        /// <summary>File extension for INI configuration files.</summary>
        public const string Ini = ".ini";

        /// <summary>File extension for Portable Network Graphics image files.</summary>
        public const string Png = ".png";

        /// <summary>File extension for Photoshop document files.</summary>
        public const string Psd = ".psd";

        /// <summary>File extension for raw game string table files.</summary>
        public const string Str = ".str";

        /// <summary>File extension for Truevision TGA image files.</summary>
        public const string Tga = ".tga";

        /// <summary>File extension for WAV audio files.</summary>
        public const string Wav = ".wav";

        /// <summary>File extension for Westwood 3D animated model files.</summary>
        public const string W3x = ".w3x";

        /// <summary>File extension for Westwood 3D model files.</summary>
        public const string W3d = ".w3d";

        /// <summary>File extension for Bink video files.</summary>
        public const string Bik = ".bik";

        /// <summary>File extension for window layout files.</summary>
        public const string Wnd = ".wnd";

        /// <summary>File extension for JSON files.</summary>
        public const string Json = ".json";

        /// <summary>File extension for ZIP archive files.</summary>
        public const string Zip = ".zip";

        /// <summary>File extension for ModBuilder project files.</summary>
        public const string Mbproj = ".mbproj";
    }

    /// <summary>
    /// Parameter names and values for bundle file configuration.
    /// </summary>
    public static class BundleParams
    {
        /// <summary>Parameter key indicating raw passthrough without conversion.</summary>
        public const string NoConvert = "noconvert";

        /// <summary>Parameter key indicating raw asset mode.</summary>
        public const string Raw = "raw";

        /// <summary>Parameter key indicating specified output format.</summary>
        public const string OutputFormat = "outputformat";

        /// <summary>Parameter value representing raw format.</summary>
        public const string RawValue = "RAW";
    }

    /// <summary>
    /// Conversion format identifiers used with file conversion services.
    /// </summary>
    public static class ConversionFormats
    {
        /// <summary>Format identifier for DirectDraw Surface.</summary>
        public const string Dds = "DDS";

        /// <summary>Format identifier for Compiled String File.</summary>
        public const string Csf = "CSF";

        /// <summary>Format identifier for INI configuration.</summary>
        public const string Ini = "INI";

        /// <summary>Format identifier for BIG archive.</summary>
        public const string Big = "BIG";

        /// <summary>Format identifier for raw string table.</summary>
        public const string Str = "STR";

        /// <summary>Format identifier for window definitions.</summary>
        public const string Window = "WINDOW";
    }

    /// <summary>
    /// Common directory names used across ModBuilder projects and sample packages.
    /// </summary>
    public static class DirectoryNames
    {
        /// <summary>Directory name for unpacked staging.</summary>
        public const string Unpacked = "unpacked";

        /// <summary>Directory name for window definitions.</summary>
        public const string Window = "Window";

        /// <summary>Directory name for art assets.</summary>
        public const string Art = "Art";

        /// <summary>Directory name for texture assets.</summary>
        public const string Textures = "Textures";

        /// <summary>Directory name for data and INI files.</summary>
        public const string Data = "Data";

        /// <summary>Directory name for INI configuration files.</summary>
        public const string Ini = "INI";

        /// <summary>Directory name for movie video files.</summary>
        public const string Movies = "Movies";

        /// <summary>Directory name for GenTool configuration assets.</summary>
        public const string GenTool = "GenTool";

        /// <summary>Language directory name for English.</summary>
        public const string English = "English";

        /// <summary>Language directory name for German.</summary>
        public const string German = "German";

        /// <summary>Language directory name for Russian.</summary>
        public const string Russian = "Russian";

        /// <summary>Language directory name for Spanish.</summary>
        public const string Spanish = "Spanish";

        /// <summary>Directory name for Zero Hour assets.</summary>
        public const string ZeroHour = "ZeroHour";

        /// <summary>Directory name for Generals classic assets.</summary>
        public const string Generals = "Generals";
    }

    /// <summary>
    /// Common file names and search patterns.
    /// </summary>
    public static class FileNames
    {
        /// <summary>File name for markdown README files.</summary>
        public const string ReadmeMd = "README.md";

        /// <summary>File name for text README files.</summary>
        public const string ReadmeTxt = "README.txt";

        /// <summary>File name for gitkeep placeholder files.</summary>
        public const string GitKeep = ".gitkeep";

        /// <summary>Prefix for Git internal or metadata files.</summary>
        public const string GitPrefix = ".git";

        /// <summary>Default CSF string table file name.</summary>
        public const string GeneralsCsf = "generals.csf";

        /// <summary>Default ControlBarPro documentation file name.</summary>
        public const string ControlBarProTxt = "ControlBarPro.txt";

        /// <summary>Default STR string table file name.</summary>
        public const string GeneralsStr = "generals.str";

        /// <summary>Search pattern for BIG files.</summary>
        public const string BigSearchPattern = "*.big";

        /// <summary>Search pattern for BIK video files.</summary>
        public const string BikSearchPattern = "*.bik";

        /// <summary>Search pattern for CSF string table files.</summary>
        public const string CsfSearchPattern = "*.csf";

        /// <summary>Search pattern for JSON files.</summary>
        public const string JsonSearchPattern = "*.json";

        /// <summary>Search pattern for INI configuration files.</summary>
        public const string IniSearchPattern = "*.ini";

        /// <summary>Search pattern for WND window layout files.</summary>
        public const string WndSearchPattern = "*.wnd";
    }

    /// <summary>
    /// Determines whether the given file name or path matches a placeholder or repository file that should be ignored during builds.
    /// </summary>
    /// <param name="filePathOrName">The file path or name.</param>
    /// <returns><c>true</c> if the file should be ignored; otherwise, <c>false</c>.</returns>
    public static bool IsIgnoredProjectFile(string filePathOrName)
    {
        if (string.IsNullOrWhiteSpace(filePathOrName))
        {
            return false;
        }

        var name = Path.GetFileName(filePathOrName);
        return name.Equals(FileNames.ReadmeMd, StringComparison.OrdinalIgnoreCase) ||
               name.Equals(FileNames.ReadmeTxt, StringComparison.OrdinalIgnoreCase) ||
               name.Equals(FileNames.GitKeep, StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith(FileNames.GitPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the list of base directories where sample project templates may reside.
    /// In production/installed builds, this is strictly bounded to application-relative directories.
    /// Development repository fallback is strictly gated to development environments with verified repository anchors.
    /// </summary>
    /// <returns>A list of candidate sample base directories.</returns>
    public static IReadOnlyList<string> GetSampleBaseDirectories()
    {
        var dirs = new List<string>
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SampleProjectsDirectoryName, ModBuilderDirName),
            Path.Combine(AppContext.BaseDirectory, SampleProjectsDirectoryName, ModBuilderDirName),
            Path.Combine(Directory.GetCurrentDirectory(), SampleProjectsDirectoryName, ModBuilderDirName),
        };

        if (System.Diagnostics.Debugger.IsAttached ||
            string.Equals(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase))
        {
            AddDevCandidate(dirs, AppDomain.CurrentDomain.BaseDirectory);
            AddDevCandidate(dirs, AppContext.BaseDirectory);
        }

        return dirs;
    }

    private static void AddDevCandidate(List<string> dirs, string baseDir)
    {
        try
        {
            var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            if (Directory.Exists(repoRoot) &&
                (File.Exists(Path.Combine(repoRoot, "GenHub.sln")) || Directory.Exists(Path.Combine(repoRoot, ".git"))))
            {
                var candidate = Path.Combine(repoRoot, SampleProjectsDirectoryName, ModBuilderDirName);
                if (Directory.Exists(candidate))
                {
                    dirs.Add(candidate);
                }
            }
        }
        catch
        {
            // Ignore path evaluation errors
        }
    }

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
        /// Download URLs for Improved Menus widescreen sample assets (English, Russian, Spanish).
        /// </summary>
        public const string ImprovedMenusUrl = "https://github.com/ElTioRata/ImprovedMenus/releases/download/v1.3/0_ImprovedMenusEnglish.zip";
        public const string ImprovedMenusEnglishUrl = "https://github.com/ElTioRata/ImprovedMenus/releases/download/v1.3/0_ImprovedMenusEnglish.zip";
        public const string ImprovedMenusRussianUrl = "https://github.com/ElTioRata/ImprovedMenus/releases/download/v1.3/0_ImprovedMenusRussian.zip";
        public const string ImprovedMenusSpanishUrl = "https://github.com/ElTioRata/ImprovedMenus/releases/download/v1.3/0_ImprovedMenusSpanish.zip";

        /// <summary>
        /// Expected SHA256 hash for Improved Menus widescreen BIG archive.
        /// </summary>
        public const string ImprovedMenusSha256 = "3280056a2d7cf9bc5cbe8d4ac18fb082846e6db11ad7bb5c60f7c4619353f0a4";

        /// <summary>
        /// Download URLs for Lemon Control Bar sample assets (720p, 1080p, 1440p, 4K).
        /// </summary>
        public const string LemonControlBarUrl = "https://github.com/L3-M/GeneralsControlBar/releases/download/v1.3/ControlBarProLemonEditionZH_v1.3_1920x1080.zip";
        public const string LemonControlBar720pUrl = "https://github.com/L3-M/GeneralsControlBar/releases/download/v1.3/ControlBarProLemonEditionZH_v1.3_1280x720.zip";
        public const string LemonControlBar1080pUrl = "https://github.com/L3-M/GeneralsControlBar/releases/download/v1.3/ControlBarProLemonEditionZH_v1.3_1920x1080.zip";
        public const string LemonControlBar1440pUrl = "https://github.com/L3-M/GeneralsControlBar/releases/download/v1.3/ControlBarProLemonEditionZH_v1.3_2560x1440.zip";
        public const string LemonControlBar4KUrl = "https://github.com/L3-M/GeneralsControlBar/releases/download/v1.3/ControlBarProLemonEditionZH_v1.3_3840x2160.zip";

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
