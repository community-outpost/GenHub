using FluentAssertions;
using GenHub.Tests.Core.Infrastructure.Imaging;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;
using Moq;
using ImageMagick;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Services;

/// <summary>
/// Unit tests for <see cref="ImageConversionService"/>.
/// </summary>
public sealed class ImageConversionServiceTests : IDisposable
{
    private readonly Mock<ILogger<ImageConversionService>> _mockLogger;
    private readonly ImageConversionService _service;
    private readonly string _tempDirectory;

    public ImageConversionServiceTests()
    {
        _mockLogger = new Mock<ILogger<ImageConversionService>>();
        _service = new ImageConversionService(_mockLogger.Object);
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        // Act
        var service = new ImageConversionService(_mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task ConvertImageAsync_WithNonExistentSource_ReturnsFalse()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "nonexistent.psd");
        var targetPath = Path.Combine(_tempDirectory, "output.dds");

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ConvertImageAsync_WithValidBmpFile_ConvertsSuccessfully()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.bmp");
        var targetPath = Path.Combine(_tempDirectory, "test.tga");

        // Create a simple 1x1 BMP file
        WriteSolidImage(sourcePath, 1, 1, MagickColors.Red, MagickFormat.Bmp3);

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath);

        // Assert
        result.Should().BeTrue();
        File.Exists(targetPath).Should().BeTrue();
    }

    [Fact]
    public async Task ConvertImageAsync_WithParameters_AppliesParameters()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.bmp");
        var targetPath = Path.Combine(_tempDirectory, "test.tga");

        WriteSolidImage(sourcePath, 1, 1, MagickColors.Red, MagickFormat.Bmp3);

        var parameters = new Dictionary<string, object>
        {
            ["resize"] = "2x2",
            ["resampling"] = "nearest"
        };

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath, parameters);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ConvertImageAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.bmp");
        var targetPath = Path.Combine(_tempDirectory, "test.tga");

        WriteSolidImage(sourcePath, 1, 1, MagickColors.Red, MagickFormat.Bmp3);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = () => _service.ConvertImageAsync(sourcePath, targetPath, cancellationToken: cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task HasAlphaChannelAsync_WithRgbaImage_ReturnsTrue()
    {
        // Arrange
        var imagePath = Path.Combine(_tempDirectory, "rgba.bmp");

        WriteSolidImage(imagePath, 1, 1, MagickColor.FromRgba(255, 0, 0, 128), MagickFormat.Bmp);

        // Act
        var result = await _service.HasAlphaChannelAsync(imagePath);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasAlphaChannelAsync_WithRgbImage_ReturnsFalse()
    {
        // Arrange
        var imagePath = Path.Combine(_tempDirectory, "rgb.bmp");

        WriteSolidImage(imagePath, 1, 1, MagickColors.Red, MagickFormat.Bmp3);

        // Act
        var result = await _service.HasAlphaChannelAsync(imagePath);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasAlphaChannelAsync_WithNonExistentFile_ReturnsFalse()
    {
        // Arrange
        var imagePath = Path.Combine(_tempDirectory, "nonexistent.bmp");

        // Act
        var result = await _service.HasAlphaChannelAsync(imagePath);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetRecommendedDxtFormatAsync_WithAlpha_ReturnsDxt5()
    {
        // Arrange
        var imagePath = Path.Combine(_tempDirectory, "rgba.bmp");

        WriteSolidImage(imagePath, 1, 1, MagickColor.FromRgba(255, 0, 0, 128), MagickFormat.Bmp);

        // Act
        var result = await _service.GetRecommendedDxtFormatAsync(imagePath);

        // Assert
        result.Should().Be("DXT5");
    }

    [Fact]
    public async Task GetRecommendedDxtFormatAsync_WithoutAlpha_ReturnsDxt1()
    {
        // Arrange
        var imagePath = Path.Combine(_tempDirectory, "rgb.bmp");

        WriteSolidImage(imagePath, 1, 1, MagickColors.Red, MagickFormat.Bmp3);

        // Act
        var result = await _service.GetRecommendedDxtFormatAsync(imagePath);

        // Assert
        result.Should().Be("DXT1");
    }

    [Fact]
    public async Task ConvertImageAsync_CreatesTargetDirectory()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.bmp");
        var targetDir = Path.Combine(_tempDirectory, "subdir");
        var targetPath = Path.Combine(targetDir, "test.tga");

        WriteSolidImage(sourcePath, 1, 1, MagickColors.Red, MagickFormat.Bmp3);

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath);

        // Assert
        result.Should().BeTrue();
        Directory.Exists(targetDir).Should().BeTrue();
        File.Exists(targetPath).Should().BeTrue();
    }

    [Fact]
    public async Task ConvertImageAsync_WithResizeParameter_ResizesImage()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.bmp");
        var targetPath = Path.Combine(_tempDirectory, "test_resized.bmp");

        WriteSolidImage(sourcePath, 1, 1, MagickColors.Red, MagickFormat.Bmp3);

        var parameters = new Dictionary<string, object>
        {
            ["resize"] = "4x4"
        };

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath, parameters);

        // Assert
        result.Should().BeTrue();
        File.Exists(targetPath).Should().BeTrue();
    }

    [Fact]
    public async Task ConvertImageAsync_WithInvalidParameters_HandlesGracefully()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.bmp");
        var targetPath = Path.Combine(_tempDirectory, "test.tga");

        WriteSolidImage(sourcePath, 1, 1, MagickColors.Red, MagickFormat.Bmp3);

        var parameters = new Dictionary<string, object>
        {
            ["invalid_param"] = "invalid_value"
        };

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath, parameters);

        // Assert
        result.Should().BeTrue(); // Should still convert, just ignore invalid params
    }

    [Theory]
    [InlineData(".bmp", ".tga")]
    [InlineData(".bmp", ".bmp")]
    [InlineData(".tga", ".bmp")]
    public async Task ConvertImageAsync_WithVariousFormats_ConvertsSuccessfully(string sourceExt, string targetExt)
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, $"test{sourceExt}");
        var targetPath = Path.Combine(_tempDirectory, $"test{targetExt}");

        WriteSolidImage(sourcePath, 1, 1, MagickColors.Red, sourceExt == ".tga" ? MagickFormat.Tga : MagickFormat.Bmp3);

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath);

        // Assert
        result.Should().BeTrue();
        File.Exists(targetPath).Should().BeTrue();
    }

    [Fact]
    public async Task HasAlphaChannelAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var imagePath = Path.Combine(_tempDirectory, "test.bmp");

        WriteSolidImage(imagePath, 1, 1, MagickColors.Red, MagickFormat.Bmp3);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = () => _service.HasAlphaChannelAsync(imagePath, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ConvertImageAsync_TgaToDdsWithoutAlpha_ProducesDxt1()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "opaque.tga");
        var targetPath = Path.Combine(_tempDirectory, "opaque.dds");

        WriteSolidImage(sourcePath, 8, 8, MagickColors.Red, MagickFormat.Tga);

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath);

        // Assert
        result.Should().BeTrue();
        ReadDdsFourCC(targetPath).Should().Be("DXT1");
    }

    [Fact]
    public async Task ConvertImageAsync_TgaToDdsWithAlpha_ProducesDxt5()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "transparent.tga");
        var targetPath = Path.Combine(_tempDirectory, "transparent.dds");

        using (var image = new MagickImage(MagickColors.Red, 8, 8))
        {
            image.Alpha(AlphaOption.Set);
            using (var pixels = image.GetPixelsUnsafe())
            {
                pixels.SetPixel(0, 0, [ushort.MaxValue, 0, 0, 0]);
            }

            image.Write(sourcePath, MagickFormat.Tga);
        }

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath);

        // Assert
        result.Should().BeTrue();
        ReadDdsFourCC(targetPath).Should().Be("DXT5");
    }

    [Fact]
    public async Task ConvertImageAsync_WithResizeArray_WritesResizedTga()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.bmp");
        var targetPath = Path.Combine(_tempDirectory, "test.tga");
        WriteSolidImage(sourcePath, 1, 1, MagickColors.Red, MagickFormat.Bmp3);

        var parameters = new Dictionary<string, object>
        {
            ["resize"] = new[] { 4, 2 },
            ["resampling"] = "nearest"
        };

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath, parameters);

        // Assert
        result.Should().BeTrue();
        var bytes = await File.ReadAllBytesAsync(targetPath);
        BitConverter.ToUInt16(bytes, 12).Should().Be(4);
        BitConverter.ToUInt16(bytes, 14).Should().Be(2);
        bytes[16].Should().Be(32);
    }

    [Fact]
    public async Task ConvertImageAsync_ResizeWithTransparency_KeepsColorUnderZeroAlpha()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "transparent.tga");
        var targetPath = Path.Combine(_tempDirectory, "resized.tga");
        WriteSolidImage(sourcePath, 2, 2, MagickColor.FromRgba(0, 255, 0, 0), MagickFormat.Tga);

        var parameters = new Dictionary<string, object>
        {
            ["resize"] = 4,
            ["resampling"] = "bilinear"
        };

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath, parameters);

        // Assert
        result.Should().BeTrue();
        var bytes = await File.ReadAllBytesAsync(targetPath);
        BitConverter.ToUInt16(bytes, 12).Should().Be(4);

        // BGRA. Green survives even though every pixel is fully transparent.
        bytes[18..22].Should().Equal(0, 255, 0, 0);
    }

    [Fact]
    public async Task ConvertImageAsync_ResizeGrayscaleWithTransparency_WritesRgbaTga()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "gray.png");
        var targetPath = Path.Combine(_tempDirectory, "gray.tga");
        using (var image = new MagickImage(MagickColor.FromRgba(128, 128, 128, 64), 2, 2))
        {
            image.ColorType = ColorType.GrayscaleAlpha;
            image.Write(sourcePath, MagickFormat.Png);
        }

        var parameters = new Dictionary<string, object>
        {
            ["resize"] = 4,
            ["resampling"] = "nearest"
        };

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath, parameters);

        // Assert
        result.Should().BeTrue();
        var bytes = await File.ReadAllBytesAsync(targetPath);
        bytes[2].Should().Be(2);
        bytes[16].Should().Be(32);
        bytes[18..22].Should().Equal(128, 128, 128, 64);
    }

    [Theory]
    [InlineData(255)]
    [InlineData(128)]
    public async Task ConvertImageAsync_TgaToBmp_Writes24BitTruecolor(byte alpha)
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "source.tga");
        var targetPath = Path.Combine(_tempDirectory, "target.bmp");
        WriteSolidImage(sourcePath, 2, 2, MagickColor.FromRgba(255, 0, 0, alpha), MagickFormat.Tga);

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath);

        // Assert
        result.Should().BeTrue();
        ReadBmpBitsPerPixel(targetPath).Should().Be(24);
    }

    [Theory]
    [InlineData(ColorType.Grayscale)]
    [InlineData(ColorType.Palette)]
    public async Task ConvertImageAsync_GrayscaleOrPalettePngToBmp_Writes24BitTruecolor(ColorType colorType)
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "source.png");
        var targetPath = Path.Combine(_tempDirectory, "target.bmp");
        using (var image = new MagickImage(MagickColors.Gray, 2, 2))
        {
            image.ColorType = colorType;
            image.Write(sourcePath, colorType == ColorType.Palette ? MagickFormat.Png8 : MagickFormat.Png);
        }

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath);

        // Assert
        result.Should().BeTrue();
        ReadBmpBitsPerPixel(targetPath).Should().Be(24);
    }

    [Fact]
    public async Task ConvertImageAsync_BmpWithAlphaToBmp_Stays32Bit()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "source.bmp");
        var targetPath = Path.Combine(_tempDirectory, "target.bmp");
        WriteSolidImage(sourcePath, 2, 2, MagickColor.FromRgba(255, 0, 0, 128), MagickFormat.Bmp);

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath);

        // Assert
        result.Should().BeTrue();
        ReadBmpBitsPerPixel(targetPath).Should().Be(32);
    }

    [Theory]
    [InlineData(ColorType.TrueColor, 8, true)]
    [InlineData(ColorType.Grayscale, 8, false)]
    [InlineData(ColorType.Grayscale, 16, false)]
    [InlineData(ColorType.GrayscaleAlpha, 8, false)]
    [InlineData(ColorType.GrayscaleAlpha, 16, true)]
    public async Task ConvertImageAsync_Tiff_RejectsFewerThan24BitsPerPixel(ColorType colorType, int depth, bool expected)
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "source.tif");
        var targetPath = Path.Combine(_tempDirectory, "target.tga");
        using (var image = new MagickImage(MagickColor.FromRgba(200, 100, 50, 200), 2, 2))
        {
            image.ColorType = colorType;
            image.Depth = (uint)depth;
            image.Write(sourcePath, MagickFormat.Tiff);
        }

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task ConvertImageAsync_CmykJpegToDds_ConvertsColorsToRgb()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "cmyk.jpg");
        var targetPath = Path.Combine(_tempDirectory, "cmyk.dds");
        await File.WriteAllBytesAsync(sourcePath, RgbaPixelsTests.CreateCmykRedJpeg());

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath);

        // Assert
        result.Should().BeTrue();
        using var decoded = new MagickImage(targetPath);
        using var pixels = decoded.GetPixels();
        var color = pixels.GetPixel(0, 0).ToColor()!;
        (color.R >> 8).Should().BeGreaterThan(200);
        (color.G >> 8).Should().BeLessThan(40);
        (color.B >> 8).Should().BeLessThan(40);
    }

    private static void WriteSolidImage(string path, int width, int height, MagickColor color, MagickFormat format)
    {
        using var image = new MagickImage(color, (uint)width, (uint)height);
        if (color.A == ushort.MaxValue)
        {
            image.Alpha(AlphaOption.Off);
        }

        image.Write(path, format);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    public async Task ConvertImageAsync_WithNonPositiveResize_ReturnsFalse(int size)
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.bmp");
        var targetPath = Path.Combine(_tempDirectory, "test.tga");
        WriteSolidImage(sourcePath, 2, 2, MagickColors.Red, MagickFormat.Bmp3);

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath, new Dictionary<string, object> { ["resize"] = size });

        // Assert
        result.Should().BeFalse();
    }

    private static int ReadBmpBitsPerPixel(string bmpPath)
    {
        var bytes = File.ReadAllBytes(bmpPath);
        return BitConverter.ToUInt16(bytes, 28);
    }

    private static string ReadDdsFourCC(string ddsPath)
    {
        var bytes = File.ReadAllBytes(ddsPath);
        bytes.Length.Should().BeGreaterThan(128);
        return Encoding.ASCII.GetString(bytes, 84, 4);
    }
}
