namespace GenHub.Core.Constants;

/// <summary>
/// File and directory name constants to prevent typos and ensure consistency.
/// </summary>
public static class FileTypes
{
    /// <summary>
    /// Directory name for storing manifest files.
    /// </summary>
    public const string ManifestsDirectory = "Manifests";

    /// <summary>
    /// File extension pattern for manifest files.
    /// </summary>
    public const string ManifestFilePattern = "*.manifest.json";

    /// <summary>
    /// File extension for manifest files.
    /// </summary>
    public const string ManifestFileExtension = ".manifest.json";

    /// <summary>
    /// File extension for JSON files.
    /// </summary>
    public const string JsonFileExtension = ".json";

    /// <summary>
    /// File extension pattern for JSON files.
    /// </summary>
    public const string JsonFilePattern = "*.json";

    /// <summary>
    /// Default settings file name.
    /// </summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>
    /// Settings file name written by releases up to v0.0.3, which combined the data root with the
    /// JSON extension instead of the settings file name.
    /// </summary>
    public const string LegacySettingsFileName = ".json";

    /// <summary>
    /// File name holding the persisted workspace metadata.
    /// </summary>
    public const string WorkspaceMetadataFileName = "workspaces.json";

    /// <summary>
    /// File name of the index tracking installed user data.
    /// </summary>
    public const string UserDataIndexFileName = "index.json";

    /// <summary>
    /// File extension for replay files.
    /// </summary>
    public const string ReplayFileExtension = ".rep";

    /// <summary>
    /// File extension for ZIP files.
    /// </summary>
    public const string ZipFileExtension = ".zip";

    /// <summary>
    /// File extension for 7-Zip archive files.
    /// </summary>
    public const string SevenZipFileExtension = ".7z";

    /// <summary>
    /// File extension for TAR archive files.
    /// </summary>
    public const string TarFileExtension = ".tar";

    /// <summary>
    /// File extension for GZIP compressed files.
    /// </summary>
    public const string GzipFileExtension = ".gz";

    /// <summary>
    /// File extension for RAR archive files.
    /// </summary>
    public const string RarFileExtension = ".rar";

    /// <summary>
    /// File extension pattern for replay files.
    /// </summary>
    public const string ReplayFilePattern = "*.rep";

    /// <summary>
    /// File extension pattern for ZIP files.
    /// </summary>
    public const string ZipFilePattern = "*.zip";

    /// <summary>
    /// File extension for backup files.
    /// </summary>
    public const string BackupExtension = ".ghbak";

    /// <summary>
    /// Legacy file extension for backup files.
    /// </summary>
    public const string LegacyBackupExtension = ".bak";

    /// <summary>
    /// Directory name for Git repository metadata.
    /// </summary>
    public const string GitDirectoryName = ".git";

    /// <summary>
    /// File extension for user data manifest files.
    /// </summary>
    public const string UserDataManifestExtension = ".userdata.json";

    /// <summary>
    /// Filename used to store the source directory path mapping for a manifest's content.
    /// This file is written inside the manifest's data directory and contains the path
    /// to the original source directory (e.g., local installation folder).
    /// </summary>
    public const string SourcePathFileName = "source.path";

    /// <summary>
    /// Sentinel value written to <see cref="SourcePathFileName"/> when content is stored
    /// via CAS and has no source directory.
    /// </summary>
    public const string CasOnlySourceMarker = "CAS-ONLY";
}
