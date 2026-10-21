// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Tools.TextureEditor;
using System;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// One baked model draw: world-space vertices, indices, resolved texture, and render state.
/// Vertex layout is position(3), normal(3), uv(2), color(4).
/// </summary>
/// <param name="Vertices">Interleaved vertex floats.</param>
/// <param name="Indices">Triangle indices.</param>
/// <param name="TextureName">Stage-zero texture name; null when untextured.</param>
/// <param name="Texture">Decoded texture pixels; null when untextured or missing.</param>
/// <param name="State">Mapped render state.</param>
/// <param name="TwoSided">True when backface culling must be disabled.</param>
public sealed record WbModelDraw(
    Memory<float> Vertices,
    Memory<uint> Indices,
    string? TextureName,
    DecodedTexture? Texture,
    W3dGlState State,
    bool TwoSided)
{
    /// <summary>
    /// Floats per vertex: position(3), normal(3), uv(2), color(4).
    /// </summary>
    public const int StrideFloats = 12;
}
