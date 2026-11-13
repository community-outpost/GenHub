namespace GenHub.Core.Models.Tools.TextureEditor;

/// <summary>
/// Represents a decoded texture in portable 8-bit RGBA pixel data.
/// This type is UI framework agnostic so core services stay testable.
/// </summary>
/// <param name="Width">The image width in pixels.</param>
/// <param name="Height">The image height in pixels.</param>
/// <param name="PixelData">Row-major RGBA bytes, top row first.</param>
public sealed record DecodedTexture(int Width, int Height, byte[] PixelData)
{
    /// <summary>
    /// Gets a value indicating whether any pixel is not fully opaque.
    /// </summary>
    public bool HasAlpha
    {
        get
        {
            for (int i = 3; i < PixelData.Length; i += 4)
            {
                if (PixelData[i] != 255)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
