using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace GenHub.Features.Tools.MapManager.Services;

/// <summary>
/// Service for parsing TGA (Truevision Graphics Adapter) image files.
/// Supports uncompressed and RLE-compressed 24-bit and 32-bit TGA images.
/// </summary>
public class TgaParser(ILogger<TgaParser> logger)
{
    /// <summary>
    /// Loads a TGA file and converts it to an Avalonia Bitmap.
    /// </summary>
    /// <param name="filePath">Path to the TGA file.</param>
    /// <returns>A Bitmap if successful, null otherwise.</returns>
    public Bitmap? LoadTga(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                logger.LogWarning("TGA file not found: {Path}", filePath);
                return null;
            }

            var fileBytes = File.ReadAllBytes(filePath);
            return ParseTga(fileBytes, filePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load TGA file: {Path}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Decodes uncompressed TGA pixel data.
    /// </summary>
    private static byte[]? DecodeUncompressed(byte[] data, int offset, int width, int height, int bytesPerPixel)
    {
        int expectedSize = width * height * bytesPerPixel;
        if (data.Length < offset + expectedSize)
        {
            return null;
        }

        var result = new byte[expectedSize];
        Array.Copy(data, offset, result, 0, expectedSize);
        return result;
    }

    /// <summary>
    /// Decodes RLE-compressed TGA pixel data.
    /// </summary>
    private static byte[] DecodeRle(byte[] data, int offset, int width, int height, int bytesPerPixel)
    {
        int totalPixels = width * height;
        var result = new byte[totalPixels * bytesPerPixel];
        int resultIndex = 0;
        int dataIndex = offset;

        while (resultIndex < result.Length && dataIndex < data.Length)
        {
            int packetHeader = data[dataIndex++];
            int packetCount = (packetHeader & 0x7F) + 1;

            if ((packetHeader & 0x80) != 0)
            {
                // RLE packet - one pixel repeated
                if (dataIndex + bytesPerPixel > data.Length)
                    break;

                for (int i = 0; i < packetCount && resultIndex < result.Length; i++)
                {
                    for (int j = 0; j < bytesPerPixel; j++)
                    {
                        result[resultIndex++] = data[dataIndex + j];
                    }
                }

                dataIndex += bytesPerPixel;
            }
            else
            {
                // Raw packet - consecutive pixels
                int bytesToCopy = packetCount * bytesPerPixel;
                if (dataIndex + bytesToCopy > data.Length)
                    break;

                for (int i = 0; i < bytesToCopy && resultIndex < result.Length; i++)
                {
                    result[resultIndex++] = data[dataIndex++];
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Converts BGR(A) pixel data to RGBA format and handles vertical flip.
    /// </summary>
    private static byte[] ConvertToRgba(byte[] bgrData, int width, int height, int bytesPerPixel, bool topToBottom)
    {
        var rgba = new byte[width * height * 4];
        int srcIndex = 0;

        for (int y = 0; y < height; y++)
        {
            int destY = topToBottom ? y : (height - 1 - y);
            int destRowStart = destY * width * 4;

            for (int x = 0; x < width; x++)
            {
                int destIndex = destRowStart + (x * 4);

                // TGA stores as BGR(A)
                byte b = bgrData[srcIndex++];
                byte g = bgrData[srcIndex++];
                byte r = bgrData[srcIndex++];
                byte a = bytesPerPixel == 4 ? bgrData[srcIndex++] : (byte)255;

                // Convert to RGBA
                rgba[destIndex] = r;
                rgba[destIndex + 1] = g;
                rgba[destIndex + 2] = b;
                rgba[destIndex + 3] = a;
            }
        }

        return rgba;
    }

    /// <summary>
    /// Creates an Avalonia Bitmap from RGBA pixel data.
    /// </summary>
    private static Bitmap? CreateBitmap(byte[] rgbaData, int width, int height)
    {
        try
        {
            using var stream = new MemoryStream();

            // Write a simple BMP header for RGBA data
            WriteBmpHeader(stream, width, height);
            WriteBmpPixelData(stream, rgbaData, width, height);

            stream.Position = 0;
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Writes a BMP file header to the stream.
    /// </summary>
    private static void WriteBmpHeader(Stream stream, int width, int height)
    {
        using var writer = new BinaryWriter(stream);

        int rowStride = width * 4;
        int pixelDataSize = rowStride * height;
        int fileSize = 54 + pixelDataSize;

        // BMP File Header (14 bytes)
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write((short)0); // Reserved
        writer.Write((short)0); // Reserved
        writer.Write(54); // Pixel data offset

        // DIB Header (BITMAPINFOHEADER - 40 bytes)
        writer.Write(40); // Header size
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1); // Color planes
        writer.Write((short)32); // Bits per pixel
        writer.Write(0); // Compression (BI_RGB)
        writer.Write(pixelDataSize);
        writer.Write(2835); // Horizontal resolution (72 DPI)
        writer.Write(2835); // Vertical resolution (72 DPI)
        writer.Write(0); // Colors in palette
        writer.Write(0); // Important colors
    }

    /// <summary>
    /// Writes RGBA pixel data as BGRA to the BMP stream.
    /// </summary>
    private static void WriteBmpPixelData(Stream stream, byte[] rgbaData, int width, int height)
    {
        using var writer = new BinaryWriter(stream);

        // BMP stores rows bottom-to-top, BGRA
        for (int y = height - 1; y >= 0; y--)
        {
            int rowStart = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int i = rowStart + (x * 4);
                byte r = rgbaData[i];
                byte g = rgbaData[i + 1];
                byte b = rgbaData[i + 2];
                byte a = rgbaData[i + 3];

                // Write as BGRA
                writer.Write(b);
                writer.Write(g);
                writer.Write(r);
                writer.Write(a);
            }
        }
    }

    private Bitmap? ParseTga(byte[] data, string sourcePath)
    {
        if (data.Length < 18)
        {
            logger.LogWarning("TGA file too small: {Path}", sourcePath);
            return null;
        }

        // TGA Header
        int idLength = data[0];
        int colorMapType = data[1];
        int imageType = data[2];

        // Image specification
        int width = data[12] | (data[13] << 8);
        int height = data[14] | (data[15] << 8);
        int bitsPerPixel = data[16];
        int imageDescriptor = data[17];

        // Validate image type
        // Type 2 = Uncompressed True-color
        // Type 10 = RLE compressed True-color
        if (imageType != 2 && imageType != 10)
        {
            logger.LogWarning("Unsupported TGA image type {Type}: {Path}", imageType, sourcePath);
            return null;
        }

        // Validate color map type (should be 0 for true-color)
        if (colorMapType != 0)
        {
            logger.LogWarning("Color-mapped TGA not supported: {Path}", sourcePath);
            return null;
        }

        // Validate bits per pixel
        if (bitsPerPixel != 24 && bitsPerPixel != 32)
        {
            logger.LogWarning("Unsupported TGA bit depth {Depth}: {Path}", bitsPerPixel, sourcePath);
            return null;
        }

        int bytesPerPixel = bitsPerPixel / 8;
        int headerEnd = 18 + idLength;

        if (data.Length < headerEnd)
        {
            logger.LogWarning("TGA file header incomplete: {Path}", sourcePath);
            return null;
        }

        // Origin flag (bit 5 of image descriptor): 0 = bottom-left, 1 = top-left
        bool topToBottom = (imageDescriptor & 0x20) != 0;

        byte[]? pixelData;

        if (imageType == 2)
        {
            // Uncompressed
            pixelData = DecodeUncompressed(data, headerEnd, width, height, bytesPerPixel);
        }
        else
        {
            // RLE compressed
            pixelData = DecodeRle(data, headerEnd, width, height, bytesPerPixel);
        }

        if (pixelData == null)
        {
            logger.LogWarning("Failed to decode TGA pixel data: {Path}", sourcePath);
            return null;
        }

        // Convert BGR(A) to RGBA for Avalonia
        var rgbaData = ConvertToRgba(pixelData, width, height, bytesPerPixel, topToBottom);

        return CreateBitmap(rgbaData, width, height);
    }
}
