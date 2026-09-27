using GenHub.Core.Models.Enums;
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
    /// Message displayed when files cannot be imported because another operation is running.
    /// </summary>
    public const string CannotImportWhileOperationInProgress = "Cannot import files while another operation is running.";

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
    /// Structured logging template for reporting staging path escape errors.
    /// </summary>
    public const string EscapeErrorLogTemplate = "{EscapeError}";

    /// <summary>
    /// Number of staged files between build progress reports during staging loops.
    /// </summary>
    public const int StagingProgressReportInterval = 25;

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
    /// Normalized path prefix for edited game source files, using forward slashes.
    /// </summary>
    public const string GameFilesEditedPrefix = "GameFilesEdited/";

    /// <summary>
    /// Default glob pattern matching every file under the edited game sources.
    /// </summary>
    public const string GameFilesEditedAllFilesGlob = "GameFilesEdited/**/*.*";

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
    /// Sample name for Generals Game Patch 2.
    /// </summary>
    public const string GeneralsGamePatch2SampleName = "GeneralsGamePatch2";

    /// <summary>
    /// Sample name for Improved Menus.
    /// </summary>
    public const string ImprovedMenusSampleName = "ImprovedMenus";

    /// <summary>
    /// Sample name for Lemon Control Bar.
    /// </summary>
    public const string LemonControlBarSampleName = "LemonControlBar";

    /// <summary>
    /// Sample name for Leikeze Hotkeys.
    /// </summary>
    public const string LeikezeHotkeysSampleName = "LeikezeHotkeys";

    /// <summary>
    /// Sample name for Hotkeys.
    /// </summary>
    public const string HotkeysSampleName = "Hotkeys";

    /// <summary>
    /// Bundle item name for Lemon Control Bar 1080-generation art textures.
    /// </summary>
    public const string LemonControlBarArt1080ItemName = "LemonControlBarArt1080";

    /// <summary>
    /// Bundle item name for Lemon Control Bar 2160-generation art textures.
    /// </summary>
    public const string LemonControlBarArt2160ItemName = "LemonControlBarArt2160";

    /// <summary>
    /// Bundle item name for Lemon Control Bar 1080-generation data and configuration files.
    /// </summary>
    public const string LemonControlBarData1080ItemName = "LemonControlBarData1080";

    /// <summary>
    /// Bundle item name for Lemon Control Bar 2160-generation data and configuration files.
    /// </summary>
    public const string LemonControlBarData2160ItemName = "LemonControlBarData2160";

    /// <summary>
    /// Bundle item name for Lemon Control Bar shared base files (ControlBarPro.txt and GenTool data).
    /// </summary>
    public const string LemonControlBarBaseItemName = "LemonControlBarBase";

    /// <summary>
    /// Bundle item name for Lemon Control Bar 720p window layouts.
    /// </summary>
    public const string LemonControlBarWindows720pItemName = "LemonControlBarWindows_720p";

    /// <summary>
    /// Bundle item name for Lemon Control Bar 1080p window layouts.
    /// </summary>
    public const string LemonControlBarWindows1080pItemName = "LemonControlBarWindows_1080p";

    /// <summary>
    /// Bundle item name for Lemon Control Bar 1440p window layouts.
    /// </summary>
    public const string LemonControlBarWindows1440pItemName = "LemonControlBarWindows_1440p";

    /// <summary>
    /// Bundle item name for Lemon Control Bar 4K window layouts.
    /// </summary>
    public const string LemonControlBarWindows4KItemName = "LemonControlBarWindows_4K";

    /// <summary>
    /// Bundle item name for Improved Menus window definitions.
    /// </summary>
    public const string MenuWindowsItemName = "MenuWindows";

    /// <summary>
    /// Bundle item name for Improved Menus mapped image definitions.
    /// </summary>
    public const string MenuMappedImagesItemName = "MenuMappedImages";

    /// <summary>
    /// Bundle item name for Improved Menus English textures.
    /// </summary>
    public const string MenuTexturesEnglishItemName = "MenuTexturesEnglish";

    /// <summary>
    /// Bundle item name for Improved Menus Russian textures.
    /// </summary>
    public const string MenuTexturesRussianItemName = "MenuTexturesRussian";

    /// <summary>
    /// Bundle item name for Improved Menus Spanish textures.
    /// </summary>
    public const string MenuTexturesSpanishItemName = "MenuTexturesSpanish";

    /// <summary>
    /// Source wildcard pattern for loose TGA textures under Art/Textures.
    /// </summary>
    public const string ArtTexturesWildcardPattern = "Art/Textures/**/*.tga";

    /// <summary>
    /// Relative manifest path for Improved Menus English BIG archive.
    /// </summary>
    public const string ImprovedMenusEnglishManifestPath = "config/0_ImprovedMenusEnglish.big.manifest.json";

    /// <summary>
    /// Relative manifest path for Improved Menus Russian BIG archive.
    /// </summary>
    public const string ImprovedMenusRussianManifestPath = "config/0_ImprovedMenusRussian.big.manifest.json";

    /// <summary>
    /// Relative manifest path for Improved Menus Spanish BIG archive.
    /// </summary>
    public const string ImprovedMenusSpanishManifestPath = "config/0_ImprovedMenusSpanish.big.manifest.json";

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
    /// File name for bundle manifest definitions configuration.
    /// </summary>
    public const string BundleManifestsConfigFileName = "ModBundleManifests.json";

    /// <summary>
    /// Default version string for ModBuilder-created content manifests.
    /// </summary>
    public const string DefaultManifestVersion = "1.0.0";

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
    /// Buffer size for ModBuilder build-pipeline file I/O (64KB).
    /// Scoped to the build pipeline so shared consumers of <see cref="IoConstants.DefaultFileBufferSize"/>
    /// keep the application-wide default.
    /// </summary>
    public const int BuildFileBufferSize = 65536;

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
    /// Name of the Blender tool executable.
    /// </summary>
    public const string BlenderExecutable = "blender";

    /// <summary>
    /// Command-line argument format for invoking Blender in headless background mode.
    /// </summary>
    public const string BlenderExportArgumentFormat = "-b \"{0}\" -o \"{1}\" --python-exit-code 1";

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
    public const string BigFileSearchPattern = FileNames.BigSearchPattern;

    /// <summary>
    /// Unpacked folder name used for asset staging.
    /// </summary>
    public const string UnpackedFolderName = DirectoryNames.Unpacked;

    /// <summary>
    /// Standard Window directory name.
    /// </summary>
    public const string WindowDirectoryName = DirectoryNames.Window;

    /// <summary>
    /// Standard Art directory name.
    /// </summary>
    public const string ArtDirectoryName = DirectoryNames.Art;

    /// <summary>
    /// Standard Data directory name.
    /// </summary>
    public const string DataDirectoryName = DirectoryNames.Data;

    /// <summary>
    /// Standard GenTool directory name.
    /// </summary>
    public const string GenToolDirectoryName = DirectoryNames.GenTool;

    /// <summary>
    /// Language name for English.
    /// </summary>
    public const string EnglishLanguageName = DirectoryNames.English;

    /// <summary>
    /// Language name for German.
    /// </summary>
    public const string GermanLanguageName = DirectoryNames.German;

    /// <summary>
    /// Language name for Russian.
    /// </summary>
    public const string RussianLanguageName = DirectoryNames.Russian;

    /// <summary>
    /// Language name for Spanish.
    /// </summary>
    public const string SpanishLanguageName = DirectoryNames.Spanish;

    /// <summary>
    /// Default Generals CSF file name.
    /// </summary>
    public const string GeneralsCsfFileName = FileNames.GeneralsCsf;

    /// <summary>
    /// Default ControlBarPro documentation text file name.
    /// </summary>
    public const string ControlBarProTxtFileName = FileNames.ControlBarProTxt;

    /// <summary>
    /// Legacy alias template name for ControlBar.
    /// </summary>
    public const string ControlBarAlias = "ControlBar";

    /// <summary>
    /// Legacy alias template name for CustomIcons.
    /// </summary>
    public const string CustomIconsAlias = "CustomIcons";

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
        GeneralsGamePatch2SampleName,
        ImprovedMenusSampleName,
        LemonControlBarSampleName,
        LeikezeHotkeysSampleName,
        HotkeysSampleName
    ];

    /// <summary>
    /// Content types selectable for ModBuilder projects and bundle manifests.
    /// </summary>
    public static readonly IReadOnlyList<ContentType> AvailableContentTypes =
    [
        ContentType.Mod,
        ContentType.Patch,
        ContentType.Addon,
        ContentType.MapPack,
        ContentType.LanguagePack,
        ContentType.ModdingTool,
    ];

    /// <summary>
    /// Target games selectable for ModBuilder projects and bundle manifests.
    /// </summary>
    public static readonly IReadOnlyList<GameType> AvailableTargetGames =
    [
        GameType.ZeroHour,
        GameType.Generals,
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

        /// <summary>File extension for TIFF image files.</summary>
        public const string Tif = ".tif";

        /// <summary>File extension for TIFF image files.</summary>
        public const string Tiff = ".tiff";

        /// <summary>File extension for plain text files.</summary>
        public const string Txt = ".txt";

        /// <summary>File extension for JPEG image files.</summary>
        public const string Jpg = ".jpg";

        /// <summary>File extension for JPEG image files.</summary>
        public const string Jpeg = ".jpeg";
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

        /// <summary>Search pattern for DDS texture files.</summary>
        public const string DdsSearchPattern = "*.dds";

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
            var current = new DirectoryInfo(baseDir);
            for (var i = 0; i < 6 && current != null; i++)
            {
                var candidate = Path.Combine(current.FullName, SampleProjectsDirectoryName, ModBuilderDirName);
                if (Directory.Exists(candidate))
                {
                    dirs.Add(candidate);
                    break;
                }

                current = current.Parent;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
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
        /// Environment variable name for overriding the GeneralsGamePatch2 download URL.
        /// </summary>
        public const string GeneralsGamePatch2UrlEnvVar = "GENHUB_MODBUILDER_GENERALSGAMEPATCH2_URL";

        /// <summary>
        /// Default download URL for Generals Community Patch 2.0 core INI sample assets.
        /// </summary>
        public const string DefaultGeneralsGamePatch2Url = "https://github.com/TheSuperHackers/GeneralsGamePatch2/releases/download/1.0.1/500_900_CommunityPatch_CoreINI.zip";

        /// <summary>
        /// Expected SHA256 hash for Generals Community Patch 2.0 core INI BIG archive.
        /// </summary>
        public const string GeneralsGamePatch2Sha256 = "6a02aca9aebe6602b3e4bb76bf6e2cf35086a33fec7c6f000d8e7a4048629775";

        /// <summary>
        /// Environment variable name for overriding the Improved Menus English download URL.
        /// </summary>
        public const string ImprovedMenusEnglishUrlEnvVar = "GENHUB_MODBUILDER_IMPROVEDMENUS_ENGLISH_URL";

        /// <summary>
        /// Default download URL for Improved Menus widescreen English sample assets.
        /// </summary>
        public const string DefaultImprovedMenusEnglishUrl = "https://github.com/ElTioRata/ImprovedMenus/releases/download/v1.3/0_ImprovedMenusEnglish.zip";

        /// <summary>
        /// Environment variable name for overriding the Improved Menus Russian download URL.
        /// </summary>
        public const string ImprovedMenusRussianUrlEnvVar = "GENHUB_MODBUILDER_IMPROVEDMENUS_RUSSIAN_URL";

        /// <summary>
        /// Default download URL for Improved Menus Russian assets.
        /// </summary>
        public const string DefaultImprovedMenusRussianUrl = "https://github.com/ElTioRata/ImprovedMenus/releases/download/v1.3/0_ImprovedMenusRussian.zip";

        /// <summary>
        /// Environment variable name for overriding the Improved Menus Spanish download URL.
        /// </summary>
        public const string ImprovedMenusSpanishUrlEnvVar = "GENHUB_MODBUILDER_IMPROVEDMENUS_SPANISH_URL";

        /// <summary>
        /// Default download URL for Improved Menus Spanish assets.
        /// </summary>
        public const string DefaultImprovedMenusSpanishUrl = "https://github.com/ElTioRata/ImprovedMenus/releases/download/v1.3/0_ImprovedMenusSpanish.zip";

        /// <summary>
        /// Expected SHA256 hash for Improved Menus widescreen BIG archive.
        /// </summary>
        public const string ImprovedMenusSha256 = "3280056a2d7cf9bc5cbe8d4ac18fb082846e6db11ad7bb5c60f7c4619353f0a4";

        /// <summary>
        /// Expected SHA256 hash for Improved Menus Russian BIG archive.
        /// </summary>
        public const string ImprovedMenusRussianSha256 = "9b5bd315545a9fee17582f7dca4962f1004ea1b40699d3582845af66ac414715";

        /// <summary>
        /// Expected SHA256 hash for Improved Menus Spanish BIG archive.
        /// </summary>
        public const string ImprovedMenusSpanishSha256 = "515649ee2db438aaf5a02a8bb2a1b24cc1ce3d4f4aa4bd635d5ff927bbf7a003";

        /// <summary>
        /// Environment variable name for overriding the Lemon Control Bar 720p download URL.
        /// </summary>
        public const string LemonControlBar720pUrlEnvVar = "GENHUB_MODBUILDER_LEMONCONTROLBAR_720P_URL";

        /// <summary>
        /// Default download URL for Lemon Control Bar 720p assets.
        /// </summary>
        public const string DefaultLemonControlBar720pUrl = "https://github.com/L3-M/GeneralsControlBar/releases/download/v1.3/ControlBarProLemonEditionZH_v1.3_1280x720.zip";

        /// <summary>
        /// Environment variable name for overriding the Lemon Control Bar 1080p download URL.
        /// </summary>
        public const string LemonControlBar1080pUrlEnvVar = "GENHUB_MODBUILDER_LEMONCONTROLBAR_1080P_URL";

        /// <summary>
        /// Default download URL for Lemon Control Bar 1080p sample assets.
        /// </summary>
        public const string DefaultLemonControlBar1080pUrl = "https://github.com/L3-M/GeneralsControlBar/releases/download/v1.3/ControlBarProLemonEditionZH_v1.3_1920x1080.zip";

        /// <summary>
        /// Environment variable name for overriding the Lemon Control Bar 1440p download URL.
        /// </summary>
        public const string LemonControlBar1440pUrlEnvVar = "GENHUB_MODBUILDER_LEMONCONTROLBAR_1440P_URL";

        /// <summary>
        /// Default download URL for Lemon Control Bar 1440p assets.
        /// </summary>
        public const string DefaultLemonControlBar1440pUrl = "https://github.com/L3-M/GeneralsControlBar/releases/download/v1.3/ControlBarProLemonEditionZH_v1.3_2560x1440.zip";

        /// <summary>
        /// Environment variable name for overriding the Lemon Control Bar 4K download URL.
        /// </summary>
        public const string LemonControlBar4KUrlEnvVar = "GENHUB_MODBUILDER_LEMONCONTROLBAR_4K_URL";

        /// <summary>
        /// Default download URL for Lemon Control Bar 4K assets.
        /// </summary>
        public const string DefaultLemonControlBar4KUrl = "https://github.com/L3-M/GeneralsControlBar/releases/download/v1.3/ControlBarProLemonEditionZH_v1.3_3840x2160.zip";

        /// <summary>
        /// Expected SHA256 hash for Lemon Control Bar (1080p) BIG archive.
        /// </summary>
        public const string LemonControlBarSha256 = "ce169f207867aeb7594e799e1cc67abd8561a1d1b5c6cb2e59af88f4caeca828";

        /// <summary>
        /// Expected SHA256 hash for Lemon Control Bar 720p window BIG archive.
        /// </summary>
        public const string LemonControlBar720pSha256 = "d79d8be0448461adeba69f4def5bf9a759bfea1266d7c0f5af6bd5287abeb221";

        /// <summary>
        /// Expected SHA256 hash for Lemon Control Bar 1440p window BIG archive.
        /// </summary>
        public const string LemonControlBar1440pSha256 = "2dfd213ea9011363746d27b30feb05f5e692c665104bc4ea0c5a3597af804eb1";

        /// <summary>
        /// Expected SHA256 hash for Lemon Control Bar 4K (2160p) window BIG archive.
        /// </summary>
        public const string LemonControlBar2160Sha256 = "d2ea1be2ec22ff6756c8655c1ea8cd07b581f4da9755dbe5bb97b1da803dd770";

        /// <summary>
        /// Expected SHA256 hash for Lemon Control Bar 1080-generation art BIG archive.
        /// </summary>
        public const string LemonControlBarArt1080Sha256 = "935ea092e15f96300c155e1277c7b758d5e80f2e13cea084102d9ee1707c158f";

        /// <summary>
        /// Expected SHA256 hash for Lemon Control Bar 1080-generation data BIG archive.
        /// </summary>
        public const string LemonControlBarData1080Sha256 = "e46dfd902b98455d697b71588dd150dc9b7139a886a1cb40ea6afa860147b56a";

        /// <summary>
        /// Expected SHA256 hash for Lemon Control Bar 2160-generation art BIG archive.
        /// </summary>
        public const string LemonControlBarArt2160Sha256 = "bb72394d20d3eea65d34d149bbf0e0578b48ff0aabdafdac3c9881da0c3fd060";

        /// <summary>
        /// Expected SHA256 hash for Lemon Control Bar 2160-generation data BIG archive.
        /// </summary>
        public const string LemonControlBarData2160Sha256 = "9bbe25446eeb500b96a5d45b2fab4d9438b12a19f3d04532fb45107f70847ef0";

        /// <summary>
        /// Expected SHA256 hash for Lemon Control Bar shared base BIG archive (ControlBarPro.txt and GenTool data).
        /// </summary>
        public const string LemonControlBarBaseSha256 = "e2c98cf602b89f0963dd8afbce82b7bebd124dd3c2420578724b86d26217a150";

        /// <summary>
        /// Shared base BIG filename carried by every Lemon Control Bar resolution zip.
        /// </summary>
        public const string LemonControlBarBaseBigFileName = "340_ControlBarProLemonEditionZH.big";

        /// <summary>
        /// Filename marker identifying Lemon Control Bar art BIG archives.
        /// </summary>
        public const string LemonArtBigMarker = "Art";

        /// <summary>
        /// Filename marker identifying Lemon Control Bar data BIG archives.
        /// </summary>
        public const string LemonDataBigMarker = "Data";

        /// <summary>
        /// Generation directory for 1080-generation Lemon assets inside GameFilesEdited.
        /// </summary>
        public const string LemonGen1080Dir = "Gen1080";

        /// <summary>
        /// Generation directory for 2160-generation Lemon assets inside GameFilesEdited.
        /// </summary>
        public const string LemonGen2160Dir = "Gen2160";

        /// <summary>
        /// Prefix for per-resolution Lemon directories inside GameFilesEdited (for example Res1080p).
        /// </summary>
        public const string LemonResolutionDirPrefix = "Res";

        /// <summary>
        /// Environment variable name for overriding the Leikeze Hotkeys download URL.
        /// </summary>
        public const string LeikezeHotkeysUrlEnvVar = "GENHUB_MODBUILDER_LEIKEZEHOTKEYS_URL";

        /// <summary>
        /// Default download URL for Leikeze Hotkeys sample assets.
        /// </summary>
        public const string DefaultLeikezeHotkeysUrl = "https://legi.cc/gp2/f/hlei.dat";

        /// <summary>
        /// Expected SHA256 hash for Leikeze Hotkeys source archive (hlei.dat).
        /// </summary>
        public const string LeikezeHotkeysSha256 = "a2450942bbf0ec2d8b3f62eb427844b65653d9aef103288449472390cc411215";

        /// <summary>
        /// Release BIG filename for Leikeze Zero Hour English hotkeys.
        /// </summary>
        public const string LeikezeHotkeysZhEnBigFileName = "!HotkeysLeikezeENZH.big";

        /// <summary>
        /// Release BIG filename for Leikeze Generals English hotkeys.
        /// </summary>
        public const string LeikezeHotkeysGeneralsEnBigFileName = "!HotkeysLeikezeEN.big";

        /// <summary>
        /// Release BIG filename for Leikeze Zero Hour German hotkeys.
        /// </summary>
        public const string LeikezeHotkeysZhDeBigFileName = "!HotkeysLeikezeDEZH.big";

        /// <summary>
        /// Environment variable name for overriding the Hotkeys hleg download URL.
        /// </summary>
        public const string HotkeysHlegUrlEnvVar = "GENHUB_MODBUILDER_HOTKEYS_HLEG_URL";

        /// <summary>
        /// Default download URL for Hotkeys hleg asset.
        /// </summary>
        public const string DefaultHotkeysHlegUrl = "https://legi.cc/gp2/f/hleg.dat";

        /// <summary>
        /// Expected SHA256 hash for Hotkeys hleg asset.
        /// </summary>
        public const string HotkeysHlegSha256 = "69693e098b309bbdb67968926234f725580bb7401cf350cce325542e361b4530";

        /// <summary>
        /// Environment variable name for overriding the Hotkeys hlen download URL.
        /// </summary>
        public const string HotkeysHlenUrlEnvVar = "GENHUB_MODBUILDER_HOTKEYS_HLEN_URL";

        /// <summary>
        /// Default download URL for Hotkeys hlen asset.
        /// </summary>
        public const string DefaultHotkeysHlenUrl = "https://legi.cc/gp2/f/hlen.dat";

        /// <summary>
        /// Expected SHA256 hash for Hotkeys hlen asset.
        /// </summary>
        public const string HotkeysHlenSha256 = "60647478fc0494cac46934fad1a830e522f90475b21feca5bf896c3902a02033";

        /// <summary>
        /// Display name for the Generals Community Patch 2.0 showcase sample.
        /// </summary>
        public const string GeneralsGamePatch2DisplayName = "Generals Community Patch 2.0";

        /// <summary>
        /// Publisher name for the Generals Community Patch 2.0 showcase sample.
        /// </summary>
        public const string GeneralsGamePatch2Publisher = "TheSuperHackers";

        /// <summary>
        /// Expected output file name for the Generals Community Patch 2.0 showcase sample.
        /// </summary>
        public const string GeneralsGamePatch2OutputFileName = "500_900_CommunityPatch_CoreINI.big";

        /// <summary>
        /// Display name for the Improved Menus showcase sample.
        /// </summary>
        public const string ImprovedMenusDisplayName = "Improved Menus Widescreen";

        /// <summary>
        /// Publisher name for the Improved Menus showcase sample.
        /// </summary>
        public const string ImprovedMenusPublisher = "ElTioRata";

        /// <summary>
        /// Expected output file name for the Improved Menus showcase sample.
        /// </summary>
        public const string ImprovedMenusOutputFileName = "0_ImprovedMenusEnglish.big";

        /// <summary>
        /// Display name for the Lemon Control Bar showcase sample.
        /// </summary>
        public const string LemonControlBarDisplayName = "Lemon Control Bar";

        /// <summary>
        /// Publisher name for the Lemon Control Bar showcase sample.
        /// </summary>
        public const string LemonControlBarPublisher = "L3-M (Lemon)";

        /// <summary>
        /// Expected output file name for the Lemon Control Bar showcase sample.
        /// </summary>
        public const string LemonControlBarOutputFileName = "340_ControlBarProLemonEdition1080ZH.big";

        /// <summary>
        /// Display name for the Leikeze Hotkeys showcase sample.
        /// </summary>
        public const string LeikezeHotkeysDisplayName = "Leikeze Competitive Hotkeys";

        /// <summary>
        /// Publisher name for the Leikeze Hotkeys showcase sample.
        /// </summary>
        public const string LeikezeHotkeysPublisher = "Leikeze";

        /// <summary>
        /// Expected output file name for the Leikeze Hotkeys showcase sample.
        /// </summary>
        public const string LeikezeHotkeysOutputFileName = "!HotkeysLeikezeENZH.big";

        /// <summary>
        /// Pack name for the Improved Menus English variant.
        /// </summary>
        public const string ImprovedMenusEnglishPack = "ImprovedMenus_English";

        /// <summary>
        /// Pack name for the Improved Menus Russian variant.
        /// </summary>
        public const string ImprovedMenusRussianPack = "ImprovedMenus_Russian";

        /// <summary>
        /// Pack name for the Leikeze Hotkeys Zero Hour English variant.
        /// </summary>
        public const string LeikezeHotkeysZhEnPack = "LeikezeHotkeys_ZH_EN";

        /// <summary>
        /// Item name for the Leikeze Hotkeys Zero Hour English content.
        /// </summary>
        public const string HotkeysZhEnglishItem = "Hotkeys_ZH_English";

        /// <summary>
        /// Legacy marker found in stale Generals Community Patch 2.0 configs.
        /// </summary>
        public const string LegacyModifiedIniToken = "ModifiedINI";

        /// <summary>
        /// Item name for the Generals Community Patch 2.0 patch INI content.
        /// </summary>
        public const string PatchIniItemName = "PatchINI";

        /// <summary>
        /// Legacy JSON key found in stale Lemon Control Bar configs.
        /// </summary>
        public const string LegacyTargetDirJsonKey = "\"TargetDir\"";

        /// <summary>
        /// Pack name for the shared Lemon Control Bar base content.
        /// </summary>
        public const string LemonControlBarBasePack = "LemonControlBar_Base";

        /// <summary>
        /// Pack name for the Lemon Control Bar 1080p art content.
        /// </summary>
        public const string LemonControlBarArt1080Pack = "LemonControlBar_Art1080";

        /// <summary>
        /// Pack name for the Hotkeys Legionnaire Zero Hour content.
        /// </summary>
        public const string HotkeysLegionnaireZhPack = "!HotkeysLegionnaireZH";

        /// <summary>
        /// Item name for the Hotkeys indicators content.
        /// </summary>
        public const string HotkeyIndicatorsItem = "HotkeyIndicators";

        /// <summary>
        /// Pack name for the Improved Menus Spanish variant.
        /// </summary>
        public const string ImprovedMenusSpanishPack = "ImprovedMenus_Spanish";

        /// <summary>
        /// Pack name for the Leikeze Hotkeys Generals English variant.
        /// </summary>
        public const string LeikezeHotkeysGeneralsEnPack = "LeikezeHotkeys_Generals_EN";

        /// <summary>
        /// Pack name for the Leikeze Hotkeys Zero Hour German variant.
        /// </summary>
        public const string LeikezeHotkeysZhDePack = "LeikezeHotkeys_ZH_DE";

        /// <summary>
        /// Item name for the Hotkeys Zero Hour German content.
        /// </summary>
        public const string HotkeysZhGermanItem = "Hotkeys_ZH_German";

        /// <summary>
        /// Item name for the Hotkeys Generals English content.
        /// </summary>
        public const string HotkeysGeneralsEnglishItem = "Hotkeys_Generals_English";

        /// <summary>
        /// Item name for the Hotkeys string table content.
        /// </summary>
        public const string HotkeyStringsItem = "HotkeyStrings";

        /// <summary>
        /// Item name for the Hotkeys INI content.
        /// </summary>
        public const string HotkeyINIsItem = "HotkeyINIs";

        /// <summary>
        /// Pack name for the Lemon Control Bar 720p resolution content.
        /// </summary>
        public const string LemonControlBar720pPack = "LemonControlBar_720p";

        /// <summary>
        /// Pack name for the Lemon Control Bar 1080p resolution content.
        /// </summary>
        public const string LemonControlBar1080pPack = "LemonControlBar_1080p";

        /// <summary>
        /// Pack name for the Lemon Control Bar 1440p resolution content.
        /// </summary>
        public const string LemonControlBar1440pPack = "LemonControlBar_1440p";

        /// <summary>
        /// Pack name for the Lemon Control Bar 4K resolution content.
        /// </summary>
        public const string LemonControlBar4KPack = "LemonControlBar_4K";

        /// <summary>
        /// Pack name for the Lemon Control Bar 1080p data content.
        /// </summary>
        public const string LemonControlBarData1080Pack = "LemonControlBar_Data1080";

        /// <summary>
        /// Pack name for the Lemon Control Bar 2160p art content.
        /// </summary>
        public const string LemonControlBarArt2160Pack = "LemonControlBar_Art2160";

        /// <summary>
        /// Pack name for the Lemon Control Bar 2160p data content.
        /// </summary>
        public const string LemonControlBarData2160Pack = "LemonControlBar_Data2160";

        /// <summary>
        /// Expected output file name for the Lemon Control Bar 720p resolution.
        /// </summary>
        public const string LemonControlBar720pOutputFileName = "340_ControlBarProLemonEdition720ZH.big";

        /// <summary>
        /// Expected output file name for the Lemon Control Bar 1440p resolution.
        /// </summary>
        public const string LemonControlBar1440pOutputFileName = "340_ControlBarProLemonEdition1440ZH.big";

        /// <summary>
        /// Expected output file name for the Lemon Control Bar 2160p resolution.
        /// </summary>
        public const string LemonControlBar2160OutputFileName = "340_ControlBarProLemonEdition2160ZH.big";

        /// <summary>
        /// Download archive file name for the Lemon Control Bar 1080p assets.
        /// </summary>
        public const string LemonControlBar1080pZipFileName = "ControlBarProLemonEditionZH_v1.3_1920x1080.zip";

        /// <summary>
        /// Download archive file name for the Lemon Control Bar 720p assets.
        /// </summary>
        public const string LemonControlBar720pZipFileName = "ControlBarProLemonEditionZH_v1.3_1280x720.zip";

        /// <summary>
        /// Download archive file name for the Lemon Control Bar 1440p assets.
        /// </summary>
        public const string LemonControlBar1440pZipFileName = "ControlBarProLemonEditionZH_v1.3_2560x1440.zip";

        /// <summary>
        /// Download archive file name for the Lemon Control Bar 2160p assets.
        /// </summary>
        public const string LemonControlBar2160ZipFileName = "ControlBarProLemonEditionZH_v1.3_3840x2160.zip";

        /// <summary>
        /// Gets the download URL for Generals Community Patch 2.0 core INI sample assets.
        /// </summary>
        public static string GeneralsGamePatch2Url =>
            Environment.GetEnvironmentVariable(GeneralsGamePatch2UrlEnvVar) is { Length: > 0 } customUrl
                ? customUrl.Trim()
                : DefaultGeneralsGamePatch2Url;

        /// <summary>
        /// Gets the download URL for Improved Menus widescreen English sample assets.
        /// </summary>
        public static string ImprovedMenusEnglishUrl =>
            Environment.GetEnvironmentVariable(ImprovedMenusEnglishUrlEnvVar) is { Length: > 0 } customUrl
                ? customUrl.Trim()
                : DefaultImprovedMenusEnglishUrl;

        /// <summary>
        /// Gets the download URL for Improved Menus widescreen sample assets (canonical default).
        /// </summary>
        public static string ImprovedMenusUrl => ImprovedMenusEnglishUrl;

        /// <summary>
        /// Gets the download URL for Improved Menus Russian assets.
        /// </summary>
        public static string ImprovedMenusRussianUrl =>
            Environment.GetEnvironmentVariable(ImprovedMenusRussianUrlEnvVar) is { Length: > 0 } customUrl
                ? customUrl.Trim()
                : DefaultImprovedMenusRussianUrl;

        /// <summary>
        /// Gets the download URL for Improved Menus Spanish assets.
        /// </summary>
        public static string ImprovedMenusSpanishUrl =>
            Environment.GetEnvironmentVariable(ImprovedMenusSpanishUrlEnvVar) is { Length: > 0 } customUrl
                ? customUrl.Trim()
                : DefaultImprovedMenusSpanishUrl;

        /// <summary>
        /// Gets the download URL for Lemon Control Bar 720p assets.
        /// </summary>
        public static string LemonControlBar720pUrl =>
            Environment.GetEnvironmentVariable(LemonControlBar720pUrlEnvVar) is { Length: > 0 } customUrl
                ? customUrl.Trim()
                : DefaultLemonControlBar720pUrl;

        /// <summary>
        /// Gets the download URL for Lemon Control Bar sample assets (canonical default).
        /// </summary>
        public static string LemonControlBarUrl => LemonControlBar1080pUrl;

        /// <summary>
        /// Gets the download URL for Lemon Control Bar 1080p assets.
        /// </summary>
        public static string LemonControlBar1080pUrl =>
            Environment.GetEnvironmentVariable(LemonControlBar1080pUrlEnvVar) is { Length: > 0 } customUrl
                ? customUrl.Trim()
                : DefaultLemonControlBar1080pUrl;

        /// <summary>
        /// Gets the download URL for Lemon Control Bar 1440p assets.
        /// </summary>
        public static string LemonControlBar1440pUrl =>
            Environment.GetEnvironmentVariable(LemonControlBar1440pUrlEnvVar) is { Length: > 0 } customUrl
                ? customUrl.Trim()
                : DefaultLemonControlBar1440pUrl;

        /// <summary>
        /// Gets the download URL for Lemon Control Bar 4K assets.
        /// </summary>
        public static string LemonControlBar4KUrl =>
            Environment.GetEnvironmentVariable(LemonControlBar4KUrlEnvVar) is { Length: > 0 } customUrl
                ? customUrl.Trim()
                : DefaultLemonControlBar4KUrl;

        /// <summary>
        /// Gets the download URL for Leikeze Hotkeys sample assets.
        /// </summary>
        public static string LeikezeHotkeysUrl =>
            Environment.GetEnvironmentVariable(LeikezeHotkeysUrlEnvVar) is { Length: > 0 } customUrl
                ? customUrl.Trim()
                : DefaultLeikezeHotkeysUrl;

        /// <summary>
        /// Gets the download URL for Hotkeys hleg asset.
        /// </summary>
        public static string HotkeysHlegUrl =>
            Environment.GetEnvironmentVariable(HotkeysHlegUrlEnvVar) is { Length: > 0 } customUrl
                ? customUrl.Trim()
                : DefaultHotkeysHlegUrl;

        /// <summary>
        /// Gets the download URL for Hotkeys hlen asset.
        /// </summary>
        public static string HotkeysHlenUrl =>
            Environment.GetEnvironmentVariable(HotkeysHlenUrlEnvVar) is { Length: > 0 } customUrl
                ? customUrl.Trim()
                : DefaultHotkeysHlenUrl;
    }
}
