using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.GameProfiles;

/// <summary>
/// Service for loading profile-related content including game installations,
/// available content, and enabled content for profiles.
/// </summary>
/// <remarks>
/// This service encapsulates content loading logic to support game profile management
/// and UI presentation. It coordinates between game installation detection, manifest pools,
/// and profile configuration to provide comprehensive content data.
/// </remarks>
public interface IProfileContentLoader
{
    /// <summary>
    /// Loads available game installations from detected installations.
    /// Creates ONE entry per unique game type in each installation (groups multiple clients of same type).
    /// </summary>
    /// <returns>A collection of content display items representing available game installations.</returns>
    Task<ObservableCollection<ContentDisplayItem>> LoadAvailableGameInstallationsAsync();

    /// <summary>
    /// Loads ALL available game clients from detected installations.
    /// Creates one entry for EACH game client, including GeneralsOnline variants and third-party clients.
    /// Use this for populating executable/game client filters.
    /// </summary>
    /// <returns>A collection of content display items representing all available game clients.</returns>
    Task<ObservableCollection<ContentDisplayItem>> LoadAvailableGameClientsAsync();

    /// <summary>
    /// Loads available content filtered by the specified content type.
    /// </summary>
    /// <param name="contentType">The content type to filter by.</param>
    /// <param name="availableGameInstallations">Pre-loaded game installations for GameClient resolution.</param>
    /// <param name="enabledContentIds">List of already-enabled content IDs to mark items as enabled.</param>
    /// <returns>A collection of content display items for the specified type.</returns>
    Task<ObservableCollection<ContentDisplayItem>> LoadAvailableContentAsync(
        ContentType contentType,
        ObservableCollection<ContentDisplayItem> availableGameInstallations,
        IEnumerable<string> enabledContentIds);

    /// <summary>
    /// Loads enabled content for a specific game profile.
    /// </summary>
    /// <param name="profile">The game profile containing enabled content IDs.</param>
    /// <returns>A collection of content display items representing enabled content.</returns>
    Task<ObservableCollection<ContentDisplayItem>> LoadEnabledContentForProfileAsync(GameProfile profile);
}
