using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Models.Enums;

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

    /// <summary>GitHub platform publisher.</summary>
    public const string GitHub = "github";

    /// <summary>ModDB platform publisher.</summary>
    public const string ModDB = "moddb";

    /// <summary>Steam Workshop publisher.</summary>
    public const string SteamWorkshop = "steamworkshop";

    /// <summary>EA App publisher.</summary>
    public const string EaApp = "eaapp";

    /// <summary>Steam publisher.</summary>
    public const string Steam = "steam";

    /// <summary>Retail publisher.</summary>
    public const string Retail = "retail";

    /// <summary>Generals Online community client publisher.</summary>
    public const string GeneralsOnline = "generalsonline";

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
