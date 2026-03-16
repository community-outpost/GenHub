using GenHub.Core.Models.Enums;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for publisher information including display names, websites, and support URLs.
/// These constants provide standardized publisher metadata for content attribution and user interface display.
/// </summary>
public static class PublisherInfoConstants
{
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
        public const string Website = ""; // TODO: Add website

        /// <summary>Support URL for TheSuperHackers.</summary>
        public const string SupportUrl = "";

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
        public const string Website = ""; // TODO: Add website

        /// <summary>Support URL for Community Outpost.</summary>
        public const string SupportUrl = "";

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
        public const string LogoSource = "avares://GenHub/Assets/Icons/generalshub-icon.png";
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
            _ => (Retail.Name, Retail.Website, Retail.SupportUrl), // Default to retail
        };
    }
}