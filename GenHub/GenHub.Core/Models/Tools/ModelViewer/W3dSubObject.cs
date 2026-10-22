namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Sub-object binding a mesh to a pivot for one level of detail.
/// </summary>
/// <param name="BoneIndex">The pivot index the mesh attaches to.</param>
/// <param name="MeshName">The mesh name in container dot mesh form.</param>
public sealed record W3dSubObject(uint BoneIndex, string MeshName);
