using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.MapManager;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.MapManager;

/// <summary>
/// Manages map directory operations.
/// </summary>
public interface IMapDirectoryService
{
    /// <summary>
    /// Gets the map directory path for the specified game version.
    /// </summary>
    /// <param name="version">The game version.</param>
    /// <returns>The path to the map directory.</returns>
    string GetMapDirectory(GameType version);

    /// <summary>
    /// Ensures the map directory exists, creating it if necessary.
    /// </summary>
    /// <param name="version">The game version.</param>
    void EnsureDirectoryExists(GameType version);

    /// <summary>
    /// Gets all map files for the specified game version.
    /// </summary>
    /// <param name="version">The game version.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of map files.</returns>
    Task<IReadOnlyList<MapFile>> GetMapsAsync(GameType version, CancellationToken ct = default);

    /// <summary>
    /// Deletes the specified map files (moves to Recycle Bin).
    /// </summary>
    /// <param name="maps">The maps to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if deletion was successful.</returns>
    Task<bool> DeleteMapsAsync(IEnumerable<MapFile> maps, CancellationToken ct = default);

    /// <summary>
    /// Opens the map directory in Windows Explorer.
    /// </summary>
    /// <param name="version">The game version.</param>
    void OpenInExplorer(GameType version);

    /// <summary>
    /// Reveals a specific file in Windows Explorer.
    /// </summary>
    /// <param name="map">The map file to reveal.</param>
    void RevealInExplorer(MapFile map);

    /// <summary>
    /// Renames a map, including its parent directory if applicable.
    /// </summary>
    /// <param name="map">The map to rename.</param>
    /// <param name="newName">The new name (without extension).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if successful, false otherwise.</returns>
    Task<bool> RenameMapAsync(MapFile map, string newName, CancellationToken ct = default);
}
