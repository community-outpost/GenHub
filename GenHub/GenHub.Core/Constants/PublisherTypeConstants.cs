using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;

namespace GenHub.Core.Constants;

/// <summary>
/// Publisher type identifiers for content attribution and dynamic discovery.
/// These constants represent content publishers (GitHub, ModDB, etc.) discovered via IContentProvider.
/// </summary>
/// <remarks>
/// IMPORTANT: These are NOT installation sources. Installation sources are handled by InstallationSourceConstants.
/// Publishers are discovered dynamically via IContentProvider implementations.
///
/// Content providers register themselves with the ContentOrchestrator and can come from anywhere:
/// - Official sources (EA/Steam detected installations)
/// - Community platforms (GitHub, ModDB, HTTP endpoints)
/// - Custom sources (any IContentProvider implementation)
///
/// This class contains constants for common publisher types.
/// </remarks>
public static class PublisherTypeConstants
{
    /// <summary>Unknown or unspecified publisher.</summary>
    public const string Unknown = "unknown";

    /// <summary>Wildcard or matching placeholder publisher identifier.</summary>
    public const string Any = "any";

    /// <summary>Display name for GitHub publisher.</summary>
    public const string GitHubDisplayName = "GitHub";

    /// <summary>Display name or alias for Community Outpost publisher.</summary>
    public const string CommunityOutpostDisplayName = "Community Outpost";

    /// <summary>Display name or alias for Generals Online publisher.</summary>
    public const string GeneralsOnlineDisplayName = "Generals Online";

    /// <summary>Display name for The Super Hackers community publisher.</summary>
    public const string TheSuperHackersDisplayName = SuperHackersConstants.PublisherDisplayName;

    /// <summary>GitHub platform publisher.</summary>
    public const string GitHub = "github";

    /// <summary>ModDB platform publisher.</summary>
    public const string ModDB = "moddb";

    /// <summary>Steam Workshop publisher.</summary>
    public const string SteamWorkshop = "steamworkshop";

    /// <summary>EA App publisher.</summary>
    public const string EaApp = "eaapp";

    /// <summary>Official Electronic Arts publisher identifier.</summary>
    public const string Ea = "ea";

    /// <summary>Steam publisher.</summary>
    public const string Steam = "steam";

    /// <summary>Retail publisher.</summary>
    public const string Retail = "retail";

    /// <summary>
    /// GenHub local custom game installation publisher (used for detected and managed game installations).
    /// </summary>
    /// <remarks>
    /// Contrast with <see cref="Local"/>, which represents generic local custom content files.
    /// </remarks>
    public const string GenHubLocal = "genhublocal";

    /// <summary>Generals Online community client publisher.</summary>
    public const string GeneralsOnline = "generalsonline";

    /// <summary>The Super Hackers community publisher.</summary>
    public const string TheSuperHackers = "thesuperhackers";

    /// <summary>Legacy alias for The Super Hackers community publisher.</summary>
    public const string LegacySuperHackers = "superhackers";

    /// <summary>Okladnoj macOS client publisher.</summary>
    public const string Okladnoj = "okladnoj";

    /// <summary>GeneralsX community client publisher.</summary>
    public const string GeneralsX = "generalsx";

    /// <summary>CNC Labs community site.</summary>
    public const string CncLabs = "cnclabs";

    /// <summary>Community Outpost platform.</summary>
    public const string CommunityOutpost = "communityoutpost";

    /// <summary>CSV registry publisher.</summary>
    public const string CsvRegistry = "csvregistry";

    /// <summary>Art of Defense Maps community site.</summary>
    public const string AODMaps = "aodmaps";

    /// <summary>GenLauncher platform publisher.</summary>
    public const string GenLauncher = "genlauncher";

    /// <summary>
    /// Local custom content publisher for user-supplied mods, maps, and custom content.
    /// </summary>
    /// <remarks>
    /// Contrast with <see cref="GenHubLocal"/>, which is scoped to game installation manifests.
    /// </remarks>
    public const string Local = "local";

    /// <summary>GenHub internal system content publisher.</summary>
    public const string GenHubInternal = "genhub";

    /// <summary>
    /// Set of known curated or platform publisher identifiers that must not be registered via untrusted direct package imports.
    /// </summary>
    public static readonly IReadOnlySet<string> CuratedPublishers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        GeneralsOnline,
        CommunityOutpost,
        TheSuperHackers,
        LegacySuperHackers,
        GitHub,
        ModDB,
        CncLabs,
        AODMaps,
        SteamWorkshop,
        Ea,
        EaApp,
        Steam,
        Retail,
        GenHubLocal,
        GenHubInternal,
    };

    /// <summary>
    /// Determines whether the specified publisher is a known curated or platform publisher.
    /// </summary>
    /// <param name="publisher">The publisher identifier to check.</param>
    /// <returns><c>true</c> if the publisher is a curated or platform publisher; otherwise, <c>false</c>.</returns>
    public static bool IsCuratedPublisher(string? publisher) =>
        !string.IsNullOrWhiteSpace(publisher) && CuratedPublishers.Contains(publisher);

    /// <summary>
    /// Set of publisher identifiers trusted to execute installation steps (e.g. installers).
    /// </summary>
    public static readonly IReadOnlySet<string> TrustedExecutablePublishers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        GeneralsOnline,
        CommunityOutpost,
        TheSuperHackers,
    };

    /// <summary>
    /// Maps GameInstallationType enum to publisher type string.
    /// </summary>
    /// <param name="installationType">The game installation type to convert.</param>
    /// <returns>The corresponding publisher type identifier string.</returns>
    public static string FromInstallationType(GameInstallationType installationType)
    {
        return installationType.ToPublisherTypeString();
    }
}
