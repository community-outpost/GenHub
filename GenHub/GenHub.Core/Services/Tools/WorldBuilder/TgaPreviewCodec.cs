using GenHub.Core.Constants;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Encodes and decodes uncompressed 32-bit TGA preview images
/// (top-left origin, BGRA order).
/// </summary>
public static class TgaPreviewCodec
{
    /// <summary>
    /// Encodes preview pixels to TGA bytes.
    /// </summary>
    /// <param name="preview">The preview pixels.</param>
    /// <returns>The TGA bytes.</returns>
    public static byte[] Encode(MapPreviewData preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        using var output = new MemoryStream(18 + (preview.Pixels.Count * 4));
        output.WriteByte(0);
        output.WriteByte(0);
        output.WriteByte(2);
        output.Write(new byte[5], 0, 5);
        output.Write(new byte[4], 0, 4);
        output.Write(BitConverter.GetBytes((ushort)preview.Width), 0, 2);
        output.Write(BitConverter.GetBytes((ushort)preview.Height), 0, 2);
        output.WriteByte(32);
        output.WriteByte(0x20);
        foreach (var pixel in preview.Pixels)
        {
            output.WriteByte((byte)(pixel & 0xFF));
            output.WriteByte((byte)((pixel >> 8) & 0xFF));
            output.WriteByte((byte)((pixel >> 16) & 0xFF));
            output.WriteByte((byte)((pixel >> 24) & 0xFF));
        }

        return output.ToArray();
    }

    /// <summary>
    /// Decodes TGA bytes to preview pixels.
    /// </summary>
    /// <param name="data">The TGA bytes.</param>
    /// <returns>The preview pixels.</returns>
    public static OperationResult<MapPreviewData> Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 18)
        {
            return OperationResult<MapPreviewData>.CreateFailure("Truncated TGA header.");
        }

        if (data[2] != 2 || data[16] != 32)
        {
            return OperationResult<MapPreviewData>.CreateFailure("Only uncompressed 32-bit TGA previews are supported.");
        }

        try
        {
            var width = BitConverter.ToUInt16(data, 12);
            var height = BitConverter.ToUInt16(data, 14);
            if (width == 0 || height == 0 || width > WorldBuilderConstants.Limits.MaxPreviewDimension || height > WorldBuilderConstants.Limits.MaxPreviewDimension)
            {
                return OperationResult<MapPreviewData>.CreateFailure($"Invalid TGA preview dimensions ({width}x{height}).");
            }

            var topLeft = (data[17] & 0x20) != 0;
            var offset = 18 + data[0];
            var totalPixels = (long)width * height;
            var expectedBytes = totalPixels * 4;
            if (offset < 0 || (long)data.Length < offset + expectedBytes)
            {
                return OperationResult<MapPreviewData>.CreateFailure("Truncated TGA pixels.");
            }

            var preview = new MapPreviewData
            {
                Width = width,
                Height = height,
                Pixels = new int[width * height],
            };
            for (var y = 0; y < height; y++)
            {
                var row = topLeft ? y : height - 1 - y;
                for (var x = 0; x < width; x++)
                {
                    var pos = offset + (((row * width) + x) * 4);
                    preview.Pixels[(y * width) + x] =
                        (data[pos + 3] << 24) | (data[pos + 2] << 16) | (data[pos + 1] << 8) | data[pos];
                }
            }

            return OperationResult<MapPreviewData>.CreateSuccess(preview);
        }
        catch (ArgumentException ex)
        {
            return OperationResult<MapPreviewData>.CreateFailure($"Malformed TGA preview: {ex.Message}");
        }
        catch (IndexOutOfRangeException ex)
        {
            return OperationResult<MapPreviewData>.CreateFailure($"Malformed TGA preview: {ex.Message}");
        }
        catch (OverflowException ex)
        {
            return OperationResult<MapPreviewData>.CreateFailure($"Malformed TGA preview: {ex.Message}");
        }
    }
}
