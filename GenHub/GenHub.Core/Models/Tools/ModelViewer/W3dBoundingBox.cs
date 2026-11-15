namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Mesh bounding volumes.
/// </summary>
/// <param name="Min">The bounding box minimum corner.</param>
/// <param name="Max">The bounding box maximum corner.</param>
/// <param name="SphereCenter">The bounding sphere center.</param>
/// <param name="SphereRadius">The bounding sphere radius.</param>
public readonly record struct W3dBoundingBox(W3dVector3 Min, W3dVector3 Max, W3dVector3 SphereCenter, float SphereRadius);
