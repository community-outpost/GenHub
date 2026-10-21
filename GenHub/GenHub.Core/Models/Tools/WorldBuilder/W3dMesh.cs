// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using System.Numerics;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Parsed mesh: header counts plus geometry, material, and pass arrays.
/// </summary>
/// <param name="Name">The mesh name.</param>
/// <param name="ContainerName">The container name.</param>
/// <param name="Attributes">Geometry attribute flags.</param>
/// <param name="Vertices">Vertex positions.</param>
/// <param name="Normals">Vertex normals; may be shorter than positions when absent.</param>
/// <param name="Triangles">Triangle faces.</param>
/// <param name="Materials">Vertex materials.</param>
/// <param name="Shaders">Fixed-pipeline shaders.</param>
/// <param name="TextureNames">Texture file names in order.</param>
/// <param name="Passes">Material passes in order.</param>
public sealed record W3dMesh(
    string Name,
    string ContainerName,
    uint Attributes,
    IList<Vector3> Vertices,
    IList<Vector3> Normals,
    IList<W3dTriangle> Triangles,
    IReadOnlyList<W3dVertexMaterial> Materials,
    IReadOnlyList<W3dShader> Shaders,
    IReadOnlyList<string> TextureNames,
    IReadOnlyList<W3dMaterialPass> Passes);
