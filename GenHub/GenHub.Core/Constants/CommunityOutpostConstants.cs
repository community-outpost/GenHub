using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results.Content;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

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
    /// Content code alias for the Retail Community Patch build in registry.
    /// </summary>
    public const string CommunityPatchRetailCode = "community-patch-retail";

    /// <summary>
    /// Content code and tag for the Non-Retail (stream) Community Patch build.
    /// </summary>
    public const string CommunityPatchNonRetCode = "community-patch-nonret";

    /// <summary>
    /// Tag for the Non-Retail Community Patch build.
    /// </summary>
    public const string CommunityPatchNonRetTag = "community-patch-nonret";

    /// <summary>
    /// Tag for non-retail game client builds.
    /// </summary>
    public const string NonRetailTag = "non-retail";

    /// <summary>
    /// Tag for retail-compatible game client builds.
    /// </summary>
    public const string RetailCompatibleTag = "retail-compatible";

    /// <summary>
    /// Tag for stream-specific game client builds.
    /// </summary>
    public const string StreamTag = "stream";

    /// <summary>
    /// Display name for the retail-compatible Community Patch build.
    /// </summary>
    public const string CommunityPatchRetailDisplayName = "Community Patch (TheSuperHackers Build)";

    /// <summary>
    /// Display name for the non-retail (stream) Community Patch build.
    /// </summary>
    public const string CommunityPatchNonRetDisplayName = "Community Patch (TheSuperHackers Non-Retail Build)";

    /// <summary>
    /// Description for the retail-compatible Community Patch build.
    /// </summary>
    public const string CommunityPatchRetailDescription = "The latest TheSuperHackers patch build for Zero Hour. Compatible with regular C&C Generals Zero Hour retail multiplayer.";

    /// <summary>
    /// Description for the non-retail (stream) Community Patch build.
    /// </summary>
    public const string CommunityPatchNonRetDescription = "The latest TheSuperHackers non-retail patch build for Zero Hour (stream build). Note: This build has a different executable CRC and is not compatible with regular C&C Generals Zero Hour retail multiplayer.";

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

    /// <summary>Tag for weekly patch builds.</summary>
    public const string WeeklyTag = "weekly";

    /// <summary>Keyword identifying nonret builds.</summary>
    public const string NonRetKeyword = "nonret";

    /// <summary>Keyword identifying hyphenated non-ret builds.</summary>
    public const string NonRetHyphenatedKeyword = "non-ret";

    /// <summary>Keyword identifying nonretail builds.</summary>
    public const string NonRetailKeyword = "nonretail";

    /// <summary>
    /// Tags associated with the patch content.
    /// </summary>
    public static readonly IReadOnlyList<string> PatchTags = ["patch", "community", WeeklyTag, "legionnaire"];

    /// <summary>
    /// Tags associated with community patch content.
    /// </summary>
    public static readonly IReadOnlyList<string> CommunityPatchTags = [CommunityPatchTag, PublisherTypeConstants.TheSuperHackers, WeeklyTag, GitHubTopicsConstants.GameClientTopic];

    /// <summary>
    /// Tags associated with the retail-compatible community patch content.
    /// </summary>
    public static readonly IReadOnlyList<string> CommunityPatchRetailTags =
        [CommunityPatchTag, PublisherTypeConstants.TheSuperHackers, WeeklyTag, GitHubTopicsConstants.GameClientTopic, RetailCompatibleTag];

    /// <summary>
    /// Tags associated with the non-retail (stream) community patch content.
    /// </summary>
    public static readonly IReadOnlyList<string> CommunityPatchNonRetTags =
        [CommunityPatchTag, CommunityPatchNonRetCode, PublisherTypeConstants.TheSuperHackers, WeeklyTag, GitHubTopicsConstants.GameClientTopic, NonRetailTag, StreamTag];

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

    /// <summary>
    /// Checks whether an identifier, filename, or display name represents a non-retail (stream) build.
    /// </summary>
    /// <param name="name">The name, identifier, or filename to check.</param>
    /// <returns><c>true</c> if non-retail; otherwise, <c>false</c>.</returns>
    public static bool IsNonRetailIdentifier(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name.Contains(NonRetKeyword, System.StringComparison.OrdinalIgnoreCase) ||
               name.Contains(NonRetHyphenatedKeyword, System.StringComparison.OrdinalIgnoreCase) ||
               name.Contains(NonRetailKeyword, System.StringComparison.OrdinalIgnoreCase) ||
               name.Contains(NonRetailTag, System.StringComparison.OrdinalIgnoreCase) ||
               name.Contains(StreamTag, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether an identifier, tag, or display name represents official base game content (e.g. 10zh, 10gn).
    /// </summary>
    /// <param name="value">The string value to check.</param>
    /// <returns><c>true</c> if base game content; otherwise, <c>false</c>.</returns>
    public static bool IsBaseGameIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("10zh", System.StringComparison.OrdinalIgnoreCase) ||
               value.Equals("10gn", System.StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".10zh", System.StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".10gn", System.StringComparison.OrdinalIgnoreCase) ||
               value.Equals("basegame", System.StringComparison.OrdinalIgnoreCase) ||
               value.Equals("base-game", System.StringComparison.OrdinalIgnoreCase) ||
               value.Equals("official", System.StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Zero Hour 1.04", System.StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Generals 1.08", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether an identifier, tag, or display name represents Community Patch content.
    /// </summary>
    /// <param name="value">The string value to check.</param>
    /// <returns><c>true</c> if Community Patch; otherwise, <c>false</c>.</returns>
    public static bool IsCommunityPatchIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || IsBaseGameIdentifier(value))
        {
            return false;
        }

        return value.Contains(CommunityPatchTag, System.StringComparison.OrdinalIgnoreCase) ||
               value.Contains(CommunityPatchNonRetCode, System.StringComparison.OrdinalIgnoreCase) ||
               value.Contains(ContentName, System.StringComparison.OrdinalIgnoreCase) ||
               value.Equals("CommunityPatch", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a content search result represents Community Patch content.
    /// </summary>
    /// <param name="result">The search result to check.</param>
    /// <returns><c>true</c> if Community Patch; otherwise, <c>false</c>.</returns>
    public static bool IsCommunityPatch(ContentSearchResult? result)
    {
        if (result == null)
        {
            return false;
        }

        if (IsBaseGameIdentifier(result.Id) || IsBaseGameIdentifier(result.Name) ||
            (result.Tags != null && result.Tags.Any(IsBaseGameIdentifier)))
        {
            return false;
        }

        return IsCommunityPatchIdentifier(result.Id) ||
               IsCommunityPatchIdentifier(result.Name) ||
               (result.Tags != null && result.Tags.Any(IsCommunityPatchIdentifier)) ||
               (result.ResolverMetadata != null && result.ResolverMetadata.TryGetValue("category", out var cat) &&
                string.Equals(cat, "CommunityPatch", System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks whether a content manifest represents Community Patch content.
    /// </summary>
    /// <param name="manifest">The manifest to check.</param>
    /// <returns><c>true</c> if Community Patch; otherwise, <c>false</c>.</returns>
    public static bool IsCommunityPatch(ContentManifest? manifest)
    {
        if (manifest == null)
        {
            return false;
        }

        if (IsBaseGameIdentifier(manifest.Id.Value) || IsBaseGameIdentifier(manifest.Name) ||
            (manifest.Metadata?.Tags != null && manifest.Metadata.Tags.Any(IsBaseGameIdentifier)))
        {
            return false;
        }

        return IsCommunityPatchIdentifier(manifest.Id.Value) ||
               IsCommunityPatchIdentifier(manifest.Name) ||
               (manifest.Metadata?.Tags != null && manifest.Metadata.Tags.Any(IsCommunityPatchIdentifier));
    }
}
