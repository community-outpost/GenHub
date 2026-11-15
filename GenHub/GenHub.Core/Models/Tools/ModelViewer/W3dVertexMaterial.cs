namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Vertex material colors and opacity.
/// </summary>
/// <param name="Name">The material name.</param>
/// <param name="DiffuseR">The diffuse red channel.</param>
/// <param name="DiffuseG">The diffuse green channel.</param>
/// <param name="DiffuseB">The diffuse blue channel.</param>
/// <param name="DiffuseA">The diffuse alpha channel.</param>
/// <param name="Opacity">The material opacity from 0 to 1.</param>
public sealed record W3dVertexMaterial(string Name, byte DiffuseR, byte DiffuseG, byte DiffuseB, byte DiffuseA, float Opacity);
