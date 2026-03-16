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
    /// Allowed file extensions for map packages.
    /// </summary>
    public static readonly string[] AllowedExtensions = [".map", ".tga", ".ini", ".str", ".txt"];

    /// <summary>
    /// Image file extensions that can be used as thumbnails.
    /// </summary>
    public static readonly string[] ImageExtensions = [".tga"];
}