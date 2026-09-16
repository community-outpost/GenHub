using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Interfaces.Manifest;

/// <summary>
/// Defines a service for retrieving ContentManifests.
/// </summary>
public interface IManifestProvider
{
    /// <summary>
    /// Asynchronously retrieves the manifest for a specific game client.
    /// </summary>
    /// <param name="gameClient">The game client for which to retrieve the manifest.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The ContentManifest, or null if not found.</returns>
    Task<ContentManifest?> GetManifestAsync(GameClient gameClient, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves the manifest for a specific game installation.
    /// </summary>
    /// <param name="gameInstallation">The game installation for which to retrieve the manifest.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The ContentManifest, or null if not found.</returns>
    Task<ContentManifest?> GetManifestAsync(GameInstallation gameInstallation, CancellationToken cancellationToken = default);
}
