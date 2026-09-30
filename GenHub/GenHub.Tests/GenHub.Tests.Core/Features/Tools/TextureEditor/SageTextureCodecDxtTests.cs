using GenHub.Core.Services.Tools.TextureEditor;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.TextureEditor;

/// <summary>
/// Unit tests for DXT3 and DXT5 decoding in <see cref="SageTextureCodec"/>.
/// </summary>
public sealed class SageTextureCodecDxtTests
{
    private readonly SageTextureCodec _codec = new(NullLogger<SageTextureCodec>.Instance);

    /// <summary>
    /// Verifies that a DXT3 block decodes explicit alpha plus interpolated colors.
    /// </summary>
    [Fact]
    public void Decode_Dxt3_DecodesExplicitAlpha()
    {
        byte[] alpha = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF];
        byte[] color = DxtColorBlock(0xF800, 0x001F, 0);
        byte[] data = BuildDds(4, 4, "DXT3", [.. alpha, .. color]);

        var result = _codec.Decode(data, ".dds", "test");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(4, result.Data.Width);
        Assert.Equal(4, result.Data.Height);
        byte[] pixels = result.Data.PixelData;
        Assert.Equal(4 * 4 * 4, pixels.Length);
        Assert.Equal([255, 0, 0, 0], pixels.Take(4).ToArray());
        int last = pixels.Length - 4;
        Assert.Equal(255, pixels[last + 3]);
    }

    /// <summary>
    /// Verifies that a DXT5 block decodes interpolated alpha plus colors.
    /// </summary>
    [Fact]
    public void Decode_Dxt5_DecodesInterpolatedAlpha()
    {
        byte[] alpha = [255, 0, 0, 0, 0, 0, 0, 0];
        byte[] color = DxtColorBlock(0xF800, 0x001F, 0);
        byte[] data = BuildDds(4, 4, "DXT5", [.. alpha, .. color]);

        var result = _codec.Decode(data, ".dds", "test");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        byte[] pixels = result.Data.PixelData;
        Assert.Equal([255, 0, 0, 255], pixels.Take(4).ToArray());
    }

    /// <summary>
    /// Verifies that DXT5 alpha interpolation spreads between endpoints.
    /// Alpha endpoints 240 and 16 with index 7 decode to (240 + 96) / 7 = 48.
    /// </summary>
    [Fact]
    public void Decode_Dxt5MaxAlphaIndex_InterpolatesEndpoints()
    {
        byte[] alpha = [240, 16, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
        byte[] color = DxtColorBlock(0xFFFF, 0x0000, 0);
        byte[] data = BuildDds(4, 4, "DXT5", [.. alpha, .. color]);

        var result = _codec.Decode(data, ".dds", "test");

        Assert.True(result.Success);
        byte[] pixels = result.Data!.PixelData;
        Assert.Equal(48, pixels[3]);
    }

    /// <summary>
    /// Verifies that truncated DXT payloads fail without throwing.
    /// </summary>
    [Fact]
    public void Decode_TruncatedDxt5_ReturnsFailure()
    {
        byte[] data = BuildDds(4, 4, "DXT5", [1, 2, 3]);

        var result = _codec.Decode(data, ".dds", "test");

        Assert.False(result.Success);
    }

    private static byte[] DxtColorBlock(ushort color0, ushort color1, uint codes)
    {
        byte[] block = new byte[8];
        BitConverter.GetBytes(color0).CopyTo(block, 0);
        BitConverter.GetBytes(color1).CopyTo(block, 2);
        BitConverter.GetBytes(codes).CopyTo(block, 4);
        return block;
    }

    private static byte[] BuildDds(int width, int height, string fourCc, byte[] payload)
    {
        byte[] header = new byte[128];
        header[0] = (byte)'D';
        header[1] = (byte)'D';
        header[2] = (byte)'S';
        header[3] = (byte)' ';
        BitConverter.GetBytes(124).CopyTo(header, 4);
        BitConverter.GetBytes(height).CopyTo(header, 12);
        BitConverter.GetBytes(width).CopyTo(header, 16);
        BitConverter.GetBytes(72).CopyTo(header, 76);
        BitConverter.GetBytes(0x4).CopyTo(header, 80);
        System.Text.Encoding.ASCII.GetBytes(fourCc).CopyTo(header, 84);
        return [.. header, .. payload];
    }
}
