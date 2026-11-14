using GenHub.Core.Constants;
using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Single renderable mesh with geometry, materials, and passes.
/// </summary>
/// <param name="Name">The mesh name.</param>
/// <param name="ContainerName">The container name.</param>
/// <param name="Version">The mesh format version.</param>
/// <param name="Attributes">The mesh attribute flags.</param>
/// <param name="Bounds">The bounding volumes.</param>
/// <param name="Vertices">The object-space vertices.</param>
/// <param name="Normals">The object-space normals.</param>
/// <param name="Triangles">The triangles.</param>
/// <param name="VertexMaterials">The vertex materials.</param>
/// <param name="Shaders">The shaders.</param>
/// <param name="Textures">The referenced textures.</param>
/// <param name="Passes">The material passes.</param>
/// <param name="BoneIndices">The per-vertex bone indices for skins (empty for rigid meshes).</param>
public sealed record W3dMesh(
    string Name,
    string ContainerName,
    uint Version,
    uint Attributes,
    W3dBoundingBox Bounds,
    IReadOnlyList<W3dVector3> Vertices,
    IReadOnlyList<W3dVector3> Normals,
    IReadOnlyList<W3dTriangle> Triangles,
    IReadOnlyList<W3dVertexMaterial> VertexMaterials,
    IReadOnlyList<W3dShader> Shaders,
    IReadOnlyList<W3dTextureReference> Textures,
    IReadOnlyList<W3dMaterialPass> Passes,
    IReadOnlyList<ushort> BoneIndices)
{
    /// <summary>
    /// Gets a value indicating whether the mesh is hidden by default.
    /// </summary>
    public bool IsHidden => (Attributes & W3dConstants.MeshFlags.Hidden) != 0;

    /// <summary>
    /// Gets a value indicating whether both faces render.
    /// </summary>
    public bool IsTwoSided => (Attributes & W3dConstants.MeshFlags.TwoSided) != 0;

    /// <summary>
    /// Gets a value indicating whether the mesh is a deformable skin.
    /// </summary>
    public bool IsSkin => (Attributes & W3dConstants.MeshFlags.GeometryTypeMask) == W3dConstants.MeshFlags.GeometryTypeSkin;

    /// <summary>
    /// Gets the display name combining container and mesh names.
    /// </summary>
    public string DisplayName => string.IsNullOrEmpty(ContainerName) ? Name : $"{ContainerName}.{Name}";
}
