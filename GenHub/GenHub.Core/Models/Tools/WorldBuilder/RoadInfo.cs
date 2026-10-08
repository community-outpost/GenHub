// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Editor-relevant view of one Road block, typed per
/// TerrainRoadType::m_terrainRoadFieldParseTable.
/// </summary>
/// <param name="Name">The road template name.</param>
/// <param name="Texture">The road texture file; null when absent.</param>
/// <param name="RoadWidth">The road width in world units; null when absent or unparsable.</param>
/// <param name="RoadWidthInTexture">The road width inside the texture; null when absent or unparsable.</param>
public sealed record RoadInfo(
    string Name,
    string? Texture,
    float? RoadWidth,
    float? RoadWidthInTexture);
