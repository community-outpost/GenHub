// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// The road and bridge catalog (TheTerrainRoads): typed Road and Bridge blocks.
/// All members read live from the SAGE INI database.
/// </summary>
public interface IRoadCatalog
{
    /// <summary>
    /// Gets all road templates ordered by name.
    /// </summary>
    /// <returns>The road templates in name order.</returns>
    IReadOnlyList<RoadInfo> GetRoads();

    /// <summary>
    /// Gets all bridge templates ordered by name.
    /// </summary>
    /// <returns>The bridge templates in name order.</returns>
    IReadOnlyList<BridgeInfo> GetBridges();

    /// <summary>
    /// Finds one road template by name (case-insensitive).
    /// </summary>
    /// <param name="name">The road template name.</param>
    /// <returns>The road template, or null when absent.</returns>
    RoadInfo? FindRoad(string name);

    /// <summary>
    /// Finds one bridge template by name (case-insensitive).
    /// </summary>
    /// <param name="name">The bridge template name.</param>
    /// <returns>The bridge template, or null when absent.</returns>
    BridgeInfo? FindBridge(string name);
}
