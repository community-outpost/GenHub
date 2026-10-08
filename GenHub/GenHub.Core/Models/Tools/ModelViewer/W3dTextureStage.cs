using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Single texture stage of a material pass.
/// </summary>
/// <param name="TextureIds">The texture indices (one shared entry or one per triangle).</param>
/// <param name="TexCoords">The per-vertex texture coordinates.</param>
/// <param name="PerFaceTexCoordIds">The optional per-face coordinate indices.</param>
public sealed record W3dTextureStage(
    IReadOnlyList<uint> TextureIds,
    IReadOnlyList<W3dVector2> TexCoords,
    IReadOnlyList<W3dIndexTriple> PerFaceTexCoordIds);
