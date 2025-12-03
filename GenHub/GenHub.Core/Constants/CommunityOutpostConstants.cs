namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the Community Outpost content provider.
/// Supports the GenPatcher dl.dat catalog format from legi.cc.
/// </summary>
public static class CommunityOutpostConstants
{
    /// <summary>
    /// The publisher ID for Community Outpost.
    /// </summary>
    public const string PublisherId = "community-outpost";

    /// <summary>
    /// The publisher type identifier (used by providers/discoverers).
    /// </summary>
    public const string PublisherType = "communityoutpost";

    /// <summary>
    /// The display name for the publisher.
    /// </summary>
    public const string PublisherName = "Community Outpost";

    /// <summary>
    /// Publisher icon color.
    /// </summary>
    public const string IconColor = "#2196F3";

    /// <summary>
    /// The URL where the patch page is hosted.
    /// </summary>
    public const string PatchPageUrl = "https://legi.cc/patch";

    /// <summary>
    /// The URL for the GenPatcher dl.dat catalog file.
    /// This file contains the list of all available content with mirrors.
    /// Format: [4-char-code] [file-size] [mirror-name] [download-url].
    /// </summary>
    public const string CatalogUrl = "https://legi.cc/gp2/dl.dat";

    /// <summary>
    /// Base URL for legi.cc mirror files.
    /// </summary>
    public const string LegiMirrorBaseUrl = "https://legi.cc/gp2/f/";

    /// <summary>
    /// Description for the content provider.
    /// </summary>
    public const string ProviderDescription = "Official patches, tools, and addons from GenPatcher (Community Outpost)";

    /// <summary>
    /// Default filename for the downloaded patch zip.
    /// </summary>
    public const string DefaultPatchFilename = "community-patch.zip";

    /// <summary>
    /// Publisher website URL.
    /// </summary>
    public const string PublisherWebsite = "https://legi.cc";

    /// <summary>
    /// GenTool website URL (also hosts mirrors).
    /// </summary>
    public const string GentoolWebsite = "https://gentool.net";

    /// <summary>
    /// Template for the content description.
    /// </summary>
    public const string DescriptionTemplate = "Community Patch - Weekly Build {0}";

    /// <summary>
    /// The name of the content.
    /// </summary>
    public const string ContentName = "Community Patch";

    /// <summary>
    /// Description for the discoverer.
    /// </summary>
    public const string DiscovererDescription = "Discovers content from GenPatcher catalog (dl.dat)";

    /// <summary>
    /// Description for the deliverer.
    /// </summary>
    public const string DelivererDescription = "Delivers Community Outpost content via 7z extraction and CAS storage";

    /// <summary>
    /// Regex pattern to find the patch zip link (for legacy scraping).
    /// </summary>
    public const string PatchZipLinkPattern = @"href=[""']([^""']*\.zip)[""']";

    /// <summary>
    /// Tags associated with the patch content.
    /// </summary>
    public static readonly string[] PatchTags = ["patch", "community", "weekly", "legionnaire"];

    /// <summary>
    /// Tags associated with official patches.
    /// </summary>
    public static readonly string[] OfficialPatchTags = ["patch", "official", "ea"];

    /// <summary>
    /// Tags associated with addons.
    /// </summary>
    public static readonly string[] AddonTags = ["addon", "community", "genpatcher"];

    /// <summary>
    /// Tags associated with tools.
    /// </summary>
    public static readonly string[] ToolsTags = ["tool", "utility", "genpatcher"];

    /// <summary>
    /// The file extension for GenPatcher .dat files (which are actually 7z archives).
    /// </summary>
    public const string DatFileExtension = ".dat";

    /// <summary>
    /// Timeout in seconds for downloading the catalog file.
    /// </summary>
    public const int CatalogDownloadTimeoutSeconds = 30;

    /// <summary>
    /// Timeout in seconds for downloading content files.
    /// </summary>
    public const int ContentDownloadTimeoutSeconds = 300;
}
