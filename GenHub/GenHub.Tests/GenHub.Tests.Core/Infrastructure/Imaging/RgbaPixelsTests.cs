using GenHub.Infrastructure.Imaging;
using ImageMagick;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Imaging;

/// <summary>
/// Tests for <see cref="RgbaPixels"/>.
/// </summary>
public class RgbaPixelsTests
{
    /// <summary>
    /// A CMYK JPEG decoded from raw channel values exports as sRGB, not as raw C, M and Y.
    /// </summary>
    [Fact]
    public void ToBytes_DecodedCmykJpeg_ExportsSrgb()
    {
        using var image = new MagickImage(CreateCmykRedJpeg());
        Assert.Equal(ColorSpace.CMYK, image.ColorSpace);

        var bytes = RgbaPixels.ToBytes(image);

        Assert.InRange(bytes[0], 230, 255);
        Assert.InRange(bytes[1], 0, 25);
        Assert.InRange(bytes[2], 0, 25);
        Assert.Equal(255, bytes[3]);
    }

    /// <summary>
    /// Palette and truecolor images are exported as sRGB with an opaque alpha.
    /// </summary>
    /// <param name="colorSpace">The source colour space.</param>
    /// <param name="colorType">The source colour type.</param>
    [Theory]
    [InlineData(ColorSpace.sRGB, ColorType.Palette)]
    [InlineData(ColorSpace.sRGB, ColorType.TrueColor)]
    public void ToBytes_NonRgbaSource_ExportsOpaqueSrgb(ColorSpace colorSpace, ColorType colorType)
    {
        using var image = new MagickImage(MagickColors.Red, 2, 1);
        image.ColorSpace = colorSpace;
        image.ColorType = colorType;

        var bytes = RgbaPixels.ToBytes(image);

        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 0, 0, 255 }, bytes);
    }

    /// <summary>
    /// Grayscale images expand to equal RGB channels.
    /// </summary>
    [Fact]
    public void ToBytes_Grayscale_ExpandsToRgb()
    {
        using var image = new MagickImage(MagickColor.FromRgb(64, 64, 64), 1, 1);
        image.ColorType = ColorType.Grayscale;

        Assert.Equal(new byte[] { 64, 64, 64, 255 }, RgbaPixels.ToBytes(image));
    }

    /// <summary>
    /// Pixels round-trip through <see cref="RgbaPixels.FromBytes"/> unchanged.
    /// </summary>
    [Fact]
    public void FromBytes_ThenToBytes_RoundTrips()
    {
        byte[] pixels = [10, 20, 30, 40, 200, 150, 100, 255];

        using var image = RgbaPixels.FromBytes(pixels, 2, 1);

        Assert.Equal(pixels, RgbaPixels.ToBytes(image));
    }

    /// <summary>
    /// Builds a CMYK JPEG whose pixels are pure red (C=0, M=255, Y=255, K=0) without going through
    /// the colorspace setter under test.
    /// </summary>
    /// <returns>The JPEG bytes.</returns>
    internal static byte[] CreateCmykRedJpeg()
    {
        byte[] cmyk = [0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0];
        using var image = new MagickImage();
        image.ReadPixels(cmyk, new PixelReadSettings(2, 2, StorageType.Char, "CMYK"));
        return image.ToByteArray(MagickFormat.Jpeg);
    }
}
