using System;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the Replay Manager feature.
/// </summary>
public static class ReplayManagerConstants
{
    /// <summary>
    /// File extension for Command &amp; Conquer Generals replay files.
    /// </summary>
    public const string ReplayFileExtension = ".rep";

    /// <summary>
    /// File extension for ZIP archive files.
    /// </summary>
    public const string ZipFileExtension = ".zip";

    /// <summary>
    /// Environment variable name to override the default community CRC mapping catalog endpoint.
    /// </summary>
    public const string CrcCatalogUrlEnvironmentVariable = "GENHUB_CRC_CATALOG_URL";

    /// <summary>
    /// Maximum size for a single replay file in bytes (10 MB).
    /// </summary>
    public const long MaxReplaySizeBytes = 10 * ConversionConstants.BytesPerMegabyte;

    /// <summary>
    /// Maximum allowed entries in a replay ZIP archive.
    /// </summary>
    public const int MaxZipEntries = 100;

    /// <summary>
    /// Maximum aggregate uncompressed bytes for a replay ZIP archive (50 MB).
    /// </summary>
    public const long MaxAggregateUncompressedBytes = 50 * ConversionConstants.BytesPerMegabyte;

    /// <summary>
    /// Maximum compression ratio allowed for replay ZIP archives.
    /// </summary>
    public const double MaxCompressionRatio = 50.0;

    /// <summary>
    /// Maximum upload bytes per period (10 MB).
    /// </summary>
    public const long MaxUploadBytesPerPeriod = 10 * ConversionConstants.BytesPerMegabyte;

    /// <summary>
    /// Prefix for temporary import files.
    /// </summary>
    public const string TempImportFilePrefix = "genhub_import_";

    /// <summary>
    /// Prefix for temporary share files.
    /// </summary>
    public const string TempShareFilePrefix = "genhub_share_";

    /// <summary>
    /// Default file name for imported replays.
    /// </summary>
    public const string DefaultImportedReplayFileName = "imported_replay.rep";

    /// <summary>
    /// File pattern for replay ZIP archives.
    /// </summary>
    public const string ZipFilePattern = "*.zip";

    /// <summary>
    /// Default name for exported replay ZIP files.
    /// </summary>
    public const string DefaultZipName = "replays";

    /// <summary>
    /// Notification title for delete failure.
    /// </summary>
    public const string DeleteFailedTitle = ToolConstants.DeleteFailedTitle;

    /// <summary>
    /// Category identifier for replay uploads.
    /// </summary>
    public const string UploadCategory = "replays";

    /// <summary>
    /// Mock path separator indicator for demo environments on Windows.
    /// </summary>
    public const string WindowsMockPathSegment = ToolConstants.WindowsMockPathSegment;

    /// <summary>
    /// Mock path separator indicator for demo environments on Unix.
    /// </summary>
    public const string UnixMockPathSegment = ToolConstants.UnixMockPathSegment;

    /// <summary>
    /// Replay file magic header bytes ("GENREP").
    /// </summary>
    public const string ReplayHeaderMagic = "GENREP";

    /// <summary>
    /// Initial buffer size in bytes for reading replay headers (16 KB).
    /// </summary>
    public const int ReplayHeaderBufferSize = 16384;

    /// <summary>
    /// Minimum size in bytes required for a valid replay header (28 bytes).
    /// </summary>
    public const int MinReplayHeaderSizeBytes = 28;

    /// <summary>
    /// Fixed offset in bytes to skip the replay magic header and initial fixed metadata fields.
    /// </summary>
    public const int ReplayHeaderInitialOffsetBytes = 28;

    /// <summary>
    /// Default GitHub URL providing the authoritative community CRC mapping catalog.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Official GenHub endpoint for community gameclient CRC catalog.")]
    public const string DefaultCrcCatalogUrl = "https://raw.githubusercontent.com/community-outpost/GenHub/development/GenHub/GenHub/Resources/crc-mapping.json";

    /// <summary>
    /// Fallback alias for backward compatibility.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Official GenHub endpoint for community gameclient CRC catalog.")]
    public const string DefaultCrcCatalogGistUrl = DefaultCrcCatalogUrl;

    /// <summary>
    /// Cache key for storing the parsed CRC catalog in the dynamic content cache.
    /// </summary>
    public const string CrcCatalogCacheKey = "ReplayManager:CrcCatalog";

    /// <summary>
    /// Local offline fallback file name for storing cached CRC mappings in app data directory.
    /// </summary>
    public const string CrcCatalogLocalFileName = "crc-mapping.json";

    /// <summary>
    /// Manifest segment indicating official retail distribution.
    /// </summary>
    public const string RetailManifestSegment = ".retail.";

    /// <summary>
    /// Default update polling interval for checking new CRC catalog releases (24 hours).
    /// </summary>
    public static readonly TimeSpan DefaultCatalogUpdateInterval = TimeSpan.FromHours(24);
}
