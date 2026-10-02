// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using System;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// CPU-side terrain render data: mesh plus atlas pixels and layout.
/// </summary>
/// <param name="Vertices">The interleaved primary vertices.</param>
/// <param name="Indices">The primary indices.</param>
/// <param name="ExtraVertices">The three-way extra-blend vertices.</param>
/// <param name="ExtraIndices">The three-way extra-blend indices.</param>
/// <param name="AtlasPixels">The atlas RGBA pixels.</param>
/// <param name="AtlasWidth">The atlas width.</param>
/// <param name="AtlasHeight">The atlas height.</param>
/// <param name="Atlas">The atlas layout.</param>
public sealed record WbTerrainRenderData(
    Memory<float> Vertices,
    Memory<uint> Indices,
    Memory<float> ExtraVertices,
    Memory<uint> ExtraIndices,
    Memory<byte> AtlasPixels,
    int AtlasWidth,
    int AtlasHeight,
    WbTileAtlas Atlas);
