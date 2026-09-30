namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Render-ready texture with top-first RGBA pixels.
/// </summary>
/// <param name="Name">The texture name.</param>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="PixelData">Row-major RGBA bytes, top row first.</param>
public sealed record W3dRenderTexture(string Name, int Width, int Height, byte[] PixelData);
