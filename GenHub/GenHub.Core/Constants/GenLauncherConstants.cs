using System;
using System.Diagnostics.CodeAnalysis;

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
    /// Session key for "do not ask again" preference for normalization dialog.
    /// </summary>
    public const string NormalizationDialogSessionKey = "genlauncher.normalization.skip";

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
}
