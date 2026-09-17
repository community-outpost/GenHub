using System;
using System.Collections.Generic;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants used for GameProfile sharing, package packaging, deep linking, and import inspection.
/// </summary>
public static class ProfileSharingConstants
{
    /// <summary>
    /// The default schema version for shared game profile packages.
    /// </summary>
    public const int DefaultSchemaVersion = 1;

    /// <summary>
    /// The maximum length in characters allowed for an inline Base64Url data payload (64 KB).
    /// Payloads exceeding this limit must be exported as .ghprofile files or hosted via URL.
    /// </summary>
    public const int MaxInlinePayloadLength = 65536;

    /// <summary>
    /// The file extension for standalone shared game profile package containers.
    /// </summary>
    public const string ProfileFileExtension = ".ghprofile";

    /// <summary>
    /// The display name for file picker dialogs when filtering for profile packages.
    /// </summary>
    public const string ProfileFileTypeDisplayName = "GenHub Profile Package";

    /// <summary>
    /// File pattern for finding profile package files.
    /// </summary>
    public const string ProfileFilePattern = "*.ghprofile";

    /// <summary>
    /// Maximum allowed length for profile names during import.
    /// </summary>
    public const int MaxProfileNameLength = 100;

    /// <summary>
    /// Default name used for shared profile fallback when input name is empty.
    /// </summary>
    public const string DefaultSharedProfileName = "Shared Profile";

    /// <summary>
    /// Maximum number of search results to request during fallback dependency resolution.
    /// </summary>
    public const int FallbackSearchLimit = 10;

    /// <summary>
    /// Maximum allowed decompressed payload size in bytes (2 MB).
    /// </summary>
    public const int MaxDecompressedPayloadBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Maximum allowed file size for .ghprofile packages (5 MB).
    /// </summary>
    public const long MaxProfileFileBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Maximum allowed size for a single downloaded manifest dependency file (500 MB).
    /// </summary>
    public const long MaxDownloadedFileBytes = 500 * 1024 * 1024;

    /// <summary>
    /// Maximum allowed total uncompressed size for an extracted package archive (1 GB).
    /// </summary>
    public const long MaxExtractedPackageBytes = 1024L * 1024 * 1024;

    /// <summary>
    /// Category name used when registering uploaded game profile packages in upload history.
    /// </summary>
    public const string UploadCategoryProfiles = "Profiles";

    /// <summary>
    /// Retention period in days for cloud storage uploads.
    /// </summary>
    public const int CloudUploadRetentionDays = 14;

    /// <summary>
    /// Maximum allowed file size for cloud upload packages (10 MB gateway limit).
    /// </summary>
    public const long MaxCloudUploadSizeBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Maximum profile upload size in bytes per rolling period (10 MB).
    /// </summary>
    public const long MaxProfileUploadBytesPerPeriod = 10 * 1024 * 1024;

    /// <summary>
    /// Backwards compatibility alias for <see cref="MaxProfileUploadBytesPerPeriod"/>.
    /// </summary>
    public const long MaxUploadBytesPerPeriod = MaxProfileUploadBytesPerPeriod;

    /// <summary>
    /// Staging directory name used for temporary cloud upload zip packages.
    /// </summary>
    public const string CloudUploadStagingDirectoryName = "CloudUploadStaging";

    /// <summary>
    /// Staging directory name used for temporary CAS blob materialization.
    /// </summary>
    public const string CasMaterializeStagingDirectoryName = "CasMaterializeStaging";

    /// <summary>
    /// Staging directory name used for temporary shared profile package imports.
    /// </summary>
    public const string SharedImportStagingDirectoryName = "SharedImportStaging";

    /// <summary>
    /// Error message when an export or inspection operation is invoked with an empty profile ID.
    /// </summary>
    public const string EmptyProfileIdErrorMessage = "Profile identifier cannot be empty.";

    /// <summary>
    /// Parent directory segment in file paths.
    /// </summary>
    public const string ParentDirectorySegment = "..";

    /// <summary>
    /// Default publisher name used for local content attribution in shared packages.
    /// </summary>
    public const string DefaultLocalPublisherName = "GenHub (Local)";

    /// <summary>
    /// Default publisher name used for generic community content in shared packages.
    /// </summary>
    public const string DefaultCommunityPublisherName = "Community";

    /// <summary>
    /// Default fallback version string used when creating fallback content dependencies.
    /// </summary>
    public const string DefaultFallbackContentVersion = "1.0";

    /// <summary>
    /// Default fallback hex theme color for imported profiles when not specified.
    /// </summary>
    public const string DefaultThemeColor = "#1976D2";

    /// <summary>
    /// Default secondary accent hex color used in profile sharing dialogs.
    /// </summary>
    public const string DefaultShareAccentColor = "#9575CD";

    /// <summary>
    /// File extensions recognized as executable or script binaries in shared profiles.
    /// </summary>
    public static readonly IReadOnlySet<string> ExecutableFileExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".asi", ".bat", ".cmd", ".ps1", ".vbs",
        ".msi", ".scr", ".com", ".pif", ".hta", ".jar", ".lnk", ".wsf",
    };
}
