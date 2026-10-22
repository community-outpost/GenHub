using FluentAssertions;
using GenHub.Features.Tools.WndEditor.Services;
using ImageMagick;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Services;

/// <summary>
/// Unit tests for <see cref="WndPreviewImageComposer"/>.
/// </summary>
public sealed class WndPreviewImageComposerTests
{
    /// <summary>
    /// Tests that three pieces compose into a bar with caps and tiled center.
    /// </summary>
    [Fact]
    public void ComposeThreePiece_WideBar_TilesCenterBetweenCaps()
    {
        // Arrange: two-tone center must tile; the pattern repeats between caps.
        var left = SolidPng(MagickColors.Red, 10, 6);
        var center = TwoToneHorizontalPng();
        var right = SolidPng(MagickColors.Blue, 10, 6);

        // Act
        var composed = WndPreviewImageComposer.ComposeThreePiece(left, center, right, 30, 6);

        // Assert
        composed.Should().NotBeNull();
        using var decoded = new MagickImage(composed!);
        decoded.Width.Should().Be(30);
        decoded.Height.Should().Be(6);
        PixelAt(decoded, 2, 3).Should().Be(MagickColors.Red);

        // Under tiling the two-tone pattern repeats: yellow at x=12, green at x=18.
        PixelAt(decoded, 12, 3).R.Should().BeGreaterThan((ushort)(Quantum.Max * 3 / 4));
        PixelAt(decoded, 18, 3).R.Should().BeLessThan((ushort)(Quantum.Max / 4));
        PixelAt(decoded, 27, 3).Should().Be(MagickColors.Blue);
    }

    /// <summary>
    /// Tests that pieces stretch vertically to the window height.
    /// </summary>
    [Fact]
    public void ComposeThreePiece_TallBar_StretchesPieces()
    {
        // Arrange
        var left = SolidPng(MagickColors.Red, 10, 4);
        var center = SolidPng(MagickColors.Green, 4, 4);
        var right = SolidPng(MagickColors.Blue, 10, 4);

        // Act
        var composed = WndPreviewImageComposer.ComposeThreePiece(left, center, right, 30, 12);

        // Assert
        composed.Should().NotBeNull();
        using var decoded = new MagickImage(composed!);
        decoded.Width.Should().Be(30);
        decoded.Height.Should().Be(12);
        PixelAt(decoded, 2, 10).Should().Be(MagickColors.Red);
        PixelAt(decoded, 27, 10).Should().Be(MagickColors.Blue);
    }

    /// <summary>
    /// Tests that a narrow window falls back to cropped halves like the engine.
    /// </summary>
    [Fact]
    public void ComposeThreePiece_NarrowBar_DrawsHalves()
    {
        // Arrange
        var left = SolidPng(MagickColors.Red, 20, 6);
        var center = SolidPng(MagickColors.Green, 4, 6);
        var right = SolidPng(MagickColors.Blue, 20, 6);

        // Act
        var composed = WndPreviewImageComposer.ComposeThreePiece(left, center, right, 30, 6);

        // Assert
        composed.Should().NotBeNull();
        using var decoded = new MagickImage(composed!);
        decoded.Width.Should().Be(30);
        PixelAt(decoded, 2, 3).Should().Be(MagickColors.Red);
        PixelAt(decoded, 27, 3).Should().Be(MagickColors.Blue);
    }

    /// <summary>
    /// Tests that three pieces compose into a vertical bar with caps and tiled center.
    /// </summary>
    [Fact]
    public void ComposeThreePieceVertical_TallBar_TilesCenterBetweenCaps()
    {
        // Arrange: two-tone center must tile; the pattern repeats between caps.
        var top = SolidPng(MagickColors.Red, 6, 10);
        var center = TwoToneVerticalPng();
        var bottom = SolidPng(MagickColors.Blue, 6, 10);

        // Act
        var composed = WndPreviewImageComposer.ComposeThreePieceVertical(top, center, bottom, 6, 30);

        // Assert
        composed.Should().NotBeNull();
        using var decoded = new MagickImage(composed!);
        decoded.Width.Should().Be(6);
        decoded.Height.Should().Be(30);
        PixelAt(decoded, 3, 2).Should().Be(MagickColors.Red);

        // Under tiling the two-tone pattern repeats: yellow at y=12, green at y=18.
        PixelAt(decoded, 3, 12).R.Should().BeGreaterThan((ushort)(Quantum.Max * 3 / 4));
        PixelAt(decoded, 3, 18).R.Should().BeLessThan((ushort)(Quantum.Max / 4));
        PixelAt(decoded, 3, 27).Should().Be(MagickColors.Blue);
    }

    /// <summary>
    /// Tests that invalid sizes return null instead of throwing.
    /// </summary>
    /// <param name="width">The target width.</param>
    /// <param name="height">The target height.</param>
    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-5, 10)]
    [InlineData(5000, 10)]
    public void ComposeThreePiece_InvalidSize_ReturnsNull(int width, int height)
    {
        // Arrange
        var piece = SolidPng(MagickColors.Red, 4, 4);

        // Act
        var composed = WndPreviewImageComposer.ComposeThreePiece(piece, piece, piece, width, height);

        // Assert
        composed.Should().BeNull();
    }

    /// <summary>
    /// Tests that squashing narrow bars preserves both outer cap borders without gaps or cuts.
    /// </summary>
    [Fact]
    public void ComposeThreePiece_NarrowBar_PreservesBothCapBorders()
    {
        // Left cap has a yellow border at x=0; right cap has a yellow border at x=19.
        using var leftImg = new MagickImage(MagickColors.Red, 20, 6);
        using var leftBorder = new MagickImage(MagickColors.Yellow, 2, 6);
        leftImg.Composite(leftBorder, 0, 0, CompositeOperator.Over);
        var left = leftImg.ToByteArray(MagickFormat.Png);

        using var rightImg = new MagickImage(MagickColors.Blue, 20, 6);
        using var rightBorder = new MagickImage(MagickColors.Yellow, 2, 6);
        rightImg.Composite(rightBorder, 18, 0, CompositeOperator.Over);
        var right = rightImg.ToByteArray(MagickFormat.Png);

        var center = SolidPng(MagickColors.Green, 4, 6);

        // Act: compose at width 30 (< 20 + 20)
        var composed = WndPreviewImageComposer.ComposeThreePiece(left, center, right, 30, 6);

        // Assert: left border at x=0 is intact, right border at x=29 is intact
        composed.Should().NotBeNull();
        using var decoded = new MagickImage(composed!);
        decoded.Width.Should().Be(30);
        PixelAt(decoded, 0, 3).Should().Be(MagickColors.Yellow);
        PixelAt(decoded, 29, 3).Should().Be(MagickColors.Yellow);
    }

    /// <summary>
    /// Tests that a fractional center tile fills the full gap between caps without leaving transparent pixels.
    /// </summary>
    [Fact]
    public void ComposeThreePiece_ClippedCenter_DoesNotLeaveGap()
    {
        // Arrange: left 10px, right 10px, center 10px, target width 25 (10 left + 5 clipped center + 10 right)
        var left = SolidPng(MagickColors.Red, 10, 6);
        var center = SolidPng(MagickColors.Green, 10, 6);
        var right = SolidPng(MagickColors.Blue, 10, 6);

        // Act
        var composed = WndPreviewImageComposer.ComposeThreePiece(left, center, right, 25, 6);

        // Assert: every pixel from x=0 to 24 must be fully opaque
        composed.Should().NotBeNull();
        using var decoded = new MagickImage(composed!);
        decoded.Width.Should().Be(25);
        using var pixels = decoded.GetPixels();
        for (var x = 0; x < 25; x++)
        {
            var p = pixels.GetPixel(x, 3)!.ToColor()!;
            p.A.Should().Be((ushort)Quantum.Max, $"pixel at x={x} should be fully opaque");
        }
    }

    private static byte[] SolidPng(MagickColor color, uint width, uint height)
    {
        using var image = new MagickImage(color, width, height);
        return image.ToByteArray(MagickFormat.Png);
    }

    private static byte[] TwoToneHorizontalPng()
    {
        using var canvas = new MagickImage(MagickColors.Green, 4, 6);
        using var right = new MagickImage(MagickColors.Yellow, 2, 6);
        canvas.Composite(right, 2, 0, CompositeOperator.Over);
        return canvas.ToByteArray(MagickFormat.Png);
    }

    private static byte[] TwoToneVerticalPng()
    {
        using var canvas = new MagickImage(MagickColors.Green, 6, 4);
        using var bottom = new MagickImage(MagickColors.Yellow, 6, 2);
        canvas.Composite(bottom, 0, 2, CompositeOperator.Over);
        return canvas.ToByteArray(MagickFormat.Png);
    }

    private static IMagickColor<ushort> PixelAt(MagickImage image, int x, int y)
    {
        using var pixels = image.GetPixels();
        return pixels.GetPixel(x, y)!.ToColor()!;
    }
}
