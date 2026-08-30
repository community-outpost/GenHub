using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Extensions.Enums;

/// <summary>
/// Extension methods for Publisher enum.
/// </summary>
public static class PublisherExtensions
{
    /// <summary>
    /// Gets the display name for the publisher.
    /// </summary>
    /// <param name="publisher">The publisher.</param>
    /// <returns>Display name string.</returns>
    public static string GetDisplayName(this Publisher publisher)
    {
        return publisher switch
        {
            Publisher.Steam => "Steam",
            Publisher.EaApp => "EA App",
            Publisher.TheFirstDecade => "The First Decade",
            Publisher.Wine => "Wine/Proton",
            Publisher.CdRom => "CD-ROM",
            Publisher.Retail => "Retail Installation",
            Publisher.GeneralsOnline => "GeneralsOnline",
            Publisher.SuperHackers => "TheSuperHackers",
            Publisher.CncLabs => "CNClabs",
            _ => GameClientConstants.UnknownVersion,
        };
    }
}
