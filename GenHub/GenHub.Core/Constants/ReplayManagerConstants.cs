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
    public const string ReplayFileExtension = FileTypes.ReplayFileExtension;

    /// <summary>
    /// File extension for ZIP archive files.
    /// </summary>
    public const string ZipFileExtension = FileTypes.ZipFileExtension;

    /// <summary>
    /// File extension for Command &amp; Conquer save files.
    /// </summary>
    public const string SaveFileExtension = ".sav";

    /// <summary>
    /// Default folder name for game save files within user data.
    /// </summary>
    public const string SaveFolderName = "Save";

    /// <summary>
    /// Command line argument flag to skip intro cinematic sequences and menus.
    /// </summary>
    public const string CliQuickStart = "-quickstart";

    /// <summary>
    /// Command line argument flag to specify the replay file to play or scrub through.
    /// </summary>
    public const string CliReplay = "-replay";

    /// <summary>
    /// Command line argument flag to mint a checkpoint save at a target frame or comma-separated list of frames.
    /// Used by preview checkpoint-recovery engine builds, gated via GameClientCapabilities.ReplayCheckpoints.
    /// </summary>
    public const string CliSaveAtFrame = "-saveatframe";

    /// <summary>
    /// Command line argument flag to specify the output checkpoint save file name.
    /// Used by preview checkpoint-recovery engine builds, gated via GameClientCapabilities.ReplayCheckpoints.
    /// </summary>
    public const string CliSaveTo = "-saveto";

    /// <summary>
    /// Command line argument flag to quit the game cleanly after reaching a target frame.
    /// Used by preview checkpoint-recovery engine builds, gated via GameClientCapabilities.ReplayCheckpoints.
    /// </summary>
    public const string CliQuitAtFrame = "-quitatframe";

    /// <summary>
    /// Command line argument flag to load a save file.
    /// </summary>
    public const string CliLoadSave = "-loadsave";

    /// <summary>
    /// Command line argument flag to resume playback of a replay file deterministically from a checkpoint save.
    /// Used by preview checkpoint-recovery engine builds, gated via GameClientCapabilities.ReplayCheckpoints.
    /// </summary>
    public const string CliResumeReplay = "-resumereplay";

    /// <summary>
    /// Command line argument flag to take over live control of a specified player slot index from a save.
    /// Used by preview checkpoint-recovery engine builds, gated via GameClientCapabilities.ReplayCheckpoints.
    /// </summary>
    public const string CliResumeAs = "-resumeas";

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
    /// Size in bytes of the SYSTEMTIME timestamp structure embedded in the replay header (16 bytes).
    /// </summary>
    public const int ReplayHeaderSystemTimeSizeBytes = 16;

    /// <summary>
    /// Combined size in bytes of the numeric version, Exe CRC, and INI CRC fields (12 bytes: 3 * 4 bytes).
    /// </summary>
    public const int ReplayHeaderCrcBlockSizeBytes = 12;

    /// <summary>
    /// Size in bytes of a 32-bit unsigned integer field in the replay header (4 bytes).
    /// </summary>
    public const int ReplayHeaderUInt32SizeBytes = 4;

    /// <summary>
    /// The expected schema version of the CRC mapping catalog.
    /// </summary>
    public const int CrcCatalogSchemaVersion = 1;

    /// <summary>
    /// Default GitHub URL providing the authoritative community CRC mapping catalog.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Official GenHub endpoint for community gameclient CRC catalog.")]
    public const string DefaultCrcCatalogUrl = "https://raw.githubusercontent.com/community-outpost/GenHub/development/GenHub/GenHub/Resources/crc-mapping.json";

    /// <summary>
    /// Cache key for storing the parsed CRC catalog in the dynamic content cache.
    /// </summary>
    public const string CrcCatalogCacheKey = "ReplayManager:CrcCatalog";

    /// <summary>
    /// Local offline fallback file name for storing cached CRC mappings in app data directory.
    /// </summary>
    public const string CrcCatalogLocalFileName = "crc-mapping.json";

    /// <summary>
    /// Default integer version number for Command &amp; Conquer Generals: Zero Hour retail manifests (1.04).
    /// </summary>
    public const int DefaultZeroHourVersionNumber = 104;

    /// <summary>
    /// Default integer version number for Command &amp; Conquer Generals retail manifests (1.08).
    /// </summary>
    public const int DefaultGeneralsVersionNumber = 108;

    /// <summary>
    /// Hexadecimal CRC representing the English vanilla Zero Hour 1.04 INI.
    /// </summary>
    public const string VanillaZeroHourIniCrcEnglish = "FEAAE3F3";

    /// <summary>
    /// Hexadecimal CRC representing the German/European vanilla Zero Hour 1.04 INI.
    /// </summary>
    public const string VanillaZeroHourIniCrcGerman = "76B251A3";

    /// <summary>
    /// Hexadecimal CRC representing the German/European Generals 1.08 INI.
    /// </summary>
    public const string VanillaGeneralsIniCrcGerman = "5CB7992C";

    /// <summary>
    /// Display name for the default Vanilla 1.04 INI data patch.
    /// </summary>
    public const string Vanilla104IniName = "Vanilla 1.04 INI";

    /// <summary>
    /// Hexadecimal Exe CRC string representing retail Zero Hour 1.04 CD / First Decade build ("0xDA2B4B18").
    /// </summary>
    public const string RetailZeroHourExeCrcFirstDecade = "0xDA2B4B18";

    /// <summary>
    /// Numeric Exe CRC representing retail Zero Hour 1.04 CD / First Decade build (0xDA2B4B18).
    /// </summary>
    public const uint RetailZeroHourExeCrcFirstDecadeValue = 0xDA2B4B18;

    /// <summary>
    /// Hexadecimal Exe CRC string representing retail Zero Hour 1.04 Steam / EA App build ("0x401D89EA").
    /// </summary>
    public const string RetailZeroHourExeCrcSteam = "0x401D89EA";

    /// <summary>
    /// Numeric Exe CRC representing retail Zero Hour 1.04 Steam / EA App build (0x401D89EA).
    /// </summary>
    public const uint RetailZeroHourExeCrcSteamValue = 0x401D89EA;

    /// <summary>
    /// Hexadecimal Exe CRC string representing retail Generals 1.08 CD / First Decade build ("0x89C1F821").
    /// </summary>
    public const string RetailGeneralsExeCrcFirstDecade = "0x89C1F821";

    /// <summary>
    /// Numeric Exe CRC representing retail Generals 1.08 CD / First Decade build (0x89C1F821).
    /// </summary>
    public const uint RetailGeneralsExeCrcFirstDecadeValue = 0x89C1F821;

    /// <summary>
    /// Hexadecimal Exe CRC string representing retail Generals 1.08 Steam / EA App build ("0x1C96366F").
    /// </summary>
    public const string RetailGeneralsExeCrcSteam = "0x1C96366F";

    /// <summary>
    /// Numeric Exe CRC representing retail Generals 1.08 Steam / EA App build (0x1C96366F).
    /// </summary>
    public const uint RetailGeneralsExeCrcSteamValue = 0x1C96366F;

    /// <summary>
    /// Composite content ID pattern for GeneralsOnline client content.
    /// </summary>
    public const string GeneralsOnlineContentIdPattern = "GeneralsOnline_{0}";

    /// <summary>
    /// Composite content ID pattern for generic third-party client content.
    /// </summary>
    public const string ThirdPartyClientContentIdPattern = "Client_{0}_{1}";

    /// <summary>
    /// Category for clients matching replay CRC.
    /// </summary>
    public const string CrcCompatibleCategory = "CRC Compatible";

    /// <summary>
    /// Category for base installation clients.
    /// </summary>
    public const string BaseInstallationCategory = "Base Installation";

    /// <summary>
    /// Category for retail fallback clients.
    /// </summary>
    public const string RetailFallbackCategory = "Retail Fallback";

    /// <summary>
    /// Category for local game profiles.
    /// </summary>
    public const string LocalProfileCategory = "Local Profile";

    /// <summary>
    /// Category for catalog manifests.
    /// </summary>
    public const string CatalogManifestCategory = "Catalog Manifest";

    /// <summary>
    /// Publisher name for EA / Retail clients.
    /// </summary>
    public const string EaRetailPublisher = "EA / Retail";

    /// <summary>
    /// Keyword substring identifying retail game clients or manifests.
    /// </summary>
    public const string RetailKeyword = "retail";

    /// <summary>
    /// Key representing the base retail client in discovery cache.
    /// </summary>
    public const string RetailBaseClientKey = "retail-base-client";

    /// <summary>
    /// Publisher name for Catalog manifests.
    /// </summary>
    public const string CatalogPublisher = "Catalog";

    /// <summary>
    /// Fallback publisher name for custom profiles.
    /// </summary>
    public const string CustomPublisher = "Custom";

    /// <summary>
    /// Prefix label for custom INI data patches.
    /// </summary>
    public const string CustomIniPrefix = "Custom INI";

    /// <summary>
    /// Identifier substring for Community Patch with a hyphen.
    /// </summary>
    public const string CommunityPatchHyphenatedKeyword = "community-patch";

    /// <summary>
    /// Identifier substring for Community Patch without spaces or hyphens.
    /// </summary>
    public const string CommunityPatchKeyword = "communitypatch";

    /// <summary>
    /// Display name substring for Community Patch.
    /// </summary>
    public const string CommunityPatchDisplayName = "Community Patch";

    /// <summary>
    /// Client version string for Zero Hour retail 1.04.
    /// </summary>
    public const string ZeroHourRetailVersion = "1.04";

    /// <summary>
    /// Client version string for Generals retail 1.08.
    /// </summary>
    public const string GeneralsRetailVersion = "1.08";

    /// <summary>
    /// Default display name for the retail Zero Hour client.
    /// </summary>
    public const string RetailZeroHourClientName = "Retail 1.04";

    /// <summary>
    /// Default display name for the retail Generals client.
    /// </summary>
    public const string RetailGeneralsClientName = "Retail 1.08";

    /// <summary>
    /// Identifier segment for Zero Hour manifests.
    /// </summary>
    public const string ZeroHourManifestSegment = ".10zh.";

    /// <summary>
    /// Identifier segment for Generals manifests.
    /// </summary>
    public const string GeneralsManifestSegment = ".10gn.";

    /// <summary>
    /// Default fallback version for catalog manifests.
    /// </summary>
    public const string DefaultManifestVersion = "1.0";

    /// <summary>
    /// Fallback version for base game installation clients.
    /// </summary>
    public const string BaseInstallationVersion = "Base";

    /// <summary>
    /// Fallback title for generic game client.
    /// </summary>
    public const string DefaultGameClientTitle = "Game";

    /// <summary>
    /// Display name for retail client fallback.
    /// </summary>
    public const string RetailClientDisplayName = "Retail Client";

    /// <summary>
    /// Display name for third-party client fallback.
    /// </summary>
    public const string ThirdPartyClientDisplayName = "Third-Party Client";

    /// <summary>
    /// Not available fallback indicator.
    /// </summary>
    public const string NotAvailable = "N/A";

    /// <summary>
    /// Default update polling interval for checking new CRC catalog releases (24 hours).
    /// </summary>
    public static readonly TimeSpan DefaultCatalogUpdateInterval = TimeSpan.FromHours(24);
}
