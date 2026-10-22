namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Pick result naming the closest intersected mesh.
/// </summary>
/// <param name="MeshIndex">The scene mesh index.</param>
/// <param name="Distance">The ray distance to the hit.</param>
public sealed record W3dPickResult(int MeshIndex, float Distance);
