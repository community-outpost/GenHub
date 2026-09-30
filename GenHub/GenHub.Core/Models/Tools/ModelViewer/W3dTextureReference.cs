namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Texture referenced by a mesh.
/// </summary>
/// <param name="Name">The texture file name.</param>
/// <param name="Attributes">The texture attribute flags.</param>
/// <param name="FrameCount">The animation frame count.</param>
/// <param name="FrameRate">The animation frame rate.</param>
public sealed record W3dTextureReference(string Name, uint Attributes, uint FrameCount, float FrameRate);
