namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the Community Outpost content provider.
/// Supports the GenPatcher dl.dat catalog format from legi.cc.
/// </summary>
/// <remarks>
/// Endpoint URLs and timeouts are configured via data-driven configuration.
/// See <c>Providers/communityoutpost.provider.json</c> for runtime-configurable values.
/// </remarks>
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
    /// Publisher logo source path for UI display.
    /// </summary>
    public const string LogoSource = "/Assets/Logos/communityoutpost-logo.png";

    /// <summary>
    /// Description for the content provider.
    /// </summary>
    public const string ProviderDescription = "Official patches, tools, and addons from GenPatcher (Community Outpost)";

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
    /// Default filename for the downloaded patch zip.
    /// </summary>
    public const string DefaultPatchFilename = "community-patch.zip";

    /// <summary>
    /// Template for the content description.
    /// </summary>
    public const string DescriptionTemplate = "Community Patch - Weekly Build {0}";

    /// <summary>
    /// Regex pattern to find the patch zip link (for legacy scraping).
    /// </summary>
    public const string PatchZipLinkPattern = @"href=[""']([^""']*\.zip)[""']";

    /// <summary>
    /// The file extension for GenPatcher .dat files (which are actually 7z archives).
    /// </summary>
    public const string DatFileExtension = ".dat";

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
}
