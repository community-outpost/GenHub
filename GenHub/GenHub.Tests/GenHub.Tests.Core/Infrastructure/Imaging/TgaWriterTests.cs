using GenHub.Infrastructure.Imaging;
using ImageMagick;
using ImageMagick.Drawing;
using System;
using System.IO;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Imaging;

/// <summary>
/// Tests for <see cref="TgaWriter"/>.
/// </summary>
public sealed class TgaWriterTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"genhub-tgawriter-{Guid.NewGuid():N}");

    /// <summary>
    /// Initializes a new instance of the <see cref="TgaWriterTests"/> class.
    /// </summary>
    public TgaWriterTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// RGB sources, with or without alpha, are written as BGRA with the bottom row first.
    /// </summary>
    /// <param name="colorType">The source colour type.</param>
    [Theory]
    [InlineData(ColorType.TrueColor)]
    [InlineData(ColorType.TrueColorAlpha)]
    public void Write32BitUncompressed_RgbSource_WritesBgraBottomRowFirst(ColorType colorType)
    {
        var bytes = WriteTwoRowImage(MagickColor.FromRgb(10, 20, 30), MagickColor.FromRgb(200, 150, 100), colorType);

        AssertUncompressed32BitHeader(bytes);
        Assert.Equal(new byte[] { 100, 150, 200, 255 }, bytes[18..22]);
        Assert.Equal(new byte[] { 30, 20, 10, 255 }, bytes[^4..]);
    }

    /// <summary>
    /// Grayscale and palette sources are still written as uncompressed 32-bit true-colour TGA
    /// with the bottom row first.
    /// </summary>
    /// <param name="colorType">The source colour type.</param>
    [Theory]
    [InlineData(ColorType.Grayscale)]
    [InlineData(ColorType.Palette)]
    [InlineData(ColorType.GrayscaleAlpha)]
    public void Write32BitUncompressed_NonRgbSource_WritesTrueColor32Bit(ColorType colorType)
    {
        var bytes = WriteTwoRowImage(MagickColor.FromRgb(64, 64, 64), MagickColor.FromRgb(192, 192, 192), colorType);

        AssertUncompressed32BitHeader(bytes);
        Assert.Equal(new byte[] { 192, 192, 192, 255 }, bytes[18..22]);
        Assert.Equal(new byte[] { 64, 64, 64, 255 }, bytes[^4..]);
    }

    /// <summary>
    /// Releases the temporary directory used by these tests.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private static void AssertUncompressed32BitHeader(byte[] bytes)
    {
        Assert.Equal(0, bytes[1]);
        Assert.Equal(2, bytes[2]);
        Assert.Equal(3, BitConverter.ToUInt16(bytes, 12));
        Assert.Equal(2, BitConverter.ToUInt16(bytes, 14));
        Assert.Equal(32, bytes[16]);
        Assert.Equal(0x08, bytes[17]);
        Assert.Equal(18 + (3 * 2 * 4), bytes.Length);
    }

    private byte[] WriteTwoRowImage(MagickColor top, MagickColor bottom, ColorType colorType)
    {
        var path = Path.Combine(_tempDir, "texture.tga");
        using (var image = new MagickImage(top, 3, 2))
        {
            image.Draw(new Drawables().FillColor(bottom).Rectangle(0, 1, 2, 1));
            image.ColorType = colorType;
            TgaWriter.Write32BitUncompressed(image, path);
        }

        return File.ReadAllBytes(path);
    }
}
