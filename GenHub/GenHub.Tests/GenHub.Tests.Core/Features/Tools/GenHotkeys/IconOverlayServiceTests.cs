using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Services.Tools.TextureEditor;
using GenHub.Features.Tools.GenHotkeys.Services;
using ImageMagick;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.GenHotkeys;

/// <summary>
/// Unit tests for <see cref="IconOverlayService"/>.
/// </summary>
public class IconOverlayServiceTests
{
    /// <summary>
    /// Verifies that GenerateOverlayTgaAsync produces a valid 60x48 32-bit uncompressed TGA.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GenerateOverlayTgaAsync_WithValidInput_ReturnsTgaBytesAsync()
    {
        var service = new IconOverlayService(NullLogger<IconOverlayService>.Instance);
        var inputBytes = CreatePngIcon(60, 48);

        var tgaBytes = await service.GenerateOverlayTgaAsync(inputBytes, 'A', OverlayCorner.TopLeft);

        Assert.NotNull(tgaBytes);
        Assert.True(tgaBytes.Length > 18);
        Assert.Equal(2, tgaBytes[2]); // uncompressed true-color
        var width = tgaBytes[12] | (tgaBytes[13] << 8);
        var height = tgaBytes[14] | (tgaBytes[15] << 8);
        Assert.Equal(60, width);
        Assert.Equal(48, height);
        Assert.Equal(32, tgaBytes[16]); // 32-bit depth
    }

    /// <summary>
    /// Verifies that GenerateOverlayTgaAsync throws an ArgumentNullException when input bytes are null.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GenerateOverlayTgaAsync_WithNullInput_ThrowsArgumentNullExceptionAsync()
    {
        var service = new IconOverlayService(NullLogger<IconOverlayService>.Instance);

        await Assert.ThrowsAsync<System.ArgumentNullException>(
            () => service.GenerateOverlayTgaAsync(null!, 'A', OverlayCorner.TopLeft));
    }

    /// <summary>
    /// Verifies that GenerateOverlayTgaAsync honours the cancellation token.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GenerateOverlayTgaAsync_WithCancellation_ThrowsOperationCanceledExceptionAsync()
    {
        var service = new IconOverlayService(NullLogger<IconOverlayService>.Instance);
        var inputBytes = CreatePngIcon(60, 48);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<System.OperationCanceledException>(
            () => service.GenerateOverlayTgaAsync(inputBytes, 'A', OverlayCorner.TopLeft, cts.Token));
    }

    /// <summary>
    /// Verifies that GenerateOverlayTgaAsync works with all overlay corner positions.
    /// </summary>
    /// <param name="corner">The target badge overlay corner.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(OverlayCorner.TopLeft)]
    [InlineData(OverlayCorner.TopRight)]
    [InlineData(OverlayCorner.BottomLeft)]
    [InlineData(OverlayCorner.BottomRight)]
    public async Task GenerateOverlayTgaAsync_WithDifferentCorners_SucceedsAsync(OverlayCorner corner)
    {
        var service = new IconOverlayService(NullLogger<IconOverlayService>.Instance);
        var inputBytes = CreatePngIcon(60, 48);

        var tgaBytes = await service.GenerateOverlayTgaAsync(inputBytes, 'B', corner);

        Assert.NotNull(tgaBytes);
        Assert.True(tgaBytes.Length > 18);
        var width = tgaBytes[12] | (tgaBytes[13] << 8);
        var height = tgaBytes[14] | (tgaBytes[15] << 8);
        Assert.Equal(60, width);
        Assert.Equal(48, height);
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
        var inputBytes = CreatePngIcon(60, 48);

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
    /// Verifies that GenerateOverlayTgaAsync falls back to ISageTextureCodec when Magick.NET cannot decode the source image bytes.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GenerateOverlayTgaAsync_WithSageTextureFallback_DecodesViaSageTextureCodecAsync()
    {
        var mockCodec = new Mock<ISageTextureCodec>();
        var nonImageBytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var rawPixels = new byte[60 * 48 * 4];
        for (int i = 0; i < rawPixels.Length; i += 4)
        {
            rawPixels[i] = 120;
            rawPixels[i + 1] = 150;
            rawPixels[i + 2] = 200;
            rawPixels[i + 3] = 255;
        }

        mockCodec.Setup(c => c.Decode(nonImageBytes, WndConstants.MappedImages.TextureExtensionTga, null))
            .Returns(OperationResult<DecodedTexture>.CreateSuccess(new DecodedTexture(60, 48, rawPixels)));

        var service = new IconOverlayService(NullLogger<IconOverlayService>.Instance, mockCodec.Object);
        var overlayTgaBytes = await service.GenerateOverlayTgaAsync(nonImageBytes, 'W', OverlayCorner.BottomRight);

        Assert.NotNull(overlayTgaBytes);
        Assert.True(overlayTgaBytes.Length > 18);
        var width = overlayTgaBytes[12] | (overlayTgaBytes[13] << 8);
        var height = overlayTgaBytes[14] | (overlayTgaBytes[15] << 8);
        Assert.Equal(60, width);
        Assert.Equal(48, height);
        Assert.Equal(32, overlayTgaBytes[16]);
        mockCodec.Verify(c => c.Decode(nonImageBytes, WndConstants.MappedImages.TextureExtensionTga, null), Times.Once);
    }

    /// <summary>
    /// Verifies that GenerateOverlayTgaAsync resizes non-60x48 input images to standard 60x48 dimensions.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GenerateOverlayTgaAsync_WithNonStandardDimensions_ResizesTo60x48Async()
    {
        var service = new IconOverlayService(NullLogger<IconOverlayService>.Instance);
        var inputBytes = CreatePngIcon(128, 128);

        var tgaBytes = await service.GenerateOverlayTgaAsync(inputBytes, 'Q', OverlayCorner.TopRight);

        Assert.NotNull(tgaBytes);
        var width = tgaBytes[12] | (tgaBytes[13] << 8);
        var height = tgaBytes[14] | (tgaBytes[15] << 8);
        Assert.Equal(60, width);
        Assert.Equal(48, height);
    }

    /// <summary>
    /// Verifies that GenerateOverlayTgaAsync skips stamping a badge when an unsupported character is passed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GenerateOverlayTgaAsync_WithUnsupportedChar_SkipsBadgeStampAsync()
    {
        var service = new IconOverlayService(NullLogger<IconOverlayService>.Instance);
        var inputBytes = CreatePngIcon(60, 48);

        var tgaBytes = await service.GenerateOverlayTgaAsync(inputBytes, '!', OverlayCorner.BottomRight);

        Assert.NotNull(tgaBytes);
        var width = tgaBytes[12] | (tgaBytes[13] << 8);
        var height = tgaBytes[14] | (tgaBytes[15] << 8);
        Assert.Equal(60, width);
        Assert.Equal(48, height);
    }

    private static byte[] CreatePngIcon(int width, int height)
    {
        using var image = new MagickImage(MagickColors.Transparent, (uint)width, (uint)height);
        return image.ToByteArray(MagickFormat.Png);
    }
}
