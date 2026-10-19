using ImageMagick;

namespace GenHub.Infrastructure.Imaging;

/// <summary>
/// Writes images as the TGA layout the game reads: uncompressed 32-bit BGRA (TGA type 2),
/// stored bottom-up with a bottom-left origin.
/// </summary>
public static class TgaWriter
{
    /// <summary>
    /// Writes <paramref name="image"/> to <paramref name="destinationPath"/> as an uncompressed 32-bit TGA.
    /// The image is modified in place: an alpha channel is added if missing, metadata is stripped,
    /// and the rows are flipped for the bottom-left origin.
    /// </summary>
    /// <param name="image">The image to write.</param>
    /// <param name="destinationPath">The output file path.</param>
    public static void Write32BitUncompressed(IMagickImage<ushort> image, string destinationPath)
    {
        image.Strip();
        image.Alpha(AlphaOption.Set);
        image.Depth = 8;
        image.Settings.Compression = CompressionMethod.NoCompression;

        // ImageMagick writes rows in memory order and only sets the origin flag from Orientation,
        // so the rows are flipped first to keep the pixels upright.
        image.Flip();
        image.Orientation = OrientationType.BottomLeft;

        image.Write(destinationPath, MagickFormat.Tga);
    }
}
