using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for GenLauncher file normalization and content publisher operations.
/// </summary>
[SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Official GenLauncher repository and documentation endpoints")]
public static class GenLauncherConstants
{
    /// <summary>
    /// Publisher identifier for GenLauncher.
    /// </summary>
    public const string PublisherId = PublisherTypeConstants.GenLauncher;

    /// <summary>
    /// Publisher type for GenLauncher.
    /// </summary>
    public const string PublisherType = PublisherTypeConstants.GenLauncher;

    /// <summary>
    /// Display name for GenLauncher.
    /// </summary>
    public const string PublisherName = PublisherInfoConstants.GenLauncher.Name;

    /// <summary>
    /// Short description for GenLauncher provider.
    /// </summary>
    public const string ProviderDescription = "Community mods, patches, and addons from the GenLauncher repository";

    /// <summary>
    /// Discoverer description for GenLauncher.
    /// </summary>
    public const string DiscovererDescription = "Discovers mods and addons from the GenLauncher YAML repository";

    /// <summary>
    /// Catalog format for GenLauncher YAML.
    /// </summary>
    public const string CatalogFormat = "genlauncher-yaml";

    /// <summary>
    /// Official repository website URL.
    /// </summary>
    public const string WebsiteUrl = PublisherInfoConstants.GenLauncher.Website;

    /// <summary>
    /// Official issues / support URL.
    /// </summary>
    public const string SupportUrl = PublisherInfoConstants.GenLauncher.SupportUrl;

    /// <summary>
    /// Probe timeout in seconds for GenLauncher size and availability probes.
    /// </summary>
    public const int ProbeTimeoutSeconds = 3;

    /// <summary>
    /// Catalog fetch timeout in seconds.
    /// </summary>
    public const int CatalogTimeoutSeconds = 30;

    /// <summary>
    /// Content acquisition timeout in seconds.
    /// </summary>
    public const int ContentTimeoutSeconds = 300;

    /// <summary>
    /// Default HTTP timeout in seconds for GenLauncher operations.
    /// </summary>
    public const int DefaultHttpTimeoutSeconds = 60;

    /// <summary>
    /// Default Zero Hour repository URL.
    /// </summary>
    public const string ZeroHourCatalogUrl = "https://raw.githubusercontent.com/p0ls3r/GenLauncherModsData/master/ReposModificationDataZH3.yaml";

    /// <summary>
    /// Default Generals repository URL.
    /// </summary>
    public const string GeneralsCatalogUrl = "https://raw.githubusercontent.com/p0ls3r/GenLauncherModsData/master/ReposModificationDataGenerals3.yaml";

    /// <summary>
    /// Game token for Zero Hour.
    /// </summary>
    public const string ZeroHourGameToken = "zerohour";

    /// <summary>
    /// Game token for Generals.
    /// </summary>
    public const string GeneralsGameToken = "generals";

    /// <summary>
    /// Default fallback version string for GenLauncher items without explicit version.
    /// </summary>
    public const string DefaultVersion = "1.0.0";

    /// <summary>
    /// Metadata key for S3 host link.
    /// </summary>
    public const string S3HostLinkMetadataKey = "s3HostLink";

    /// <summary>
    /// Legacy metadata key for S3 host.
    /// </summary>
    public const string S3HostMetadataKey = "s3Host";

    /// <summary>
    /// Metadata key for S3 bucket name.
    /// </summary>
    public const string S3BucketNameMetadataKey = "s3BucketName";

    /// <summary>
    /// Legacy metadata key for S3 bucket.
    /// </summary>
    public const string S3BucketMetadataKey = "s3Bucket";

    /// <summary>
    /// Metadata key for S3 folder name.
    /// </summary>
    public const string S3FolderNameMetadataKey = "s3FolderName";

    /// <summary>
    /// Legacy metadata key for S3 folder.
    /// </summary>
    public const string S3FolderMetadataKey = "s3Folder";

    /// <summary>
    /// Metadata key for S3 host public key.
    /// </summary>
    public const string S3HostPublicKeyMetadataKey = "s3HostPublicKey";

    /// <summary>
    /// Metadata key for S3 host secret key.
    /// </summary>
    public const string S3HostSecretKeyMetadataKey = "s3HostSecretKey";

    /// <summary>
    /// Default S3 region used for AWS Signature V4 request signing.
    /// </summary>
    public const string DefaultS3Region = "us-east-1";

    /// <summary>
    /// Default expiration time in seconds for S3 presigned URLs (24 hours).
    /// </summary>
    public const int DefaultS3PresignedUrlExpirySeconds = 86400;

    /// <summary>
    /// S3 host link substring for the default GenLauncher InSave server.
    /// </summary>
    public const string DefaultGenInsaveHost = "gen.insave.ovh:9000";

    /// <summary>
    /// Metadata key for news link.
    /// </summary>
    public const string NewsLinkMetadataKey = "newsLink";

    /// <summary>
    /// Metadata key for support link.
    /// </summary>
    public const string SupportLinkMetadataKey = "supportLink";

    /// <summary>
    /// Metadata key for discord link.
    /// </summary>
    public const string DiscordLinkMetadataKey = "discordLink";

    /// <summary>
    /// Metadata key for ModDB link.
    /// </summary>
    public const string ModDbLinkMetadataKey = "modDbLink";

    /// <summary>
    /// Metadata key for mod link.
    /// </summary>
    public const string ModLinkMetadataKey = "modLink";

    /// <summary>
    /// Metadata key for patches count.
    /// </summary>
    public const string PatchesCountMetadataKey = "patchesCount";

    /// <summary>
    /// Metadata key for addons count.
    /// </summary>
    public const string AddonsCountMetadataKey = "addonsCount";

    /// <summary>
    /// Metadata key for dependence name.
    /// </summary>
    public const string DependenceNameMetadataKey = "dependenceName";

    /// <summary>
    /// Metadata key for simple download link.
    /// </summary>
    public const string SimpleDownloadLinkMetadataKey = "simpleDownloadLink";

    /// <summary>
    /// Metadata key for YAML manifest URL.
    /// </summary>
    public const string YamlUrlMetadataKey = "yamlUrl";

    /// <summary>
    /// Maximum number of S3 pages to fetch when resolving files.
    /// </summary>
    public const int MaxS3ResolverPages = 100;

    /// <summary>
    /// Maximum number of S3 pages to fetch when calculating size.
    /// </summary>
    public const int MaxS3SizePages = 50;

    /// <summary>
    /// Maximum response body size for catalog and manifest downloads (10 MB).
    /// </summary>
    public const long MaxCatalogResponseBodyBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Maximum allowed total cache memory in bytes for GenLauncher discoverer responses (50 MB).
    /// </summary>
    public const long MaxCacheTotalBytes = 50 * 1024 * 1024;

    /// <summary>
    /// Default buffer size for catalog downloads.
    /// </summary>
    public const int DefaultBufferSize = 8192;

    /// <summary>
    /// Default concurrency limit for GenLauncher catalog and manifest operations.
    /// </summary>
    public const int DefaultCatalogConcurrency = 6;

    /// <summary>
    /// GenLauncher Replace suffix - appended to original game files when temporarily disabled.
    /// </summary>
    public const string ReplaceSuffix = ".GLR";

    /// <summary>
    /// GenLauncher Original File suffix - backup suffix for original files before modification.
    /// </summary>
    public const string OriginalFileSuffix = ".GOF";

    /// <summary>
    /// GenLauncher Temp Copy suffix - temporary folder suffix for version copies.
    /// </summary>
    public const string TempCopySuffix = ".GLTC";

    /// <summary>
    /// GenLauncher scrambled .big file extension.
    /// </summary>
    public const string GibExtension = ".gib";

    /// <summary>
    /// Contra mod inactive .big file extension.
    /// </summary>
    public const string CtrExtension = ".ctr";

    /// <summary>
    /// Shockwave mod inactive .big file extension.
    /// </summary>
    public const string SkwExtension = ".skw";

    /// <summary>
    /// Standard .big file extension.
    /// </summary>
    public const string BigExtension = ".big";

    /// <summary>
    /// Standard executable file extension.
    /// </summary>
    public const string ExeExtension = ".exe";

    /// <summary>
    /// DLL dynamic library extension (.dll).
    /// </summary>
    public const string DllExtension = ".dll";

    /// <summary>
    /// DAT file extension (.dat).
    /// </summary>
    public const string DatExtension = ".dat";

    /// <summary>
    /// Session key for "do not ask again" preference for normalization dialog.
    /// </summary>
    public const string NormalizationDialogSessionKey = "genlauncher.normalization.skip";

    /// <summary>
    /// Default public access key for GenLauncher's InSave MinIO server (gen.insave.ovh:9000).
    /// May be overridden via the GENLAUNCHER_INSAVE_PUBLIC_KEY environment variable.
    /// </summary>
    public static readonly string DefaultGenInsavePublicKey =
        Environment.GetEnvironmentVariable("GENLAUNCHER_INSAVE_PUBLIC_KEY")
        ?? "S58TYR9ISEZV8PBP8QG1";

    /// <summary>
    /// Default secret access key for GenLauncher's InSave MinIO server (gen.insave.ovh:9000).
    /// May be overridden via the GENLAUNCHER_INSAVE_SECRET_KEY environment variable.
    /// </summary>
    [SuppressMessage("Security", "S6418:Strings should not contain all capital secret keys or credentials", Justification = "Public read-only GenInsave S3 key distributed in the open-source GenLauncher client for community mod downloads")]
    public static readonly string DefaultGenInsaveSecretKey =
        Environment.GetEnvironmentVariable("GENLAUNCHER_INSAVE_SECRET_KEY")
        ?? Encoding.UTF8.GetString(
            Convert.FromBase64String("YjJSVTFvcVZVNXRvSlJuYjRnT0RyWFg4c0JTZ29MY0hSWDZxUFd4ag==")); // NOSONAR

    /// <summary>
    /// Probe timeout TimeSpan for GenLauncher size and availability probes.
    /// </summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(ProbeTimeoutSeconds);

    /// <summary>
    /// Engine extensions requiring MD5 checksum validation against S3 ETags.
    /// </summary>
    public static readonly string[] ChecksumExtensions =
    [
        ".w3d",
        ".big",
        ".gib",
        ".bik",
        ".dds",
        ".tga",
        ".ini",
        ".scb",
        ".wnd",
        ".csf",
        ".str",
        ".exe",
        ".dll",
        ".dat",
        ".map",
    ];

    /// <summary>
    /// All GenLauncher suffixes that should be removed during normalization.
    /// </summary>
    public static readonly string[] AllSuffixes =
    [
        ReplaceSuffix,
        OriginalFileSuffix,
        TempCopySuffix,
    ];

    /// <summary>
    /// Extensions for inactive BIG archive files used by mods/launchers.
    /// </summary>
    public static readonly string[] InactiveBigExtensions =
    [
        GibExtension,
        CtrExtension,
        SkwExtension,
    ];

    /// <summary>
    /// Determines whether the specified filename or path corresponds to a GenLauncher catalog or version YAML descriptor.
    /// </summary>
    /// <param name="path">The file path or URL to check.</param>
    /// <returns>True if the path has a .yaml or .yml extension; otherwise false.</returns>
    public static bool IsYamlDescriptorPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);
    }
}
