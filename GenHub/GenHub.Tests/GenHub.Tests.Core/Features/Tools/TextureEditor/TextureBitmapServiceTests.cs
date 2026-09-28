using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Services.Tools.TextureEditor;
using GenHub.Features.Tools.TextureEditor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.TextureEditor;

/// <summary>
/// Unit tests for <see cref="TextureBitmapService"/> cropping and atomic saves.
/// </summary>
public sealed class TextureBitmapServiceTests
{
    private readonly TextureBitmapService _service = new(
        new SageTextureCodec(NullLogger<SageTextureCodec>.Instance),
        NullLogger<TextureBitmapService>.Instance);

    /// <summary>
    /// Verifies that a negative origin crops to the source intersection instead of an oversized region.
    /// </summary>
    [AvaloniaFact]
    public void Crop_NegativeOrigin_ReturnsIntersection()
    {
        using var source = new WriteableBitmap(
            new PixelSize(20, 20),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);

        var cropped = TextureBitmapService.Crop(source, -10, -10, 20, 20);

        var rect = Assert.IsType<CroppedBitmap>(cropped).SourceRect;
        Assert.Equal(new PixelRect(0, 0, 10, 10), rect);
    }

    /// <summary>
    /// Verifies that a rectangle entirely outside the source returns null.
    /// </summary>
    [AvaloniaFact]
    public void Crop_FullyOutside_ReturnsNull()
    {
        using var source = new WriteableBitmap(
            new PixelSize(20, 20),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);

        Assert.Null(TextureBitmapService.Crop(source, -30, 0, 20, 20));
        Assert.Null(TextureBitmapService.Crop(source, 0, -30, 20, 20));
        Assert.Null(TextureBitmapService.Crop(source, 25, 25, 5, 5));
    }

    /// <summary>
    /// Verifies that corrupt image content returns a failure instead of throwing.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadDecodedAsync_CorruptPng_ReturnsFailureAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
            byte[] truncatedChunk = [0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0, 0, 0, 0, 0, 1];
            await File.WriteAllBytesAsync(path, [.. signature, .. truncatedChunk]);

            var result = await _service.LoadDecodedAsync(path);

            Assert.True(result.Failed);
            Assert.NotEmpty(result.Errors);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Verifies that TGA saves write exact bytes without leaving temp files behind.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveTgaAsync_ValidTexture_WritesFileWithoutTempLeftoversAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var texture = new DecodedTexture(2, 1, [255, 0, 0, 255, 0, 255, 0, 255]);
            string path = Path.Combine(directory, "out.tga");

            var result = await _service.SaveTgaAsync(texture, path);

            Assert.True(result.Success);
            Assert.Equal(path, result.Data);
            Assert.True(File.Exists(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that a TGA save into a missing directory fails without creating partial files.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveTgaAsync_MissingDirectory_ReturnsFailureWithoutPartialFilesAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "missing", "out.tga");
        try
        {
            var texture = new DecodedTexture(2, 1, [255, 0, 0, 255, 0, 255, 0, 255]);

            var result = await _service.SaveTgaAsync(texture, path);

            Assert.True(result.Failed);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that PNG saves produce a decodable PNG file without leaving temp files behind.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SavePngAsync_ValidTexture_WritesDecodablePngWithoutTempLeftoversAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var texture = new DecodedTexture(2, 1, [255, 0, 0, 255, 0, 255, 0, 128]);
            string path = Path.Combine(directory, "out.png");

            var result = await _service.SavePngAsync(texture, path);

            Assert.True(result.Success);
            byte[] bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], bytes.Take(8).ToArray());
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

            var decoded = await _service.LoadDecodedAsync(path);
            Assert.True(decoded.Success);
            Assert.NotNull(decoded.Data);
            Assert.Equal(texture.PixelData, decoded.Data.PixelData);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that bitmap conversion rejects pixel data whose length mismatches the dimensions.
    /// </summary>
    [Fact]
    public void ToBitmap_MismatchedPixels_ReturnsFailure()
    {
        var result = _service.ToBitmap(new DecodedTexture(2, 2, [1, 2, 3]));

        Assert.True(result.Failed);
        Assert.NotEmpty(result.Errors);
    }

    /// <summary>
    /// Verifies that PNG saves reject pixel data whose length mismatches the dimensions.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SavePngAsync_MismatchedPixels_ReturnsFailureAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");

        var result = await _service.SavePngAsync(new DecodedTexture(2, 2, [1, 2, 3]), path);

        Assert.True(result.Failed);
        Assert.False(File.Exists(path));
    }
}
