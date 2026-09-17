using System;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the Replay Manager feature.
/// </summary>
public static class ReplayManagerConstants
{
    /// <summary>
    /// Protocol argument sizes in bytes for replay chunk command parameter types.
    /// </summary>
    public static class CommandArgSizes
    {
        /// <summary>Size of 32-bit integer argument (4 bytes).</summary>
        public const int Integer = 4;

        /// <summary>Size of 32-bit floating-point real argument (4 bytes).</summary>
        public const int Real = 4;

        /// <summary>Size of boolean argument (1 byte).</summary>
        public const int Boolean = 1;

        /// <summary>Size of object identifier argument (4 bytes).</summary>
        public const int ObjectId = 4;

        /// <summary>Size of drawable identifier argument (4 bytes).</summary>
        public const int DrawableId = 4;

        /// <summary>Size of team identifier argument (4 bytes).</summary>
        public const int TeamId = 4;

        /// <summary>Size of location coordinates argument (12 bytes: 3 * 4-byte floats).</summary>
        public const int Location = 12;

        /// <summary>Size of pixel coordinate argument (8 bytes: 2 * 4-byte integers).</summary>
        public const int Pixel = 8;

        /// <summary>Size of pixel region argument (16 bytes: 4 * 4-byte integers).</summary>
        public const int PixelRegion = 16;

        /// <summary>Size of timestamp argument (4 bytes).</summary>
        public const int Timestamp = 4;

        /// <summary>Size of wide character argument (2 bytes).</summary>
        public const int WideChar = 2;
    }

    /// <summary>
    /// Frame rate for classic Command &amp; Conquer Generals / Zero Hour matches (30 FPS).
    /// </summary>
    public const int ClassicFps = 30;

    /// <summary>
    /// Frame rate for modern community high-refresh-rate builds such as GeneralsOnline (60 FPS).
    /// </summary>
    public const int GeneralsOnlineFps = 60;

    /// <summary>
    /// Keyword used to detect 60Hz high-refresh-rate replay builds.
    /// </summary>
    public const string HighRefreshRateKeyword = "60Hz";

    /// <summary>
    /// Default maximum checkpoint duration in seconds when replay metadata has no end time or frames (600s / 10 minutes).
    /// </summary>
    public const int DefaultMaxCheckpointSeconds = 600;

    /// <summary>
    /// Default target checkpoint time in seconds when opening the checkpoint drawer (120s / 2 minutes).
    /// </summary>
    public const int DefaultTargetCheckpointSeconds = 120;

    /// <summary>
    /// Display label suffix representing computer AI players in slot displays.
    /// </summary>
    public const string AiLabel = "AI";

    /// <summary>
    /// Minimum valid size in bytes for a Command &amp; Conquer save file (1024 bytes).
    /// </summary>
    public const long MinValidSaveFileSizeBytes = 1024;

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
    /// Used by preview checkpoint-recovery engine builds, gated via GameClientCapabilities.CheckpointSaves.
    /// </summary>
    public const string CliSaveAtFrame = "-saveatframe";

    /// <summary>
    /// Command line argument flag to specify the output checkpoint save file name.
    /// Used by preview checkpoint-recovery engine builds, gated via GameClientCapabilities.CheckpointSaves.
    /// </summary>
    public const string CliSaveTo = "-saveto";

    /// <summary>
    /// Command line argument flag to quit the game cleanly after reaching a target frame.
    /// Used by preview checkpoint-recovery engine builds, gated via GameClientCapabilities.CheckpointSaves.
    /// </summary>
    public const string CliQuitAtFrame = "-quitatframe";

    /// <summary>
    /// Command line argument flag to load a save file.
    /// </summary>
    public const string CliLoadSave = "-loadsave";

    /// <summary>
    /// Command line argument flag to resume playback of a replay file deterministically from a checkpoint save.
    /// Used by preview checkpoint-recovery engine builds, gated via GameClientCapabilities.ReplayResumption.
    /// </summary>
    public const string CliResumeReplay = "-resumereplay";

    /// <summary>
    /// Command line argument flag to take over live control of a specified player slot index from a save.
    /// Used by preview checkpoint-recovery engine builds, gated via GameClientCapabilities.PlayerTakeover.
    /// </summary>
    public const string CliResumeAs = "-resumeas";

    /// <summary>
    /// Keyword substring identifying recovery engine builds or capabilities.
    /// </summary>
    public const string CapabilityRecoveryKeyword = "recovery";

    /// <summary>
    /// Keyword substring identifying checkpoint save capabilities.
    /// </summary>
    public const string CapabilityCheckpointKeyword = "checkpoint";

    /// <summary>
    /// Keyword substring identifying player takeover capabilities.
    /// </summary>
    public const string CapabilityTakeoverKeyword = "takeover";

    /// <summary>
    /// Prefix prepended to generated checkpoint save files.
    /// </summary>
    public const string CheckpointFilePrefix = "cp_";

    /// <summary>
    /// File search pattern used to discover checkpoint save files.
    /// </summary>
    public const string CheckpointFileSearchPattern = "*.sav";

    /// <summary>
    /// Default polling interval in milliseconds when waiting for checkpoint process exits.
    /// </summary>
    public const int DefaultCheckpointPollIntervalMs = 500;

    /// <summary>
    /// Maximum consecutive retry attempts allowed during process status inspection.
    /// </summary>
    public const int MaxProcessExitRetries = 3;

    /// <summary>
    /// Sanity floor Unix epoch timestamp (2000-01-01 00:00:00 UTC) below which header timestamps are treated as invalid.
    /// </summary>
    public const uint MinSanityTimestampEpoch = 946684800u;

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
    /// Error message returned when a checkpoint minting operation is canceled by the user.
    /// </summary>
    public const string CheckpointMintingCanceledErrorMessage = "Checkpoint minting canceled by user.";

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
    /// Maximum buffer size in bytes for reading replay headers (16 KB).
    /// </summary>
    public const int MaxHeaderReadBytes = ReplayHeaderBufferSize;

    /// <summary>
    /// Minimum size in bytes required for a valid replay header (28 bytes).
    /// </summary>
    public const int MinReplayHeaderSizeBytes = 28;

    /// <summary>
    /// Minimum size in bytes required for reading a replay header (28 bytes).
    /// </summary>
    public const int MinHeaderReadBytes = MinReplayHeaderSizeBytes;

    /// <summary>
    /// Fixed offset in bytes to skip the replay magic header and initial fixed metadata fields.
    /// </summary>
    public const int ReplayHeaderInitialOffsetBytes = 28;

    /// <summary>
    /// Offset in bytes from the start of the replay file to the StartTime field (6 bytes).
    /// </summary>
    public const int StartTimeOffsetBytes = 6;

    /// <summary>
    /// Offset in bytes from the start of the replay file to the EndTime field (10 bytes).
    /// </summary>
    public const int EndTimeOffsetBytes = 10;

    /// <summary>
    /// Offset in bytes from the start of the replay file to the HeaderFrameCount field (14 bytes).
    /// </summary>
    public const int HeaderFrameCountOffsetBytes = 14;

    /// <summary>
    /// Stride in bytes between consecutive chunk header timecode markers when scanning replay chunks (13 bytes).
    /// </summary>
    public const int MaxChunkTimecodeStrideBytes = 13;

    /// <summary>
    /// Nominal size in bytes of the post-header trailer structure following the initial setup string
    /// (consisting of a variable-length null-terminated local player index string plus 16 fixed trailer bytes before the chunk stream; typically 18 bytes for single-digit index "0\0").
    /// </summary>
    public const int ReplayPostHeaderTrailerSizeBytes = 18;

    /// <summary>
    /// Fixed size in bytes of the trailer following the null-terminated player index string before the chunk stream (16 bytes).
    /// </summary>
    public const int ReplayPlayerIndexFixedTrailerSizeBytes = 16;

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
    /// Identifier segment for retail game client manifests.
    /// </summary>
    public const string RetailGameClientSegment = ".retail.gameclient.";

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
    /// Fallback title for Zero Hour game client.
    /// </summary>
    public const string ZeroHourGameClientTitle = GameClientConstants.ZeroHourShortName;

    /// <summary>
    /// Fallback title for Generals game client.
    /// </summary>
    public const string GeneralsGameClientTitle = GameClientConstants.GeneralsShortName;

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
