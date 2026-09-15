using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.MapManager;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.MapManager;

/// <summary>
/// Manages MapPacks - collections of maps associated with profiles.
/// </summary>
public interface IMapPackService
{
    /// <summary>
    /// Creates a new MapPack from selected maps.
    /// </summary>
    /// <param name="name">The name of the MapPack.</param>
    /// <param name="profileId">Optional profile ID to associate with.</param>
    /// <param name="mapFilePaths">List of map file paths to include.</param>
    /// <returns>The created MapPack.</returns>
    Task<MapPack> CreateMapPackAsync(string name, Guid? profileId, IEnumerable<string> mapFilePaths);

    /// <summary>
    /// Creates a new MapPack manifest using the Content Addressable Storage system.
    /// </summary>
    /// <param name="name">The name of the MapPack.</param>
    /// <param name="targetGame">The target game.</param>
    /// <param name="selectedMaps">The maps to include.</param>
    /// <param name="progress">Progress repoter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The operation result with the created manifest.</returns>
    Task<OperationResult<ContentManifest>> CreateCasMapPackAsync(
        string name,
        GameType targetGame,
        IEnumerable<MapFile> selectedMaps,
        IProgress<ContentStorageProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all available MapPacks.
    /// </summary>
    /// <returns>List of all MapPacks.</returns>
    Task<IReadOnlyList<MapPack>> GetAllMapPacksAsync();

    /// <summary>
    /// Gets MapPacks associated with a specific profile.
    /// </summary>
    /// <param name="profileId">The profile ID.</param>
    /// <returns>List of MapPacks for the profile.</returns>
    Task<IReadOnlyList<MapPack>> GetMapPacksForProfileAsync(Guid profileId);

    /// <summary>
    /// Loads a MapPack by copying its maps to the game directory.
    /// </summary>
    /// <param name="mapPackId">The MapPack ID.</param>
    /// <returns>True if successful.</returns>
    Task<bool> LoadMapPackAsync(ManifestId mapPackId);

    /// <summary>
    /// Unloads a MapPack by removing its maps from the game directory.
    /// </summary>
    /// <param name="mapPackId">The MapPack ID.</param>
    /// <returns>True if successful.</returns>
    Task<bool> UnloadMapPackAsync(ManifestId mapPackId);

    /// <summary>
    /// Deletes a MapPack.
    /// </summary>
    /// <param name="mapPackId">The MapPack ID.</param>
    /// <returns>True if successful.</returns>
    Task<bool> DeleteMapPackAsync(ManifestId mapPackId);

    /// <summary>
    /// Updates an existing MapPack.
    /// </summary>
    /// <param name="mapPack">The updated MapPack.</param>
    /// <returns>True if successful.</returns>
    Task<bool> UpdateMapPackAsync(MapPack mapPack);
}