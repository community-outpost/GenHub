namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Triple of indices addressing per-vertex data such as texture coordinates.
/// </summary>
/// <param name="I0">The first index.</param>
/// <param name="I1">The second index.</param>
/// <param name="I2">The third index.</param>
public readonly record struct W3dIndexTriple(uint I0, uint I1, uint I2);
