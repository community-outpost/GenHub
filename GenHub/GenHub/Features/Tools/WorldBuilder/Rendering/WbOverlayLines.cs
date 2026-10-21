// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using System;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// CPU-side 3D overlay lines: waypoint links, playable boundary, and trigger
/// polygons. Vertex layout is position(3), color(4).
/// </summary>
/// <param name="Vertices">Interleaved line-list vertices.</param>
public sealed record WbOverlayLines(Memory<float> Vertices)
{
    /// <summary>
    /// Floats per vertex: position(3), color(4).
    /// </summary>
    public const int StrideFloats = 7;
}
