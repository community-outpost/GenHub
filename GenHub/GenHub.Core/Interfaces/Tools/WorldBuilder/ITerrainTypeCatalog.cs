// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// The terrain material palette (TheTerrainTypes): typed Terrain blocks.
/// All members read live from the SAGE INI database.
/// </summary>
public interface ITerrainTypeCatalog
{
    /// <summary>
    /// Gets all terrain types ordered by name, including blend edges.
    /// </summary>
    IReadOnlyList<TerrainTypeInfo> All { get; }

    /// <summary>
    /// Gets the palette entries: non-blend-edge types ordered by class, then name.
    /// </summary>
    IReadOnlyList<TerrainTypeInfo> PaletteEntries { get; }

    /// <summary>
    /// Finds one terrain type by name (case-insensitive).
    /// </summary>
    /// <param name="name">The terrain type key.</param>
    /// <returns>The terrain type, or null when absent.</returns>
    TerrainTypeInfo? FindByName(string name);
}
