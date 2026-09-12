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
            Publisher.Steam => PublisherInfoConstants.Steam.Name,
            Publisher.EaApp => PublisherInfoConstants.EaApp.Name,
            Publisher.TheFirstDecade => PublisherInfoConstants.TheFirstDecade.Name,
            Publisher.Wine => PublisherInfoConstants.Wine.Name,
            Publisher.CdRom => PublisherInfoConstants.CdIso.Name,
            Publisher.Retail => PublisherInfoConstants.Retail.Name,
            Publisher.GeneralsOnline => "GeneralsOnline",
            Publisher.SuperHackers => "TheSuperHackers",
            Publisher.CncLabs => "CNClabs",
            Publisher.GenHubLocal => PublisherInfoConstants.GenHubLocal.Name,
            _ => GameClientConstants.UnknownVersion,
        };
    }
}
