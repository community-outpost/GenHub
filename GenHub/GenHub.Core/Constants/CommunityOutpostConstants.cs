namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the Community Outpost content provider.
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
    /// The URL where the patch is hosted.
    /// </summary>
    public const string PatchPageUrl = "https://legi.cc/patch";

    /// <summary>
    /// Description for the content provider.
    /// </summary>
    public const string ProviderDescription = "Community patches and builds from the Generals community";

    /// <summary>
    /// Default filename for the downloaded patch zip.
    /// </summary>
    public const string DefaultPatchFilename = "community-patch.zip";

    /// <summary>
    /// Publisher website URL.
    /// </summary>
    public const string PublisherWebsite = "https://legi.cc";

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
    public const string DiscovererDescription = "Discovers community patches from Community Outpost";

    /// <summary>
    /// Description for the deliverer.
    /// </summary>
    public const string DelivererDescription = "Delivers Community Outpost content via ZIP extraction and CAS storage";

    /// <summary>
    /// Regex pattern to find the patch zip link.
    /// </summary>
    public const string PatchZipLinkPattern = @"href=[""']([^""']*\.zip)[""']";

    /// <summary>
    /// Tags associated with the patch content.
    /// </summary>
    public static readonly string[] PatchTags = ["patch", "community", "weekly", "legionnaire"];
}
