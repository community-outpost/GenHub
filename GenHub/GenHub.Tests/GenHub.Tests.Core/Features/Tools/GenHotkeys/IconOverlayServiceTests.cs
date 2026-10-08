using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Services.Tools.TextureEditor;
using GenHub.Features.Tools.GenHotkeys.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.GenHotkeys;

/// <summary>
/// Unit tests for <see cref="IconOverlayService"/>.
/// </summary>
public class IconOverlayServiceTests
{
    /// <summary>
    /// Verifies that GenerateOverlayTgaAsync stamps the badge and produces a 32-bit TGA image.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task GenerateOverlayTgaAsync_StampsBadgeAndExports32BitTgaAsync()
    {
        var service = new IconOverlayService(NullLogger<IconOverlayService>.Instance);

        // Create a 60x48 test image
        using var testImage = new Image<Rgba32>(60, 48);
        using var ms = new MemoryStream();
        await testImage.SaveAsPngAsync(ms);
        var inputBytes = ms.ToArray();

        var tgaBytes = await service.GenerateOverlayTgaAsync(inputBytes, 'D', OverlayCorner.TopLeft);

        Assert.NotNull(tgaBytes);
        Assert.True(tgaBytes.Length > 18); // TGA header is 18 bytes

        // Inspect TGA header
        // Byte 2: Image type (2 = uncompressed true-color)
        Assert.Equal(2, tgaBytes[2]);

        // Bytes 12-13: Width (little-endian 60)
        var width = tgaBytes[12] | (tgaBytes[13] << 8);
        Assert.Equal(60, width);

        // Bytes 14-15: Height (little-endian 48)
        var height = tgaBytes[14] | (tgaBytes[15] << 8);
        Assert.Equal(48, height);

        // Byte 16: Pixel depth (32 bpp with alpha channel)
        Assert.Equal(32, tgaBytes[16]);
    }

    /// <summary>
    /// Verifies that high-fidelity glyphs (including distinct letters like G, B, R and digits like 8, 0)
    /// stamp cleanly without throwing across various overlay corners.
    /// </summary>
    /// <param name="key">The hotkey character to test.</param>
    /// <param name="corner">The overlay corner to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData('G', OverlayCorner.TopLeft)]
    [InlineData('B', OverlayCorner.TopRight)]
    [InlineData('R', OverlayCorner.BottomLeft)]
    [InlineData('8', OverlayCorner.BottomRight)]
    [InlineData('0', OverlayCorner.TopLeft)]
    public async Task GenerateOverlayTgaAsync_WithVariousGlyphsAndCorners_ProducesValidTgaAsync(char key, OverlayCorner corner)
    {
        var service = new IconOverlayService(NullLogger<IconOverlayService>.Instance);

        using var testImage = new Image<Rgba32>(60, 48);
        using var ms = new MemoryStream();
        await testImage.SaveAsPngAsync(ms);
        var inputBytes = ms.ToArray();

        var tgaBytes = await service.GenerateOverlayTgaAsync(inputBytes, key, corner);

        Assert.NotNull(tgaBytes);
        Assert.True(tgaBytes.Length > 18);
        Assert.Equal(32, tgaBytes[16]); // 32-bit depth
    }

    /// <summary>
    /// Verifies that ConvertToTgaAsync converts source images to 60x48 true-color 32-bit TGAs without badge stamping.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ConvertToTgaAsync_WithoutBadge_OutputsClean60x48TgaAsync()
    {
        var service = new IconOverlayService(NullLogger<IconOverlayService>.Instance);

        using var testImage = new Image<Rgba32>(60, 48);
        using var ms = new MemoryStream();
        await testImage.SaveAsPngAsync(ms);
        var inputBytes = ms.ToArray();

        var tgaBytes = await service.ConvertToTgaAsync(inputBytes);

        Assert.NotNull(tgaBytes);
        Assert.True(tgaBytes.Length > 18);
        Assert.Equal(2, tgaBytes[2]); // uncompressed true-color
        var width = tgaBytes[12] | (tgaBytes[13] << 8);
        var height = tgaBytes[14] | (tgaBytes[15] << 8);
        Assert.Equal(60, width);
        Assert.Equal(48, height);
        Assert.Equal(32, tgaBytes[16]); // 32 bpp
    }

    /// <summary>
    /// Verifies that GenerateOverlayTgaAsync safely decodes native TGA image bytes via SageTextureCodec fallback.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GenerateOverlayTgaAsync_WithTgaInput_DecodesViaSageTextureCodecAndOutputsValidOverlayAsync()
    {
        var codec = new SageTextureCodec(NullLogger<SageTextureCodec>.Instance);
        var service = new IconOverlayService(NullLogger<IconOverlayService>.Instance, codec);

        var rawPixels = new byte[60 * 48 * 4];
        for (int i = 0; i < rawPixels.Length; i += 4)
        {
            rawPixels[i] = 120;
            rawPixels[i + 1] = 150;
            rawPixels[i + 2] = 200;
            rawPixels[i + 3] = 255;
        }

        var encodeResult = codec.EncodeTga(new DecodedTexture(60, 48, rawPixels));
        Assert.True(encodeResult.Success);
        var tgaInputBytes = encodeResult.Data!;

        var overlayTgaBytes = await service.GenerateOverlayTgaAsync(tgaInputBytes, 'W', OverlayCorner.BottomRight);

        Assert.NotNull(overlayTgaBytes);
        Assert.True(overlayTgaBytes.Length > 18);
        var width = overlayTgaBytes[12] | (overlayTgaBytes[13] << 8);
        var height = overlayTgaBytes[14] | (overlayTgaBytes[15] << 8);
        Assert.Equal(60, width);
        Assert.Equal(48, height);
        Assert.Equal(32, overlayTgaBytes[16]);
    }

    /// <summary>
    /// Verifies that GenerateOverlayTgaAsync resizes non-60x48 input images to standard 60x48 dimensions.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GenerateOverlayTgaAsync_WithNonStandardDimensions_ResizesTo60x48Async()
    {
        var service = new IconOverlayService(NullLogger<IconOverlayService>.Instance);

        using var testImage = new Image<Rgba32>(128, 128);
        using var ms = new MemoryStream();
        await testImage.SaveAsPngAsync(ms);
        var inputBytes = ms.ToArray();

        var tgaBytes = await service.GenerateOverlayTgaAsync(inputBytes, 'Q', OverlayCorner.TopRight);

        Assert.NotNull(tgaBytes);
        var width = tgaBytes[12] | (tgaBytes[13] << 8);
        var height = tgaBytes[14] | (tgaBytes[15] << 8);
        Assert.Equal(60, width);
        Assert.Equal(48, height);
    }
}
