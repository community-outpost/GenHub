namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the Map Manager feature.
/// </summary>
public static class MapManagerConstants
{
    /// <summary>
    /// Maximum file size for individual maps in bytes (10 MB).
    /// </summary>
    public const long MaxMapSizeBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Maximum allowed entries in a map ZIP archive.
    /// </summary>
    public const int MaxZipEntries = 500;

    /// <summary>
    /// Maximum aggregate uncompressed bytes for a map ZIP archive (200 MB).
    /// </summary>
    public const long MaxAggregateUncompressedBytes = 200 * 1024 * 1024;

    /// <summary>
    /// Maximum file size for individual map assets in bytes (10 MB).
    /// </summary>
    public const long MaxAssetSizeBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Maximum compression ratio allowed for ZIP archives.
    /// </summary>
    public const double MaxCompressionRatio = 100.0;

    /// <summary>
    /// Number of days for rate limit reset period.
    /// </summary>
    public const int RateLimitDays = 3;

    /// <summary>
    /// Maximum upload size in bytes per period (100 MB).
    /// </summary>
    public const long MaxUploadBytesPerPeriod = 100 * 1024 * 1024;

    /// <summary>
    /// Maximum width for map thumbnails in pixels.
    /// </summary>
    public const int ThumbnailMaxWidth = 128;

    /// <summary>
    /// Maximum height for map thumbnails in pixels.
    /// </summary>
    public const int ThumbnailMaxHeight = 128;

    /// <summary>
    /// Default thumbnail filename to look for in map directories.
    /// </summary>
    public const string DefaultThumbnailName = "map.tga";

    /// <summary>
    /// Standard per-map rules and scripts filename.
    /// </summary>
    public const string MapIniFileName = "map.ini";

    /// <summary>
    /// Standard per-map string table filename.
    /// </summary>
    public const string MapStrFileName = "map.str";

    /// <summary>
    /// Maximum directory nesting depth for maps (1 level).
    /// </summary>
    public const int MaxDirectoryDepth = 1;

    /// <summary>
    /// Directory name for Generals data.
    /// </summary>
    public const string GeneralsDataDirectoryName = "Command and Conquer Generals Data";

    /// <summary>
    /// Directory name for Zero Hour data.
    /// </summary>
    public const string ZeroHourDataDirectoryName = "Command and Conquer Generals Zero Hour Data";

    /// <summary>
    /// Subdirectory name where maps are stored.
    /// </summary>
    public const string MapsSubdirectoryName = "Maps";

    /// <summary>
    /// Subdirectory name where MapPacks are stored.
    /// </summary>
    public const string MapPacksSubdirectoryName = "mappacks";

    /// <summary>
    /// File pattern for map files.
    /// </summary>
    public const string MapFilePattern = "*.map";

    /// <summary>
    /// File pattern for ZIP files.
    /// </summary>
    public const string ZipFilePattern = "*.zip";

    /// <summary>
    /// Default name for exported ZIP files.
    /// </summary>
    public const string DefaultZipName = "maps";

    /// <summary>
    /// Tool identifier for Map Manager.
    /// </summary>
    public const string ToolId = "map-manager";

    /// <summary>
    /// Tool display name for Map Manager.
    /// </summary>
    public const string ToolName = "Map Manager";

    /// <summary>
    /// Tool description for Map Manager.
    /// </summary>
    public const string ToolDescription = "Manage, import, and share custom maps. Create MapPacks for easy profile switching.";

    /// <summary>
    /// Icon URI for Map Manager tool.
    /// </summary>
    public const string IconPath = UriConstants.MapManagerIconUri;

    /// <summary>
    /// Prefix for temporary share archives created for uploads.
    /// </summary>
    public const string TempShareFilePrefix = "genhub_maps_";

    /// <summary>
    /// Mock path separator indicator for demo environments on Windows.
    /// </summary>
    public const string WindowsMockPathSegment = ToolConstants.WindowsMockPathSegment;

    /// <summary>
    /// Mock path separator indicator for demo environments on Unix.
    /// </summary>
    public const string UnixMockPathSegment = ToolConstants.UnixMockPathSegment;

    /// <summary>
    /// Notification title for delete failure.
    /// </summary>
    public const string DeleteFailedTitle = ToolConstants.DeleteFailedTitle;

    /// <summary>
    /// Category identifier for map uploads.
    /// </summary>
    public const string UploadCategory = "maps";

    /// <summary>
    /// Default title used for MapPack creation failure notifications.
    /// </summary>
    public const string DefaultCreationFailedTitle = "Creation Failed";

    /// <summary>
    /// Default status text used for MapPack creation failures.
    /// </summary>
    public const string DefaultCreationFailedStatus = "Creation failed.";

    /// <summary>
    /// Localization key for the MapPack creation failure notification title.
    /// </summary>
    public const string CreationFailedTitleKey = "Maps.MapPack.Notification.CreationFailedTitle";

    /// <summary>
    /// Localization key for the MapPack creation failure status text.
    /// </summary>
    public const string CreationFailedStatusKey = "Maps.MapPack.Status.CreationFailed";

    /// <summary>
    /// Localization key for the unknown error notification message.
    /// </summary>
    public const string UnknownErrorKey = "Maps.MapPack.Notification.UnknownError";

    /// <summary>
    /// Default fallback message for unknown errors.
    /// </summary>
    public const string DefaultUnknownError = "Unknown error";

    /// <summary>Localization key for Add to Profile button.</summary>
    public const string AddToProfileButtonKey = "Maps.MapPack.Button.AddToProfile";

    /// <summary>Localization key for Profile Selection notification title.</summary>
    public const string ProfileSelectionTitleKey = "Maps.MapPack.Notification.ProfileSelectionTitle";

    /// <summary>Localization key for Add MapPack to Profile dialog title.</summary>
    public const string ProfileSelectionDialogTitleKey = "Maps.MapPack.ProfileSelection.DialogTitle";

    /// <summary>Localization key for Profile Selection header title.</summary>
    public const string ProfileSelectionHeaderTitleKey = "Maps.MapPack.ProfileSelection.HeaderTitle";

    /// <summary>Localization key for Profile Selection header subtitle.</summary>
    public const string ProfileSelectionHeaderSubtitleKey = "Maps.MapPack.ProfileSelection.HeaderSubtitle";

    /// <summary>Localization key for Profile Selection create card subtitle.</summary>
    public const string ProfileSelectionCreateCardSubtitleKey = "Maps.MapPack.ProfileSelection.CreateCardSubtitle";

    /// <summary>Localization key for Profile Selection action badge.</summary>
    public const string ProfileSelectionActionBadgeKey = "Maps.MapPack.ProfileSelection.ActionBadge";

    /// <summary>Localization key for Create &amp; Add to Profile button.</summary>
    public const string CreateAndAddToProfileButtonKey = "Maps.MapPack.Button.CreateAndAddToProfile";

    /// <summary>Localization key for Invalid Input notification title.</summary>
    public const string InvalidInputTitleKey = "Maps.MapPack.Notification.InvalidInputTitle";

    /// <summary>Localization key for Invalid Input notification message.</summary>
    public const string InvalidInputMessageKey = "Maps.MapPack.Notification.InvalidInputMessage";

    /// <summary>
    /// Allowed file extensions for map packages.
    /// </summary>
    public static readonly string[] AllowedExtensions = [".map", ".tga", ".wak", ".ini", ".str", ".txt"];

    /// <summary>
    /// Image file extensions that can be used as thumbnails.
    /// </summary>
    public static readonly string[] ImageExtensions = [".tga"];
}
