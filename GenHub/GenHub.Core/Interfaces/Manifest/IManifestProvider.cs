using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Interfaces.Manifest;

/// <summary>
/// Defines a service for retrieving ContentManifests.
/// </summary>
public interface IManifestProvider
{
    /// <summary>
    /// Asynchronously retrieves the manifest for a specific game version.
    /// </summary>
    /// <param name="gameVersion">The game version for which to retrieve the manifest.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The ContentManifest, or null if not found.</returns>
    Task<ContentManifest?> GetManifestAsync(GameVersion gameVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves the manifest for a specific game version.
    /// </summary>
    /// <param name="gameInstallation">The game version for which to retrieve the manifest.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The ContentManifest, or null if not found.</returns>
    Task<ContentManifest?> GetManifestAsync(GameInstallation gameInstallation, CancellationToken cancellationToken = default);
}
