using System;
using System.IO;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace GenHub.Infrastructure.Imaging;

/// <summary>
/// Parser for TGA (Targa) image files commonly used in Command and Conquer maps.
/// </summary>
public class TgaImageParser(ILogger<TgaImageParser> logger)
{
    /// <summary>
    /// Loads a TGA file and returns it as a thumbnail bitmap.
    /// </summary>
    /// <param name="tgaPath">Path to the TGA file.</param>
    /// <param name="maxWidth">Maximum width for the thumbnail.</param>
    /// <param name="maxHeight">Maximum height for the thumbnail.</param>
    /// <returns>A bitmap thumbnail, or null if loading fails.</returns>
    public Bitmap? LoadTgaThumbnail(string tgaPath, int maxWidth = 128, int maxHeight = 128)
    {
        try
        {
            if (!File.Exists(tgaPath))
            {
                logger.LogWarning("TGA file not found: {Path}", tgaPath);
                return null;
            }

            var bitmap = ParseTgaFile(tgaPath);
            if (bitmap == null)
            {
                return null;
            }

            if (bitmap.PixelSize.Width <= maxWidth && bitmap.PixelSize.Height <= maxHeight)
            {
                return bitmap;
            }

            return ResizeImage(bitmap, maxWidth, maxHeight);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load TGA thumbnail: {Path}", tgaPath);
            return null;
        }
    }

    /// <summary>
    /// Decompresses RLE-encoded TGA data.
    /// </summary>
    private static byte[] DecompressRle(BinaryReader reader, int width, int height, int bytesPerPixel)
    {
        var pixelCount = width * height;
        var output = new byte[pixelCount * bytesPerPixel];
        var outputIndex = 0;

        while (outputIndex < output.Length)
        {
            var packetHeader = reader.ReadByte();
            var isRlePacket = (packetHeader & 0x80) != 0;
            var pixelCountInPacket = (packetHeader & 0x7F) + 1;

            if (isRlePacket)
            {
                var pixel = reader.ReadBytes(bytesPerPixel);
                for (int i = 0; i < pixelCountInPacket; i++)
                {
                    Array.Copy(pixel, 0, output, outputIndex, bytesPerPixel);
                    outputIndex += bytesPerPixel;
                }
            }
            else
            {
                var rawData = reader.ReadBytes(pixelCountInPacket * bytesPerPixel);
                Array.Copy(rawData, 0, output, outputIndex, rawData.Length);
                outputIndex += rawData.Length;
            }
        }

        return output;
    }

    /// <summary>
    /// Converts BGR/BGRA data to RGBA format.
    /// </summary>
    private static byte[] ConvertToRgba(byte[] sourceData, int width, int height, int sourceBytesPerPixel)
    {
        var pixelCount = width * height;
        var rgbaData = new byte[pixelCount * 4];

        for (int i = 0; i < pixelCount; i++)
        {
            var srcIndex = i * sourceBytesPerPixel;
            var dstIndex = i * 4;

            rgbaData[dstIndex] = sourceData[srcIndex + 2];
            rgbaData[dstIndex + 1] = sourceData[srcIndex + 1];
            rgbaData[dstIndex + 2] = sourceData[srcIndex];
            rgbaData[dstIndex + 3] = sourceBytesPerPixel == 4 ? sourceData[srcIndex + 3] : (byte)255;
        }

        return rgbaData;
    }

    /// <summary>
    /// Flips image data vertically.
    /// </summary>
    private static void FlipVertically(byte[] data, int width, int height)
    {
        var rowSize = width * 4;
        var tempRow = new byte[rowSize];

        for (int y = 0; y < height / 2; y++)
        {
            var topRowIndex = y * rowSize;
            var bottomRowIndex = (height - 1 - y) * rowSize;

            Array.Copy(data, topRowIndex, tempRow, 0, rowSize);
            Array.Copy(data, bottomRowIndex, data, topRowIndex, rowSize);
            Array.Copy(tempRow, 0, data, bottomRowIndex, rowSize);
        }
    }

    /// <summary>
    /// Creates an Avalonia bitmap from RGBA data.
    /// </summary>
    private static Bitmap CreateBitmapFromRgba(byte[] rgbaData, int width, int height)
    {
        using var memoryStream = new MemoryStream();
        using var writer = new BinaryWriter(memoryStream);

        writer.Write((byte)'B');
        writer.Write((byte)'M');

        var fileSize = 54 + rgbaData.Length;
        writer.Write(fileSize);
        writer.Write(0);
        writer.Write(54);

        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(rgbaData.Length);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                var index = ((y * width) + x) * 4;
                writer.Write(rgbaData[index + 2]);
                writer.Write(rgbaData[index + 1]);
                writer.Write(rgbaData[index]);
                writer.Write(rgbaData[index + 3]);
            }
        }

        memoryStream.Position = 0;
        return new Bitmap(memoryStream);
    }

    /// <summary>
    /// Parses a TGA file and returns it as a bitmap.
    /// </summary>
    /// <param name="path">Path to the TGA file.</param>
    /// <returns>A bitmap, or null if parsing fails.</returns>
    private Bitmap? ParseTgaFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            var idLength = reader.ReadByte();
            var colorMapType = reader.ReadByte();
            var imageType = reader.ReadByte();

            reader.ReadBytes(5);

            var xOrigin = reader.ReadUInt16();
            var yOrigin = reader.ReadUInt16();
            var width = reader.ReadUInt16();
            var height = reader.ReadUInt16();
            var bitsPerPixel = reader.ReadByte();
            var imageDescriptor = reader.ReadByte();

            if (idLength > 0)
            {
                reader.ReadBytes(idLength);
            }

            if (colorMapType != 0)
            {
                logger.LogWarning("Color-mapped TGA files are not supported: {Path}", path);
                return null;
            }

            if (imageType != 2 && imageType != 10)
            {
                logger.LogWarning("Unsupported TGA image type {Type}: {Path}", imageType, path);
                return null;
            }

            if (bitsPerPixel != 24 && bitsPerPixel != 32)
            {
                logger.LogWarning("Unsupported TGA bit depth {Depth}: {Path}", bitsPerPixel, path);
                return null;
            }

            var bytesPerPixel = bitsPerPixel / 8;
            var imageDataSize = width * height * bytesPerPixel;
            byte[] imageData;

            if (imageType == 2)
            {
                imageData = reader.ReadBytes(imageDataSize);
            }
            else
            {
                imageData = DecompressRle(reader, width, height, bytesPerPixel);
            }

            var rgbaData = ConvertToRgba(imageData, width, height, bytesPerPixel);

            var flipped = (imageDescriptor & 0x20) == 0;
            if (flipped)
            {
                FlipVertically(rgbaData, width, height);
            }

            return CreateBitmapFromRgba(rgbaData, width, height);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse TGA file: {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// Resizes an image to fit within the specified dimensions while maintaining aspect ratio.
    /// </summary>
    private Bitmap? ResizeImage(Bitmap source, int maxWidth, int maxHeight)
    {
        try
        {
            var sourceWidth = source.PixelSize.Width;
            var sourceHeight = source.PixelSize.Height;

            var ratioX = (double)maxWidth / sourceWidth;
            var ratioY = (double)maxHeight / sourceHeight;
            var ratio = Math.Min(ratioX, ratioY);

            var newWidth = (int)(sourceWidth * ratio);
            var newHeight = (int)(sourceHeight * ratio);

            return source.CreateScaledBitmap(new Avalonia.PixelSize(newWidth, newHeight));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resize image");
            return source;
        }
    }
}