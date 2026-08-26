using System;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the Replay Manager feature.
/// </summary>
public static class ReplayManagerConstants
{
    /// <summary>
    /// Maximum size for a single replay file in bytes (1 MB).
    /// </summary>
    public const long MaxReplaySizeBytes = 1024 * 1024;

    /// <summary>
    /// Maximum allowed entries in a replay ZIP archive.
    /// </summary>
    public const int MaxZipEntries = 100;

    /// <summary>
    /// Maximum aggregate uncompressed bytes for a replay ZIP archive (50 MB).
    /// </summary>
    public const long MaxAggregateUncompressedBytes = 50 * 1024 * 1024;

    /// <summary>
    /// Maximum compression ratio allowed for replay ZIP archives.
    /// </summary>
    public const double MaxCompressionRatio = 50.0;

    /// <summary>
    /// Maximum upload bytes per period (10 MB).
    /// </summary>
    public const long MaxUploadBytesPerPeriod = 10 * 1024 * 1024;

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
    /// Replay file magic header bytes ("GENREP").
    /// </summary>
    public const string ReplayHeaderMagic = "GENREP";

    /// <summary>
    /// Default GitHub Gist URL providing the live CRC mapping catalog.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Official GenHub Gist endpoint for community gameclient CRC catalog.")]
    public const string DefaultCrcCatalogGistUrl = "https://gist.githubusercontent.com/undead2146/99bda56e85a579204dd7cad277547779/raw/crc-mapping.json";

    /// <summary>
    /// Cache key for storing the parsed CRC catalog in the dynamic content cache.
    /// </summary>
    public const string CrcCatalogCacheKey = "ReplayManager:CrcCatalog";

    /// <summary>
    /// Local offline fallback file name for storing cached CRC mappings in app data directory.
    /// </summary>
    public const string CrcCatalogLocalFileName = "crc-mapping.json";

    /// <summary>
    /// Default update polling interval for checking new CRC catalog releases (24 hours).
    /// </summary>
    public static readonly TimeSpan DefaultCatalogUpdateInterval = TimeSpan.FromHours(24);
}
