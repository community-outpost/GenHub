using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace GenHub.Core.Services.Tools.TextureEditor;

/// <summary>
/// Decodes TGA and DDS textures and encodes uncompressed 32-bit TGA files.
/// Supports 24-bit and 32-bit uncompressed and RLE TGA images, uncompressed
/// 24-bit and 32-bit DDS images, and DXT1 through DXT5 compressed DDS images.
/// </summary>
public sealed class SageTextureCodec(ILogger<SageTextureCodec> logger) : ISageTextureCodec
{
    private sealed record DdsUncompressedRequest(byte[] Data, int Offset, int Width, int Height, int BytesPerPixel, int Pitch, uint RedMask, bool HasAlpha, string SourceName, long Started);

    private sealed record DdsImageHeader(byte[] Data, int HeaderOffset, int DataOffset, int Width, int Height, int PixelOffset, int PixelFlags, uint FourCc, int RgbBitCount, uint RedMask, string SourceName, long Started);

    private sealed record DdsBlockDecodeRequest(byte[] Data, int Offset, int Width, int Height, string SourceName, long Started, bool IsDxt5, bool Premultiplied, string Label);

    private sealed record DdsBlitTarget(byte[] Rgba, int Width, int Height);

    private const int TgaHeaderSize = 18;
    private const int TgaTypeUncompressed = 2;
    private const int TgaTypeRle = 10;
    private const int DdsHeaderSize = 124;
    private const int DdsMagicSize = 4;
    private const uint DdsFourCcDxt1 = 0x31545844;
    private const uint DdsFourCcDxt2 = 0x32545844;
    private const uint DdsFourCcDxt3 = 0x33545844;
    private const uint DdsFourCcDxt4 = 0x34545844;
    private const uint DdsFourCcDxt5 = 0x35545844;
    private const int DdsPixelFormatOffset = 72;
    private const int DdsHeaderFlagsOffset = 4;
    private const int DdsHeaderPitchFlag = 0x8;
    private const int DdsHeightOffset = 8;
    private const int DdsWidthOffset = 12;
    private const int DdsPitchOffset = 16;
    private const int DdsPixelFormatFlagsOffset = 4;
    private const int DdsPixelFormatFourCcOffset = 8;
    private const int DdsPixelFormatBitCountOffset = 12;
    private const int DdsPixelFormatAlphaMaskOffset = 28;
    private const int DdsPixelFormatRedMaskOffset = 16;
    private const int DdsPixelFormatFourCcFlag = 0x4;
    private const int DdsPixelFormatAlphaPixelsFlag = 0x1;
    private const uint DdsRedMaskRgbaOrder = 0x000000FF;
    private const int TgaDescriptorRightOrigin = 0x10;
    private const int TgaDescriptorTopOrigin = 0x20;

    /// <inheritdoc />
    public bool SupportsExtension(string extension) =>
        extension.Equals(".tga", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".dds", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public OperationResult<DecodedTexture> Decode(byte[] data, string extension, string? sourceName = null)
    {
        var started = Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        if (extension.Equals(".tga", StringComparison.OrdinalIgnoreCase))
        {
            return DecodeTga(data, sourceName ?? "texture", started);
        }

        if (extension.Equals(".dds", StringComparison.OrdinalIgnoreCase))
        {
            return DecodeDds(data, sourceName ?? "texture", started);
        }

        return OperationResult<DecodedTexture>.CreateFailure($"Unsupported texture format '{extension}'.", Stopwatch.GetElapsedTime(started));
    }

    /// <inheritdoc />
    public async Task<OperationResult<DecodedTexture>> DecodeFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return OperationResult<DecodedTexture>.CreateFailure($"Texture file not found: {path}", Stopwatch.GetElapsedTime(started));
        }

        try
        {
            var data = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return Decode(data, Path.GetExtension(path), path);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to read texture file: {Path}", path);
            return OperationResult<DecodedTexture>.CreateFailure($"Failed to read texture file: {path}", Stopwatch.GetElapsedTime(started));
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied reading texture file: {Path}", path);
            return OperationResult<DecodedTexture>.CreateFailure($"Access denied reading texture file: {path}", Stopwatch.GetElapsedTime(started));
        }
    }

    /// <inheritdoc />
    public OperationResult<byte[]> EncodeTga(DecodedTexture texture)
    {
        var started = Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(texture);

        long total = (long)texture.Width * texture.Height * 4;
        if (texture.Width <= 0 || texture.Height <= 0 ||
            texture.Width > ushort.MaxValue || texture.Height > ushort.MaxValue)
        {
            return OperationResult<byte[]>.CreateFailure("Texture dimensions must be positive and fit in 16-bit TGA headers.", Stopwatch.GetElapsedTime(started));
        }

        if (texture.PixelData.Length != total || total > int.MaxValue - TgaHeaderSize)
        {
            return OperationResult<byte[]>.CreateFailure("Pixel data length does not match texture dimensions.", Stopwatch.GetElapsedTime(started));
        }

        var output = new byte[TgaHeaderSize + texture.PixelData.Length];
        output[2] = TgaTypeUncompressed;
        output[12] = (byte)(texture.Width & 0xFF);
        output[13] = (byte)((texture.Width >> 8) & 0xFF);
        output[14] = (byte)(texture.Height & 0xFF);
        output[15] = (byte)((texture.Height >> 8) & 0xFF);
        output[16] = 32;
        output[17] = 8;

        int stride = texture.Width * 4;
        for (int y = 0; y < texture.Height; y++)
        {
            int srcRow = y * stride;
            int dstRow = TgaHeaderSize + ((texture.Height - 1 - y) * stride);
            for (int x = 0; x < stride; x += 4)
            {
                output[dstRow + x] = texture.PixelData[srcRow + x + 2];
                output[dstRow + x + 1] = texture.PixelData[srcRow + x + 1];
                output[dstRow + x + 2] = texture.PixelData[srcRow + x];
                output[dstRow + x + 3] = texture.PixelData[srcRow + x + 3];
            }
        }

        return OperationResult<byte[]>.CreateSuccess(output, Stopwatch.GetElapsedTime(started));
    }

    private static int ReadInt32(byte[] data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);

    private static ushort ReadUInt16(byte[] data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static byte[]? DecodeTgaPixels(byte[] data, int offset, int width, int height, int bytesPerPixel, int imageType)
    {
        return imageType == TgaTypeUncompressed
            ? DecodeTgaUncompressed(data, offset, width, height, bytesPerPixel)
            : DecodeTgaRle(data, offset, width, height, bytesPerPixel);
    }

    private static byte[]? DecodeTgaUncompressed(byte[] data, int offset, int width, int height, int bytesPerPixel)
    {
        long total = (long)width * height * bytesPerPixel;
        if (total <= 0 || total > int.MaxValue || offset < 0 || offset > data.Length || data.Length - offset < total)
        {
            return null;
        }

        int expected = (int)total;
        var result = new byte[expected];
        Array.Copy(data, offset, result, 0, expected);
        return result;
    }

    private static byte[]? DecodeTgaRle(byte[] data, int offset, int width, int height, int bytesPerPixel)
    {
        long total = (long)width * height * bytesPerPixel;
        if (total <= 0 || total > int.MaxValue || offset < 0 || offset > data.Length)
        {
            return null;
        }

        var result = new byte[(int)total];
        int resultIndex = 0;
        int dataIndex = offset;

        while (resultIndex < result.Length)
        {
            if (dataIndex >= data.Length)
            {
                return null;
            }

            int header = data[dataIndex++];
            int count = (header & 0x7F) + 1;
            if ((header & 0x80) != 0)
            {
                if (!CopyRleRun(data, ref dataIndex, result, ref resultIndex, count, bytesPerPixel))
                {
                    return null;
                }
            }
            else if (!CopyRawRun(data, ref dataIndex, result, ref resultIndex, count, bytesPerPixel))
            {
                return null;
            }
        }

        return result;
    }

    private static bool CopyRleRun(byte[] data, ref int dataIndex, byte[] result, ref int resultIndex, int count, int bytesPerPixel)
    {
        if (dataIndex + bytesPerPixel > data.Length)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            if (resultIndex + bytesPerPixel > result.Length)
            {
                return false;
            }

            for (int j = 0; j < bytesPerPixel; j++)
            {
                result[resultIndex++] = data[dataIndex + j];
            }
        }

        dataIndex += bytesPerPixel;
        return true;
    }

    private static bool CopyRawRun(byte[] data, ref int dataIndex, byte[] result, ref int resultIndex, int count, int bytesPerPixel)
    {
        int bytes = count * bytesPerPixel;
        if (dataIndex + bytes > data.Length || resultIndex + bytes > result.Length)
        {
            return false;
        }

        Array.Copy(data, dataIndex, result, resultIndex, bytes);
        dataIndex += bytes;
        resultIndex += bytes;
        return true;
    }

    private static byte[]? ConvertTgaToRgba(byte[] pixels, int width, int height, int bytesPerPixel, bool bottomToTop, bool rightToLeft)
    {
        long total = (long)width * height * 4;
        if (total <= 0 || total > int.MaxValue || pixels.Length < (long)width * height * bytesPerPixel)
        {
            return null;
        }

        var rgba = new byte[(int)total];
        int srcIndex = 0;

        for (int y = 0; y < height; y++)
        {
            int destY = bottomToTop ? height - 1 - y : y;
            int destRow = destY * width * 4;
            for (int x = 0; x < width; x++)
            {
                int destX = rightToLeft ? width - 1 - x : x;
                int dest = destRow + (destX * 4);
                rgba[dest] = pixels[srcIndex + 2];
                rgba[dest + 1] = pixels[srcIndex + 1];
                rgba[dest + 2] = pixels[srcIndex];
                rgba[dest + 3] = bytesPerPixel == 4 ? pixels[srcIndex + 3] : (byte)255;
                srcIndex += bytesPerPixel;
            }
        }

        return rgba;
    }

    private static void DecodeDxt1Block(byte[] data, int offset, DdsBlitTarget target, int blockX, int blockY)
    {
        ushort color0 = ReadUInt16(data, offset);
        ushort color1 = ReadUInt16(data, offset + 2);
        uint codes = (uint)ReadInt32(data, offset + 4);

        Span<byte> palette = stackalloc byte[16];
        ExpandRgb565(color0, palette.Slice(0, 4));
        ExpandRgb565(color1, palette.Slice(4, 4));
        MixDxt1Colors(palette, color0 > color1);

        // DXT1 carries alpha in the palette entries (index 3 is transparent in
        // three-color mode), so expand it per pixel for the shared blit.
        Span<byte> alphas = stackalloc byte[16];
        for (int i = 0; i < 16; i++)
        {
            alphas[i] = palette[(((int)((codes >> (i * 2)) & 0x3)) * 4) + 3];
        }

        BlitDxtBlock(codes, palette, alphas, target, blockX, blockY);
    }

    private static void ExpandRgb565(ushort color, Span<byte> output)
    {
        output[0] = (byte)(((color >> 11) & 0x1F) * 255 / 31);
        output[1] = (byte)(((color >> 5) & 0x3F) * 255 / 63);
        output[2] = (byte)((color & 0x1F) * 255 / 31);
        output[3] = 255;
    }

    private static void MixDxt1Colors(Span<byte> palette, bool fourColor)
    {
        if (fourColor)
        {
            for (int i = 0; i < 3; i++)
            {
                palette[8 + i] = (byte)(((palette[i] * 2) + palette[4 + i]) / 3);
                palette[12 + i] = (byte)((palette[i] + (palette[4 + i] * 2)) / 3);
            }

            palette[11] = 255;
            palette[15] = 255;
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                palette[8 + i] = (byte)((palette[i] + palette[4 + i]) / 2);
                palette[12 + i] = 0;
            }

            palette[11] = 255;
            palette[15] = 0;
        }
    }

    private static OperationResult<DecodedTexture> DecodeDdsUncompressed(DdsUncompressedRequest request)
    {
        long rowBytes = (long)request.Width * request.BytesPerPixel;
        long stride = request.Pitch >= rowBytes ? request.Pitch : rowBytes;
        long expected = (stride * (request.Height - 1)) + rowBytes;
        long pixelBytes = (long)request.Width * request.Height * 4;
        if (expected > int.MaxValue || pixelBytes > int.MaxValue || request.Data.Length - request.Offset < expected)
        {
            return OperationResult<DecodedTexture>.CreateFailure($"Truncated DDS pixel data: {request.SourceName}", Stopwatch.GetElapsedTime(request.Started));
        }

        var rgba = new byte[(int)pixelBytes];
        bool rgbaOrder = request.RedMask == DdsRedMaskRgbaOrder;
        int destIndex = 0;
        for (int y = 0; y < request.Height; y++)
        {
            int srcIndex = request.Offset + (int)(y * stride);
            for (int x = 0; x < request.Width; x++)
            {
                WriteUncompressedPixel(request.Data, srcIndex, request.BytesPerPixel, rgbaOrder, request.HasAlpha, rgba, destIndex);
                srcIndex += request.BytesPerPixel;
                destIndex += 4;
            }
        }

        return OperationResult<DecodedTexture>.CreateSuccess(new DecodedTexture(request.Width, request.Height, rgba), Stopwatch.GetElapsedTime(request.Started));
    }

    private static void WriteUncompressedPixel(byte[] data, int srcIndex, int bytesPerPixel, bool rgbaOrder, bool hasAlpha, byte[] rgba, int destIndex)
    {
        if (rgbaOrder)
        {
            rgba[destIndex] = data[srcIndex];
            rgba[destIndex + 1] = data[srcIndex + 1];
            rgba[destIndex + 2] = data[srcIndex + 2];
        }
        else
        {
            rgba[destIndex] = data[srcIndex + 2];
            rgba[destIndex + 1] = data[srcIndex + 1];
            rgba[destIndex + 2] = data[srcIndex];
        }

        rgba[destIndex + 3] = bytesPerPixel == 4 && hasAlpha ? data[srcIndex + 3] : (byte)255;
    }

    private static OperationResult<DecodedTexture> DecodeDdsDxt1(byte[] data, int offset, int width, int height, string sourceName, long started)
    {
        long blocksX = ((long)width + 3) / 4;
        long blocksY = ((long)height + 3) / 4;
        long expected = blocksX * blocksY * 8;
        long pixelBytes = (long)width * height * 4;
        if (pixelBytes > int.MaxValue || expected > int.MaxValue || data.Length - offset < expected)
        {
            return OperationResult<DecodedTexture>.CreateFailure($"Truncated DDS DXT1 data: {sourceName}", Stopwatch.GetElapsedTime(started));
        }

        var rgba = new byte[(int)pixelBytes];
        var target = new DdsBlitTarget(rgba, width, height);
        int blockIndex = 0;
        for (long blockY = 0; blockY < blocksY; blockY++)
        {
            for (long blockX = 0; blockX < blocksX; blockX++)
            {
                DecodeDxt1Block(data, offset + (blockIndex * 8), target, (int)blockX, (int)blockY);
                blockIndex++;
            }
        }

        return OperationResult<DecodedTexture>.CreateSuccess(new DecodedTexture(width, height, rgba), Stopwatch.GetElapsedTime(started));
    }

    private static uint DecodeDxtColorPalette(byte[] data, int offset, Span<byte> palette)
    {
        ushort color0 = ReadUInt16(data, offset);
        ushort color1 = ReadUInt16(data, offset + 2);
        ExpandRgb565(color0, palette.Slice(0, 4));
        ExpandRgb565(color1, palette.Slice(4, 4));
        MixDxt1Colors(palette, true);
        return (uint)ReadInt32(data, offset + 4);
    }

    private static void DecodeDxt3Alpha(byte[] data, int offset, Span<byte> alphas)
    {
        for (int i = 0; i < 16; i++)
        {
            int nibble = (data[offset + (i / 2)] >> ((i % 2) * 4)) & 0xF;
            alphas[i] = (byte)(nibble * 17);
        }
    }

    private static void DecodeDxt5Alpha(byte[] data, int offset, Span<byte> alphas)
    {
        byte alpha0 = data[offset];
        byte alpha1 = data[offset + 1];
        ulong bits = 0;
        for (int i = 0; i < 6; i++)
        {
            bits |= (ulong)data[offset + 2 + i] << (i * 8);
        }

        Span<byte> table = stackalloc byte[8];
        BuildDxt5AlphaTable(alpha0, alpha1, table);
        for (int i = 0; i < 16; i++)
        {
            alphas[i] = table[(int)((bits >> (i * 3)) & 0x7)];
        }
    }

    private static void BuildDxt5AlphaTable(byte alpha0, byte alpha1, Span<byte> table)
    {
        table[0] = alpha0;
        table[1] = alpha1;
        if (alpha0 > alpha1)
        {
            table[2] = (byte)(((6 * alpha0) + alpha1) / 7);
            table[3] = (byte)(((5 * alpha0) + (2 * alpha1)) / 7);
            table[4] = (byte)(((4 * alpha0) + (3 * alpha1)) / 7);
            table[5] = (byte)(((3 * alpha0) + (4 * alpha1)) / 7);
            table[6] = (byte)(((2 * alpha0) + (5 * alpha1)) / 7);
            table[7] = (byte)((alpha0 + (6 * alpha1)) / 7);
        }
        else
        {
            table[2] = (byte)(((4 * alpha0) + alpha1) / 5);
            table[3] = (byte)(((3 * alpha0) + (2 * alpha1)) / 5);
            table[4] = (byte)(((2 * alpha0) + (3 * alpha1)) / 5);
            table[5] = (byte)((alpha0 + (4 * alpha1)) / 5);
            table[6] = 0;
            table[7] = 255;
        }
    }

    private static void BlitDxtBlock(uint codes, ReadOnlySpan<byte> palette, ReadOnlySpan<byte> alphas, DdsBlitTarget target, int blockX, int blockY)
    {
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int px = (blockX * 4) + x;
                int py = (blockY * 4) + y;
                if (px >= target.Width || py >= target.Height)
                {
                    continue;
                }

                int paletteIndex = (int)((codes >> (((y * 4) + x) * 2)) & 0x3) * 4;
                int dest = ((py * target.Width) + px) * 4;
                target.Rgba[dest] = palette[paletteIndex];
                target.Rgba[dest + 1] = palette[paletteIndex + 1];
                target.Rgba[dest + 2] = palette[paletteIndex + 2];
                target.Rgba[dest + 3] = alphas[(y * 4) + x];
            }
        }
    }

    private static OperationResult<DecodedTexture> DecodeDdsDxt35(DdsBlockDecodeRequest request)
    {
        long blocksX = ((long)request.Width + 3) / 4;
        long blocksY = ((long)request.Height + 3) / 4;
        long expected = blocksX * blocksY * 16;
        long pixelBytes = (long)request.Width * request.Height * 4;
        if (pixelBytes > int.MaxValue || expected > int.MaxValue || request.Data.Length - request.Offset < expected)
        {
            return OperationResult<DecodedTexture>.CreateFailure($"Truncated DDS {request.Label} data: {request.SourceName}", Stopwatch.GetElapsedTime(request.Started));
        }

        var rgba = new byte[(int)pixelBytes];
        var target = new DdsBlitTarget(rgba, request.Width, request.Height);
        Span<byte> palette = stackalloc byte[16];
        Span<byte> alphas = stackalloc byte[16];
        int blockIndex = 0;
        for (long blockY = 0; blockY < blocksY; blockY++)
        {
            for (long blockX = 0; blockX < blocksX; blockX++)
            {
                int blockOffset = request.Offset + (blockIndex * 16);
                if (request.IsDxt5)
                {
                    DecodeDxt5Alpha(request.Data, blockOffset, alphas);
                }
                else
                {
                    DecodeDxt3Alpha(request.Data, blockOffset, alphas);
                }

                uint codes = DecodeDxtColorPalette(request.Data, blockOffset + 8, palette);
                BlitDxtBlock(codes, palette, alphas, target, (int)blockX, (int)blockY);
                blockIndex++;
            }
        }

        if (request.Premultiplied)
        {
            UnPremultiplyAlpha(rgba);
        }

        return OperationResult<DecodedTexture>.CreateSuccess(new DecodedTexture(request.Width, request.Height, rgba), Stopwatch.GetElapsedTime(request.Started));
    }

    private static void UnPremultiplyAlpha(byte[] rgba)
    {
        // DXT2/DXT4 store premultiplied RGB; restore straight alpha the way
        // D3D sampling does so decoded colors match the authored values.
        for (int i = 0; i < rgba.Length; i += 4)
        {
            int alpha = rgba[i + 3];
            if (alpha == 0)
            {
                rgba[i] = 0;
                rgba[i + 1] = 0;
                rgba[i + 2] = 0;
            }
            else if (alpha < 255)
            {
                rgba[i] = (byte)Math.Min(255, (rgba[i] * 255) / alpha);
                rgba[i + 1] = (byte)Math.Min(255, (rgba[i + 1] * 255) / alpha);
                rgba[i + 2] = (byte)Math.Min(255, (rgba[i + 2] * 255) / alpha);
            }
        }
    }

    private OperationResult<DecodedTexture> DecodeTga(byte[] data, string sourceName, long started)
    {
        if (data.Length < TgaHeaderSize)
        {
            return OperationResult<DecodedTexture>.CreateFailure($"TGA file too small: {sourceName}", Stopwatch.GetElapsedTime(started));
        }

        int idLength = data[0];
        int colorMapType = data[1];
        int imageType = data[2];
        int width = ReadUInt16(data, 12);
        int height = ReadUInt16(data, 14);
        int bitsPerPixel = data[16];
        int descriptor = data[17];

        if (imageType != TgaTypeUncompressed && imageType != TgaTypeRle)
        {
            logger.LogWarning("Unsupported TGA image type {Type}: {Source}", imageType, sourceName);
            return OperationResult<DecodedTexture>.CreateFailure($"Unsupported TGA image type {imageType}: {sourceName}", Stopwatch.GetElapsedTime(started));
        }

        if (colorMapType != 0)
        {
            return OperationResult<DecodedTexture>.CreateFailure($"Color-mapped TGA not supported: {sourceName}", Stopwatch.GetElapsedTime(started));
        }

        if (bitsPerPixel != 24 && bitsPerPixel != 32)
        {
            return OperationResult<DecodedTexture>.CreateFailure($"Unsupported TGA bit depth {bitsPerPixel}: {sourceName}", Stopwatch.GetElapsedTime(started));
        }

        if (width <= 0 || height <= 0)
        {
            return OperationResult<DecodedTexture>.CreateFailure($"Invalid TGA dimensions: {sourceName}", Stopwatch.GetElapsedTime(started));
        }

        int bytesPerPixel = bitsPerPixel / 8;
        var pixels = DecodeTgaPixels(data, TgaHeaderSize + idLength, width, height, bytesPerPixel, imageType);
        if (pixels is null)
        {
            logger.LogWarning("Failed to decode TGA pixel data: {Source}", sourceName);
            return OperationResult<DecodedTexture>.CreateFailure($"Failed to decode TGA pixel data: {sourceName}", Stopwatch.GetElapsedTime(started));
        }

        bool bottomToTop = (descriptor & TgaDescriptorTopOrigin) == 0;
        bool rightToLeft = (descriptor & TgaDescriptorRightOrigin) != 0;
        var rgba = ConvertTgaToRgba(pixels, width, height, bytesPerPixel, bottomToTop, rightToLeft);
        if (rgba is null)
        {
            logger.LogWarning("TGA pixel data exceeds supported size: {Source}", sourceName);
            return OperationResult<DecodedTexture>.CreateFailure($"TGA pixel data exceeds supported size: {sourceName}", Stopwatch.GetElapsedTime(started));
        }

        return OperationResult<DecodedTexture>.CreateSuccess(new DecodedTexture(width, height, rgba), Stopwatch.GetElapsedTime(started));
    }

    private OperationResult<DecodedTexture> DecodeDds(byte[] data, string sourceName, long started)
    {
        if (data.Length < DdsMagicSize + DdsHeaderSize)
        {
            return OperationResult<DecodedTexture>.CreateFailure($"DDS file too small: {sourceName}", Stopwatch.GetElapsedTime(started));
        }

        if (data[0] != 'D' || data[1] != 'D' || data[2] != 'S' || data[3] != ' ')
        {
            return OperationResult<DecodedTexture>.CreateFailure($"Invalid DDS magic: {sourceName}", Stopwatch.GetElapsedTime(started));
        }

        int headerOffset = DdsMagicSize;
        if (ReadInt32(data, headerOffset) != DdsHeaderSize)
        {
            return OperationResult<DecodedTexture>.CreateFailure($"Invalid DDS header size: {sourceName}", Stopwatch.GetElapsedTime(started));
        }

        int height = ReadInt32(data, headerOffset + DdsHeightOffset);
        int width = ReadInt32(data, headerOffset + DdsWidthOffset);
        if (width <= 0 || height <= 0)
        {
            return OperationResult<DecodedTexture>.CreateFailure($"Invalid DDS dimensions: {sourceName}", Stopwatch.GetElapsedTime(started));
        }

        int pixelOffset = headerOffset + DdsPixelFormatOffset;
        int pixelFlags = ReadInt32(data, pixelOffset + DdsPixelFormatFlagsOffset);
        uint fourCc = (uint)ReadInt32(data, pixelOffset + DdsPixelFormatFourCcOffset);
        int rgbBitCount = ReadInt32(data, pixelOffset + DdsPixelFormatBitCountOffset);
        uint redMask = (uint)ReadInt32(data, pixelOffset + DdsPixelFormatRedMaskOffset);
        int dataOffset = DdsMagicSize + DdsHeaderSize;

        return DecodeDdsPixels(new DdsImageHeader(data, headerOffset, dataOffset, width, height, pixelOffset, pixelFlags, fourCc, rgbBitCount, redMask, sourceName, started));
    }

    private OperationResult<DecodedTexture> DecodeDdsPixels(DdsImageHeader header)
    {
        if ((header.PixelFlags & DdsPixelFormatFourCcFlag) != 0)
        {
            return DecodeDdsFourCc(header);
        }

        if (header.RgbBitCount == 32 || header.RgbBitCount == 24)
        {
            int headerFlags = ReadInt32(header.Data, header.HeaderOffset + DdsHeaderFlagsOffset);
            int pitch = (headerFlags & DdsHeaderPitchFlag) != 0
                ? ReadInt32(header.Data, header.HeaderOffset + DdsPitchOffset)
                : 0;
            uint alphaMask = (uint)ReadInt32(header.Data, header.PixelOffset + DdsPixelFormatAlphaMaskOffset);
            bool hasAlpha = (header.PixelFlags & DdsPixelFormatAlphaPixelsFlag) != 0 || alphaMask != 0;
            return DecodeDdsUncompressed(new DdsUncompressedRequest(header.Data, header.DataOffset, header.Width, header.Height, header.RgbBitCount / 8, pitch, header.RedMask, hasAlpha, header.SourceName, header.Started));
        }

        logger.LogWarning("Unsupported DDS pixel format in {Source}", header.SourceName);
        return OperationResult<DecodedTexture>.CreateFailure($"Unsupported DDS pixel format: {header.SourceName}", Stopwatch.GetElapsedTime(header.Started));
    }

    private OperationResult<DecodedTexture> DecodeDdsFourCc(DdsImageHeader header)
    {
        if (header.FourCc == DdsFourCcDxt1)
        {
            return DecodeDdsDxt1(header.Data, header.DataOffset, header.Width, header.Height, header.SourceName, header.Started);
        }

        if (header.FourCc == DdsFourCcDxt2 || header.FourCc == DdsFourCcDxt3)
        {
            bool premultiplied = header.FourCc == DdsFourCcDxt2;
            return DecodeDdsDxt35(new DdsBlockDecodeRequest(header.Data, header.DataOffset, header.Width, header.Height, header.SourceName, header.Started, false, premultiplied, premultiplied ? "DXT2" : "DXT3"));
        }

        if (header.FourCc == DdsFourCcDxt4 || header.FourCc == DdsFourCcDxt5)
        {
            bool premultiplied = header.FourCc == DdsFourCcDxt4;
            return DecodeDdsDxt35(new DdsBlockDecodeRequest(header.Data, header.DataOffset, header.Width, header.Height, header.SourceName, header.Started, true, premultiplied, premultiplied ? "DXT4" : "DXT5"));
        }

        logger.LogWarning("Unsupported DDS pixel format in {Source}", header.SourceName);
        return OperationResult<DecodedTexture>.CreateFailure($"Unsupported DDS pixel format: {header.SourceName}", Stopwatch.GetElapsedTime(header.Started));
    }
}
