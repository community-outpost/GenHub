// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using System;
using System.Collections.Generic;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// CPU-side water render data: translucent quads at the water level.
/// Vertex layout is position(3), color(4).
/// </summary>
/// <param name="Vertices">Interleaved vertex floats.</param>
/// <param name="Indices">Triangle indices.</param>
public sealed record WbWaterData(Memory<float> Vertices, Memory<uint> Indices)
{
    /// <summary>
    /// Floats per vertex: position(3), color(4).
    /// </summary>
    public const int StrideFloats = 7;
}
