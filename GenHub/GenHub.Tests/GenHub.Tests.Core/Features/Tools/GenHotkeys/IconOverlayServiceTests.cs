using System.IO;
using System.Threading.Tasks;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Features.Tools.GenHotkeys.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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
}
