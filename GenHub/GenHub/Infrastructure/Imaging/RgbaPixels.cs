using ImageMagick;

namespace GenHub.Infrastructure.Imaging;

/// <summary>
/// Converts between Magick.NET images and tightly packed 8-bit RGBA pixel buffers.
/// </summary>
public static class RgbaPixels
{
    /// <summary>
    /// Copies the image into an 8-bit sRGB RGBA buffer, row by row from the top. CMYK, grayscale and
    /// palette images are converted, and images without an alpha channel come back fully opaque.
    /// </summary>
    /// <param name="image">The source image.</param>
    /// <returns>The packed pixels, four bytes per pixel.</returns>
    public static byte[] ToBytes(IMagickImage<ushort> image)
    {
        using var rgba = image.Clone();
        rgba.ColorSpace = ColorSpace.sRGB;
        rgba.ColorType = ColorType.TrueColorAlpha;
        rgba.Alpha(AlphaOption.Set);
        using var pixels = rgba.GetPixelsUnsafe();
        return pixels.ToByteArray(PixelMapping.RGBA) ?? [];
    }

    /// <summary>
    /// Creates an image from an 8-bit RGBA buffer laid out row by row from the top.
    /// </summary>
    /// <param name="pixels">The packed pixels, four bytes per pixel.</param>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <returns>The new image. The caller owns it.</returns>
    public static MagickImage FromBytes(byte[] pixels, int width, int height)
    {
        var image = new MagickImage();
        image.ReadPixels(pixels, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGBA));
        return image;
    }
}
