using System.Collections.Generic;
using System.Numerics;

namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Render-ready mesh with interleaved vertex data.
/// Each vertex packs position, normal, texture coordinates, and diffuse color
/// as twelve floats; texture V is flipped for top-first pixel uploads.
/// </summary>
/// <param name="Name">The mesh display name.</param>
/// <param name="Vertices">The interleaved vertex floats.</param>
/// <param name="Indices">The triangle indices.</param>
/// <param name="TextureIndex">The texture index, or -1 when untextured.</param>
/// <param name="Opacity">The material opacity from 0 to 1.</param>
/// <param name="AlphaTest">Whether alpha-tested cutout applies.</param>
/// <param name="TwoSided">Whether both faces render.</param>
/// <param name="BoneIndex">The pivot index the mesh attaches to, or -1.</param>
/// <param name="IsSkin">Whether the mesh is a deformable skin whose vertices are exported in world bind-pose space.</param>
/// <param name="IsHidden">Whether the mesh is marked hidden in the source model data.</param>
/// <param name="LayoutOffset">The world-space offset placing the mesh inside a composed multi-model scene.</param>
public sealed record W3dRenderMesh(
    string Name,
    IReadOnlyList<float> Vertices,
    IReadOnlyList<uint> Indices,
    int TextureIndex,
    float Opacity,
    bool AlphaTest,
    bool TwoSided,
    int BoneIndex,
    bool IsSkin = false,
    bool IsHidden = false,
    Vector3 LayoutOffset = default)
{
    /// <summary>
    /// Gets the number of floats per vertex.
    /// </summary>
    public static int Stride => 12;

    /// <summary>
    /// Gets the vertex count.
    /// </summary>
    public int VertexCount => Vertices.Count / 12;

    /// <summary>
    /// Gets the triangle count.
    /// </summary>
    public int TriangleCount => Indices.Count / 3;
}
