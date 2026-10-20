using ImageMagick;
using System.IO;

namespace GenHub.Infrastructure.Imaging;

/// <summary>
/// Writes images as the TGA layout the game reads: uncompressed 32-bit BGRA (TGA type 2),
/// stored bottom-up with a bottom-left origin.
/// </summary>
public static class TgaWriter
{
    /// <summary>
    /// Writes <paramref name="image"/> to <paramref name="destinationPath"/> as an uncompressed 32-bit TGA.
    /// The image is modified in place: it is converted to RGBA, metadata is stripped,
    /// and the rows are flipped for the bottom-left origin.
    /// </summary>
    /// <param name="image">The image to write.</param>
    /// <param name="destinationPath">The output file path.</param>
    public static void Write32BitUncompressed(IMagickImage<ushort> image, string destinationPath)
    {
        PrepareForTga(image);
        image.Write(destinationPath, MagickFormat.Tga);
    }

    /// <summary>
    /// Writes <paramref name="image"/> to <paramref name="destination"/> as an uncompressed 32-bit TGA.
    /// The image is modified in place, as described on the path overload.
    /// </summary>
    /// <param name="image">The image to write.</param>
    /// <param name="destination">The output stream.</param>
    public static void Write32BitUncompressed(IMagickImage<ushort> image, Stream destination)
    {
        PrepareForTga(image);
        image.Write(destination, MagickFormat.Tga);
    }

    private static void PrepareForTga(IMagickImage<ushort> image)
    {
        image.Strip();

        // Grayscale and palette sources would otherwise be written as TGA type 3 or type 1.
        image.ColorSpace = ColorSpace.sRGB;
        image.ColorType = ColorType.TrueColorAlpha;
        image.Alpha(AlphaOption.Set);
        image.Depth = 8;
        image.Settings.Compression = CompressionMethod.NoCompression;

        // ImageMagick writes rows in memory order and only sets the origin flag from Orientation,
        // so the rows are flipped first to keep the pixels upright.
        image.Flip();
        image.Orientation = OrientationType.BottomLeft;
    }
}
