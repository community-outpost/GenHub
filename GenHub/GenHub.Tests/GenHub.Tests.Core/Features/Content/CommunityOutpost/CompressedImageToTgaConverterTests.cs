using GenHub.Features.Content.Services.CommunityOutpost;
using ImageMagick;
using ImageMagick.Drawing;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.CommunityOutpost;

/// <summary>
/// Tests for <see cref="CompressedImageToTgaConverter"/>.
/// </summary>
public class CompressedImageToTgaConverterTests : IDisposable
{
    /// <summary>
    /// A minimal valid AVIF (8x8 solid colour). Embedded as base64 so the test needs no
    /// external tooling and runs identically on every platform.
    /// </summary>
    private const string TinyAvifBase64 =
        "AAAAIGZ0eXBhdmlmAAAAAGF2aWZtaWYxbWlhZk1BMUIAAADybWV0YQAAAAAAAAAoaGRscgAAAAAAAAAA"
        + "cGljdAAAAAAAAAAAAAAAAGxpYmF2aWYAAAAADnBpdG0AAAAAAAEAAAAeaWxvYwAAAABEAAABAAEAAAAB"
        + "AAABGgAAAB8AAAAoaWluZgAAAAAAAQAAABppbmZlAgAAAAABAABhdjAxQ29sb3IAAAAAamlwcnAAAABL"
        + "aXBjbwAAABRpc3BlAAAAAAAAAAgAAAAIAAAAEHBpeGkAAAAAAwgICAAAAAxhdjFDgQAMAAAAABNjb2xy"
        + "bmNseAABAA0ABgAAAAAXaXBtYQAAAAAAAAABAAEEAQKDBAAAACdtZGF0EgAKCBgIv2CAhoMCMhEXwAkk"
        + "kkQAALATVO0wKFrK0A==";

    private const int TgaHeaderLength = 18;
    private const byte TgaUncompressedTrueColor = 2;
    private const byte TgaBottomLeftWith8AlphaBits = 0x08;

    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        $"genhub-tga-{Guid.NewGuid():N}");

    private readonly CompressedImageToTgaConverter _converter =
        new(NullLogger<CompressedImageToTgaConverter>.Instance);

    /// <summary>
    /// Initializes a new instance of the <see cref="CompressedImageToTgaConverterTests"/> class.
    /// </summary>
    public CompressedImageToTgaConverterTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// AVIF converts on every platform and produces the uncompressed 32-bit TGA the game reads.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ConvertFileAsync_Avif_WritesUncompressed32BitTgaAsync()
    {
        var source = Path.Combine(_tempDir, "texture.avif");
        await File.WriteAllBytesAsync(source, Convert.FromBase64String(TinyAvifBase64));
        var destination = Path.Combine(_tempDir, "texture.tga");

        await _converter.ConvertFileAsync(source, destination);

        var bytes = await File.ReadAllBytesAsync(destination);
        AssertUncompressed32BitTga(bytes, width: 8, height: 8);
    }

    /// <summary>
    /// WebP keeps its alpha and stays upright. The TGA is stored bottom-up, so the first
    /// pixel in the file is the bottom-left pixel of the source image.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ConvertFileAsync_WebpWithAlpha_PreservesAlphaAndOrientationAsync()
    {
        var source = Path.Combine(_tempDir, "texture.webp");
        using (var image = new MagickImage(MagickColors.Red, 4, 2))
        {
            image.Draw(new Drawables().FillColor(MagickColors.Blue).Rectangle(0, 1, 3, 1));
            image.Alpha(AlphaOption.Set);
            using (var pixels = image.GetPixels())
            {
                pixels.SetPixel(3, 0, [0, 0, 0, 0]);
            }

            image.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
            await image.WriteAsync(source, MagickFormat.WebP);
        }

        var destination = Path.Combine(_tempDir, "texture.tga");

        await _converter.ConvertFileAsync(source, destination);

        var bytes = await File.ReadAllBytesAsync(destination);
        AssertUncompressed32BitTga(bytes, width: 4, height: 2);

        // BGRA. The bottom row is blue and opaque.
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, bytes[TgaHeaderLength..(TgaHeaderLength + 4)]);

        // The top-right pixel is the last pixel in the file and is transparent.
        Assert.Equal(0, bytes[^1]);
    }

    /// <summary>
    /// Converting a directory replaces each source with a TGA of the same base name.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ConvertDirectoryAsync_Avif_ReplacesSourceWithTgaAsync()
    {
        var nested = Path.Combine(_tempDir, "Art", "Textures");
        Directory.CreateDirectory(nested);
        var source = Path.Combine(nested, "texture.AVIF");
        await File.WriteAllBytesAsync(source, Convert.FromBase64String(TinyAvifBase64));

        var converted = await _converter.ConvertDirectoryAsync(_tempDir);

        Assert.Equal(1, converted);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(nested, "texture.tga")));
    }

    /// <summary>
    /// A file that fails to decode is kept on disk and not counted as converted.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ConvertDirectoryAsync_CorruptAvif_IsLeftOnDiskAsync()
    {
        var source = Path.Combine(_tempDir, "texture.avif");
        await File.WriteAllBytesAsync(source, [0x00, 0x01, 0x02, 0x03]);

        var converted = await _converter.ConvertDirectoryAsync(_tempDir);

        Assert.Equal(0, converted);
        Assert.True(File.Exists(source));
    }

    /// <summary>
    /// Concurrent conversions all produce a complete 8x8 uncompressed 32-bit TGA.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ConvertFileAsync_ConcurrentAvif_AllSucceedAsync()
    {
        var tasks = Enumerable.Range(0, 8)
            .Select(
                async index =>
                {
                    var source = Path.Combine(_tempDir, $"texture-{index}.avif");
                    var destination = Path.Combine(_tempDir, $"texture-{index}.tga");
                    await File.WriteAllBytesAsync(source, Convert.FromBase64String(TinyAvifBase64));
                    await _converter.ConvertFileAsync(source, destination);
                    return destination;
                });

        var destinations = await Task.WhenAll(tasks);

        Assert.All(destinations, destination => AssertUncompressed32BitTga(File.ReadAllBytes(destination), width: 8, height: 8));
    }

    /// <summary>
    /// Releases the temporary directory used by these tests.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private static void AssertUncompressed32BitTga(byte[] bytes, int width, int height)
    {
        Assert.Equal(TgaUncompressedTrueColor, bytes[2]);
        Assert.Equal(width, BitConverter.ToUInt16(bytes, 12));
        Assert.Equal(height, BitConverter.ToUInt16(bytes, 14));
        Assert.Equal(32, bytes[16]);
        Assert.Equal(TgaBottomLeftWith8AlphaBits, bytes[17]);
        Assert.Equal(TgaHeaderLength + (width * height * 4), bytes.Length);
    }
}
