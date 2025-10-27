using GenHub.Core.Models.Enums;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for publisher information including display names, websites, and support URLs.
/// These constants provide standardized publisher metadata for content attribution.
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
            _ => (Retail.Name, Retail.Website, Retail.SupportUrl) // Default to retail
        };
    }
}
