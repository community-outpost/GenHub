// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Editor-relevant view of one Terrain block, typed per
/// TerrainType::m_terrainTypeFieldParseTable.
/// </summary>
/// <param name="Name">The terrain type key (for example, Dirt).</param>
/// <param name="Texture">The texture file under Art\Textures; null when absent.</param>
/// <param name="IsBlendEdge">True when the entry is a blend edge (excluded from the palette).</param>
/// <param name="Class">The terrain class name (palette sort key); null when absent.</param>
/// <param name="RestrictConstruction">True when construction is restricted on this terrain.</param>
/// <param name="GlintStrength">Water glint strength.</param>
/// <param name="GlintGloss">Water glint gloss.</param>
public sealed record TerrainTypeInfo(
    string Name,
    string? Texture,
    bool IsBlendEdge,
    string? Class,
    bool RestrictConstruction,
    float GlintStrength,
    float GlintGloss);
