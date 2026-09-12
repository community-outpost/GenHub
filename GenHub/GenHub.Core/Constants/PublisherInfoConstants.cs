using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for publisher information including display names, websites, and support URLs.
/// These constants provide standardized publisher metadata for content attribution and user interface display.
/// </summary>
[SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Centralized URI constants / mock demo paths")]
public static class PublisherInfoConstants
{
    /// <summary>
    /// Default icon source for general GenHub publishers and fallback views.
    /// </summary>
    public const string DefaultGenHubIconSource = "avares://GenHub/Assets/Icons/generalshub-icon.png";

    private static readonly (string[] Keywords, string LogoSource)[] LogoRules =
    [
        (["communityoutpost", "community outpost", "community-outpost"], CommunityOutpost.LogoSource),
        (["superhacker"], TheSuperHackers.LogoSource),
        (["generalsonline", "generals online", "generals-online"], GeneralsOnline.LogoSource),
        (["moddb", "mod db", "mod-db"], ModDB.LogoSource),
        (["cnclabs", "cnc labs", "cnc-labs"], CNCLabs.LogoSource),
        (["aodmaps", "aod maps", "aod-maps"], AODMaps.LogoSource),
        (["genhublocal", "genhub local"], GenHubLocal.LogoSource),
        (["lutris"], Lutris.LogoSource),
        (["github"], GitHub.LogoSource),
    ];

    private static readonly (string[] Keywords, string CoverSource)[] CoverRules =
    [
        (["communityoutpost", "community outpost", "community-outpost"], CommunityOutpostConstants.CoverSource),
        (["superhacker"], SuperHackersConstants.ZeroHourCoverSource),
        (["generalsonline", "generals online", "generals-online"], GeneralsOnlineConstants.CoverSource),
    ];

    /// <summary>
    /// Publisher information for Steam.
    /// </summary>
    public static class Steam
    {
        /// <summary>Display name for Steam publisher.</summary>
        public const string Name = "Steam";

        /// <summary>Website URL for Steam.</summary>
        public const string Website = "https://store.steampowered.com";

        /// <summary>Support URL for Steam.</summary>
        public const string SupportUrl = "https://help.steampowered.com";

        /// <summary>Logo source for Steam.</summary>
        public const string LogoSource = ""; // Placeholder/System managed
    }

    /// <summary>
    /// Publisher information for EA App.
    /// </summary>
    public static class EaApp
    {
        /// <summary>Display name for EA App publisher.</summary>
        public const string Name = "EA App";

        /// <summary>Website URL for EA App.</summary>
        public const string Website = "https://www.ea.com";

        /// <summary>Support URL for EA App.</summary>
        public const string SupportUrl = "https://help.ea.com";

        /// <summary>Logo source for EA App.</summary>
        public const string LogoSource = ""; // Placeholder/System managed
    }

    /// <summary>
    /// Publisher information for The First Decade.
    /// </summary>
    public static class TheFirstDecade
    {
        /// <summary>Display name for The First Decade publisher.</summary>
        public const string Name = "The First Decade";

        /// <summary>Website URL for The First Decade.</summary>
        public const string Website = "https://westwood.com";

        /// <summary>Support URL for The First Decade (empty).</summary>
        public const string SupportUrl = "";

        /// <summary>Logo source for The First Decade.</summary>
        public const string LogoSource = ""; // Placeholder/System managed
    }

    /// <summary>
    /// Publisher information for Wine/Proton.
    /// </summary>
    public static class Wine
    {
        /// <summary>Display name for Wine/Proton publisher.</summary>
        public const string Name = "Wine/Proton";

        /// <summary>Website URL for Wine/Proton (empty).</summary>
        public const string Website = "";

        /// <summary>Support URL for Wine/Proton (empty).</summary>
        public const string SupportUrl = "";

        /// <summary>Logo source for Wine/Proton.</summary>
        public const string LogoSource = ""; // Placeholder/System managed
    }

    /// <summary>
    /// Publisher information for CD-ROM installations.
    /// </summary>
    public static class CdIso
    {
        /// <summary>Display name for CD-ROM publisher.</summary>
        public const string Name = "CD-ROM";

        /// <summary>Website URL for CD-ROM (empty).</summary>
        public const string Website = "";

        /// <summary>Support URL for CD-ROM (empty).</summary>
        public const string SupportUrl = "";

        /// <summary>Logo source for CD-ROM.</summary>
        public const string LogoSource = ""; // Placeholder/System managed
    }

    /// <summary>
    /// Publisher information for retail installations.
    /// </summary>
    public static class Retail
    {
        /// <summary>Display name for retail publisher.</summary>
        public const string Name = "Retail Installation";

        /// <summary>Website URL for retail (empty).</summary>
        public const string Website = "";

        /// <summary>Support URL for retail (empty).</summary>
        public const string SupportUrl = "";

        /// <summary>Logo source for Retail.</summary>
        public const string LogoSource = ""; // Placeholder/System managed
    }

    /// <summary>
    /// Publisher information for Lutris.
    /// </summary>
    public static class Lutris
    {
        /// <summary>Display name for Lutris publisher.</summary>
        public const string Name = "Lutris";

        /// <summary>Website URL for Lutris.</summary>
        public const string Website = "https://lutris.net";

        /// <summary>Support URL for Lutris.</summary>
        public const string SupportUrl = "https://forums.lutris.net";

        /// <summary>Logo source for Lutris.</summary>
        public const string LogoSource = DefaultGenHubIconSource;
    }

    /// <summary>
    /// Publisher information for GenHub Local.
    /// </summary>
    public static class GenHubLocal
    {
        /// <summary>Display name for GenHub Local publisher.</summary>
        public const string Name = "GenHub Local";

        /// <summary>Website URL for GenHub Local.</summary>
        public const string Website = "https://github.com/community-outpost/GenHub";

        /// <summary>Support URL for GenHub Local.</summary>
        public const string SupportUrl = "https://github.com/community-outpost/GenHub/issues";

        /// <summary>Logo source for GenHub Local.</summary>
        public const string LogoSource = DefaultGenHubIconSource;
    }

    /// <summary>
    /// Publisher information for Generals Online.
    /// </summary>
    public static class GeneralsOnline
    {
        /// <summary>Display name for Generals Online publisher.</summary>
        public const string Name = "Generals Online";

        /// <summary>Website URL for Generals Online.</summary>
        public const string Website = "https://www.playgenerals.online/";

        /// <summary>Support URL for Generals Online.</summary>
        public const string SupportUrl = "https://www.playgenerals.online/support";

        /// <summary>Logo source for Generals Online.</summary>
        public const string LogoSource = "avares://GenHub/Assets/Logos/generalsonline-logo.png";
    }

    /// <summary>
    /// Publisher information for TheSuperHackers.
    /// </summary>
    public static class TheSuperHackers
    {
        /// <summary>Display name for TheSuperHackers publisher.</summary>
        public const string Name = "TheSuperHackers";

        /// <summary>Website URL for TheSuperHackers.</summary>
        public const string Website = "https://github.com/thesuperhackers";

        /// <summary>Support URL for TheSuperHackers.</summary>
        public const string SupportUrl = "https://github.com/thesuperhackers/GeneralsGameCode/issues";

        /// <summary>Logo source for TheSuperHackers.</summary>
        public const string LogoSource = "avares://GenHub/Assets/Logos/thesuperhackers-logo.png";
    }

    /// <summary>
    /// Publisher information for Community Outpost.
    /// </summary>
    public static class CommunityOutpost
    {
        /// <summary>Display name for Community Outpost publisher.</summary>
        public const string Name = "CommunityOutpost";

        /// <summary>Website URL for Community Outpost.</summary>
        public const string Website = "https://legi.cc";

        /// <summary>Support URL for Community Outpost.</summary>
        public const string SupportUrl = "https://legi.cc/patch";

        /// <summary>Logo source for Community Outpost.</summary>
        public const string LogoSource = "avares://GenHub/Assets/Logos/communityoutpost-logo.png";
    }

    /// <summary>
    /// Publisher information for ModDB.
    /// </summary>
    public static class ModDB
    {
        /// <summary>Display name for ModDB publisher.</summary>
        public const string Name = "ModDB";

        /// <summary>Website URL for ModDB.</summary>
        public const string Website = "https://www.moddb.com";

        /// <summary>Support URL for ModDB.</summary>
        public const string SupportUrl = "https://www.moddb.com/help";

        /// <summary>Logo source for ModDB.</summary>
        public const string LogoSource = "avares://GenHub/Assets/Logos/moddb-logo.png";
    }

    /// <summary>
    /// Publisher information for CNC Labs.
    /// </summary>
    public static class CNCLabs
    {
        /// <summary>Display name for CNC Labs publisher.</summary>
        public const string Name = "CNC Labs";

        /// <summary>Website URL for CNC Labs.</summary>
        public const string Website = "https://www.cnclabs.com";

        /// <summary>Support URL for CNC Labs.</summary>
        public const string SupportUrl = "https://www.cnclabs.com";

        /// <summary>Logo source for CNC Labs.</summary>
        public const string LogoSource = "avares://GenHub/Assets/Logos/cnclabs-logo.png";
    }

    /// <summary>
    /// Publisher information for GitHub.
    /// </summary>
    public static class GitHub
    {
        /// <summary>Display name for GitHub publisher.</summary>
        public const string Name = "GitHub";

        /// <summary>Website URL for GitHub.</summary>
        public const string Website = "https://github.com";

        /// <summary>Support URL for GitHub.</summary>
        public const string SupportUrl = "https://docs.github.com";

        /// <summary>Logo source for GitHub.</summary>
        public const string LogoSource = "avares://GenHub/Assets/Logos/github-logo.png";
    }

    /// <summary>
    /// Publisher information for AODMaps.
    /// </summary>
    public static class AODMaps
    {
        /// <summary>Display name for AODMaps publisher.</summary>
        public const string Name = "AODMaps";

        /// <summary>Website URL for AODMaps.</summary>
        public const string Website = "https://aodmaps.com";

        /// <summary>Support URL for AODMaps.</summary>
        public const string SupportUrl = "https://aodmaps.com";

        /// <summary>Logo source for AODMaps.</summary>
        public const string LogoSource = "avares://GenHub/Assets/Logos/aodmaps-logo.png";
    }

    /// <summary>
    /// Publisher information for All Publishers view.
    /// </summary>
    public static class AllPublishers
    {
        /// <summary>Display name for All Publishers view.</summary>
        public const string Name = "All Publishers";

        /// <summary>Logo source for All Publishers view.</summary>
        public const string LogoSource = DefaultGenHubIconSource;
    }

    /// <summary>
    /// Publisher information for Unknown/Other publishers.
    /// </summary>
    public static class Unknown
    {
        /// <summary>Display name for Unknown publisher.</summary>
        public const string Name = "Unknown";

        /// <summary>Website URL for Unknown.</summary>
        public const string Website = "about:blank";

        /// <summary>Support URL for Unknown.</summary>
        public const string SupportUrl = "about:blank";

        /// <summary>Logo source for Unknown.</summary>
        public const string LogoSource = DefaultGenHubIconSource;
    }

    /// <summary>
    /// Gets publisher information for the specified installation type.
    /// </summary>
    /// <param name="installationType">The game installation type.</param>
    /// <returns>A tuple containing (Name, Website, SupportUrl) for the publisher.</returns>
    public static (string Name, string Website, string SupportUrl) GetPublisherInfo(GameInstallationType installationType)
    {
        return installationType switch
        {
            GameInstallationType.Steam => (Steam.Name, Steam.Website, Steam.SupportUrl),
            GameInstallationType.EaApp => (EaApp.Name, EaApp.Website, EaApp.SupportUrl),
            GameInstallationType.TheFirstDecade => (TheFirstDecade.Name, TheFirstDecade.Website, TheFirstDecade.SupportUrl),
            GameInstallationType.Wine => (Wine.Name, Wine.Website, Wine.SupportUrl),
            GameInstallationType.CDISO => (CdIso.Name, CdIso.Website, CdIso.SupportUrl),
            GameInstallationType.Retail => (Retail.Name, Retail.Website, Retail.SupportUrl),
            GameInstallationType.Lutris => (Lutris.Name, Lutris.Website, Lutris.SupportUrl),
            GameInstallationType.Custom => (GenHubLocal.Name, GenHubLocal.Website, GenHubLocal.SupportUrl),
            _ => (Unknown.Name, Unknown.Website, Unknown.SupportUrl),
        };
    }

    /// <summary>
    /// Gets the logo source URI for a publisher or content item based on publisher ID, provider name, or title.
    /// </summary>
    /// <param name="publisherIdOrName">The publisher ID or provider display name.</param>
    /// <param name="contentIdOrName">The content ID, title, or manifest ID context.</param>
    /// <returns>An avares:// URI string pointing to the logo image asset, or null if unmapped.</returns>
    public static string? GetPublisherLogo(string? publisherIdOrName, string? contentIdOrName = null)
    {
        var primary = MatchLogo(publisherIdOrName);
        var secondary = MatchLogo(contentIdOrName);

        // If primary matched generic GitHub, but secondary matched a specific publisher, prefer the specific publisher
        if (primary == GitHub.LogoSource && secondary != null && secondary != GitHub.LogoSource)
        {
            return secondary;
        }

        return primary ?? secondary;
    }

    /// <summary>
    /// Gets the cover source URI for a publisher or content item based on publisher ID, provider name, or title.
    /// </summary>
    /// <param name="publisherIdOrName">The publisher ID or provider display name.</param>
    /// <param name="contentIdOrName">The content ID, title, or manifest ID context.</param>
    /// <returns>A cover image path string, or null if unmapped.</returns>
    public static string? GetPublisherCover(string? publisherIdOrName, string? contentIdOrName = null)
    {
        var primary = MatchCover(publisherIdOrName);
        var secondary = MatchCover(contentIdOrName);

        return primary ?? secondary;
    }

    private static string? MatchLogo(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        foreach (var (keywords, logoSource) in LogoRules)
        {
            if (keywords.Any(keyword => input.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return logoSource;
            }
        }

        return null;
    }

    private static string? MatchCover(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        foreach (var (keywords, coverSource) in CoverRules)
        {
            if (keywords.Any(keyword => input.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return coverSource;
            }
        }

        return null;
    }
}
