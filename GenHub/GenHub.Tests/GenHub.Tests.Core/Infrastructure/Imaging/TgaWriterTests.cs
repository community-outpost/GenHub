using GenHub.Infrastructure.Imaging;
using ImageMagick;
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
    /// Grayscale and palette sources are still written as uncompressed 32-bit true-colour TGA.
    /// </summary>
    /// <param name="colorType">The source colour type.</param>
    [Theory]
    [InlineData(ColorType.Grayscale)]
    [InlineData(ColorType.Palette)]
    [InlineData(ColorType.GrayscaleAlpha)]
    public void Write32BitUncompressed_NonRgbSource_WritesTrueColor32Bit(ColorType colorType)
    {
        var path = Path.Combine(_tempDir, "texture.tga");
        using (var image = new MagickImage(MagickColors.Gray, 3, 2))
        {
            image.ColorType = colorType;
            TgaWriter.Write32BitUncompressed(image, path);
        }

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(0, bytes[1]);
        Assert.Equal(2, bytes[2]);
        Assert.Equal(32, bytes[16]);
        Assert.Equal(0x08, bytes[17]);
        Assert.Equal(18 + (3 * 2 * 4), bytes.Length);
        Assert.Equal(new byte[] { 128, 128, 128, 255 }, bytes[18..22]);
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
}
