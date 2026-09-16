using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the Community Outpost content provider.
/// Supports the GenPatcher dl.dat catalog format from legi.cc.
/// </summary>
/// <remarks>
/// Endpoint URLs and timeouts are configured via data-driven configuration.
/// See <c>Providers/communityoutpost.provider.json</c> for runtime-configurable values.
/// </remarks>
[SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Default base URL for Community Outpost service")]
public static class CommunityOutpostConstants
{
    /// <summary>
    /// Base URL for Community Outpost / GenPatcher service.
    /// </summary>
    public const string BaseUrl = "https://legi.cc";

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
    public const string LogoSource = "avares://GenHub/Assets/Logos/communityoutpost-logo.png";

    /// <summary>
    /// Cover image source path for UI display.
    /// </summary>
    public const string CoverSource = "/Assets/Covers/gla-cover.png";

    /// <summary>
    /// Theme color for Community Outpost content.
    /// </summary>
    public const string ThemeColor = "#2D5A27";

    /// <summary>
    /// Description for the content provider.
    /// </summary>
    public const string ProviderDescription = "Official patches, tools, and addons from GenPatcher (Community Outpost)";

    /// <summary>
    /// The name of the content.
    /// </summary>
    public const string ContentName = "Community Patch";

    /// <summary>
    /// Tag and content code for Community Patch items.
    /// </summary>
    public const string CommunityPatchTag = "community-patch";

    /// <summary>
    /// Tag for addon content items.
    /// </summary>
    public const string AddonTag = "addon";

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
    /// The URL for the patch page (used for relative URL resolution).
    /// </summary>
    public const string PatchPageUrl = "https://legi.cc/downloads/genpatcher/";

    /// <summary>
    /// Maximum number of file entries a downloaded Community Outpost archive may contain.
    /// </summary>
    public const int MaxArchiveEntries = 10000;

    /// <summary>
    /// Maximum number of bytes a single Community Outpost archive entry may expand to (2 GiB),
    /// sized to accommodate the largest shipped BIG files.
    /// </summary>
    public const long MaxEntryUncompressedBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// Maximum aggregate uncompressed bytes a Community Outpost archive may expand to (4 GiB).
    /// </summary>
    public const long MaxAggregateUncompressedBytes = 4L * 1024 * 1024 * 1024;

    /// <summary>Display name for Game Clients content type.</summary>
    public const string ContentTypeGameClients = "Game Clients";

    /// <summary>Display name for Addons content type.</summary>
    public const string ContentTypeAddons = "Addons";

    /// <summary>Display name for Tools content type.</summary>
    public const string ContentTypeTools = "Tools";

    /// <summary>Display name for Maps content type.</summary>
    public const string ContentTypeMaps = "Maps";

    /// <summary>
    /// Tags associated with the patch content.
    /// </summary>
    public static readonly IReadOnlyList<string> PatchTags = ["patch", "community", "weekly", "legionnaire"];

    /// <summary>
    /// Tags associated with community patch content.
    /// </summary>
    public static readonly IReadOnlyList<string> CommunityPatchTags = [CommunityPatchTag, PublisherTypeConstants.TheSuperHackers, "weekly", GitHubTopicsConstants.GameClientTopic];

    /// <summary>
    /// Tags associated with official patches.
    /// </summary>
    public static readonly IReadOnlyList<string> OfficialPatchTags = ["patch", "official", "ea"];

    /// <summary>
    /// Tags associated with base game content.
    /// </summary>
    public static readonly IReadOnlyList<string> BaseGameTags = ["base-game", "vanilla"];

    /// <summary>
    /// Tags associated with control bar addons.
    /// </summary>
    public static readonly IReadOnlyList<string> ControlBarTags = [AddonTag, "control-bar", "ui"];

    /// <summary>
    /// Tags associated with hotkey addons.
    /// </summary>
    public static readonly IReadOnlyList<string> HotkeysTags = [AddonTag, "hotkeys", "keyboard"];

    /// <summary>
    /// Tags associated with camera modifications.
    /// </summary>
    public static readonly IReadOnlyList<string> CameraTags = [AddonTag, "camera"];

    /// <summary>
    /// Tags associated with tools.
    /// </summary>
    public static readonly IReadOnlyList<string> ToolsTags = ["tool", "utility", "genpatcher"];

    /// <summary>
    /// Tags associated with maps and missions.
    /// </summary>
    public static readonly IReadOnlyList<string> MapsTags = ["maps", "missions"];

    /// <summary>
    /// Tags associated with visual enhancements.
    /// </summary>
    public static readonly IReadOnlyList<string> VisualsTags = [AddonTag, "visuals", "graphics"];

    /// <summary>
    /// Tags associated with system prerequisites.
    /// </summary>
    public static readonly IReadOnlyList<string> PrerequisitesTags = ["prerequisite", "system"];

    /// <summary>
    /// Tags associated with addons.
    /// </summary>
    public static readonly IReadOnlyList<string> AddonTags = [AddonTag, "community", "genpatcher"];
}
