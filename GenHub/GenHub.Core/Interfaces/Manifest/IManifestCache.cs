using System.Collections.Generic;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Interfaces.Manifest;

/// <summary>
/// Defines a contract for a centralized cache of all discovered game manifests.
/// </summary>
public interface IManifestCache
{
    /// <summary>
    /// Gets a manifest by its unique identifier.
    /// </summary>
    /// <param name="manifestId">The ID of the manifest to retrieve.</param>
    /// <returns>The <see cref="GameManifest"/> if found; otherwise, null.</returns>
    GameManifest? GetManifest(string manifestId);

    /// <summary>
    /// Adds or updates a manifest in the cache.
    /// </summary>
    /// <param name="manifest">The manifest to add or update.</param>
    void AddOrUpdateManifest(GameManifest manifest);

    /// <summary>
    /// Gets all manifests currently in the cache.
    /// </summary>
    /// <returns>An enumerable collection of all cached manifests.</returns>
    IEnumerable<GameManifest> GetAllManifests();
}
