namespace GenHub.Features.Tools.IniEditor.ViewModels;

/// <summary>
/// Sub-object row of the 3D model preview.
/// </summary>
/// <param name="MeshIndex">The scene mesh index.</param>
/// <param name="Name">The mesh display name.</param>
/// <param name="TriangleCount">The triangle count.</param>
/// <param name="VertexCount">The vertex count.</param>
/// <param name="BoneName">The attached pivot name, or null when unbound.</param>
/// <param name="TextureName">The texture name, or null when untextured.</param>
/// <param name="PartLabel">The owning composite part label, or null for single-model previews.</param>
public sealed record W3dPreviewMeshItem(
    int MeshIndex,
    string Name,
    int TriangleCount,
    int VertexCount,
    string? BoneName,
    string? TextureName,
    string? PartLabel = null);
