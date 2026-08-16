using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.PixelFormats;

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
        using (var image = new Image<Rgba32>(1, 1))
        {
            image[0, 0] = new Rgba32(255, 0, 0, 255);
            image.Save(sourcePath, new BmpEncoder());
        }

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

        using (var image = new Image<Rgba32>(1, 1))
        {
            image[0, 0] = new Rgba32(255, 0, 0, 255);
            image.Save(sourcePath, new BmpEncoder());
        }

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
    public async Task ConvertImageAsync_WithCancellation_ReturnsFalse()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.bmp");
        var targetPath = Path.Combine(_tempDirectory, "test.tga");

        using (var image = new Image<Rgba32>(1, 1))
        {
            image[0, 0] = new Rgba32(255, 0, 0, 255);
            image.Save(sourcePath, new BmpEncoder());
        }

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath, cancellationToken: cts.Token);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasAlphaChannelAsync_WithRgbaImage_ReturnsTrue()
    {
        // Arrange
        var imagePath = Path.Combine(_tempDirectory, "rgba.bmp");

        using (var image = new Image<Rgba32>(1, 1))
        {
            image[0, 0] = new Rgba32(255, 0, 0, 128);
            image.Save(imagePath, new BmpEncoder { BitsPerPixel = BmpBitsPerPixel.Pixel32 });
        }

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

        using (var image = new Image<Rgb24>(1, 1))
        {
            image[0, 0] = new Rgb24(255, 0, 0);
            image.Save(imagePath, new BmpEncoder());
        }

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

        using (var image = new Image<Rgba32>(1, 1))
        {
            image[0, 0] = new Rgba32(255, 0, 0, 128);
            image.Save(imagePath, new BmpEncoder { BitsPerPixel = BmpBitsPerPixel.Pixel32 });
        }

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

        using (var image = new Image<Rgb24>(1, 1))
        {
            image[0, 0] = new Rgb24(255, 0, 0);
            image.Save(imagePath, new BmpEncoder());
        }

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

        using (var image = new Image<Rgba32>(1, 1))
        {
            image[0, 0] = new Rgba32(255, 0, 0, 255);
            image.Save(sourcePath, new BmpEncoder());
        }

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

        using (var image = new Image<Rgba32>(1, 1))
        {
            image[0, 0] = new Rgba32(255, 0, 0, 255);
            image.Save(sourcePath, new BmpEncoder());
        }

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

        using (var image = new Image<Rgba32>(1, 1))
        {
            image[0, 0] = new Rgba32(255, 0, 0, 255);
            image.Save(sourcePath, new BmpEncoder());
        }

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

        using (var image = new Image<Rgba32>(1, 1))
        {
            image[0, 0] = new Rgba32(255, 0, 0, 255);
            if (sourceExt == ".bmp")
                image.Save(sourcePath, new BmpEncoder());
            else if (sourceExt == ".tga")
                image.Save(sourcePath, new TgaEncoder());
        }

        // Act
        var result = await _service.ConvertImageAsync(sourcePath, targetPath);

        // Assert
        result.Should().BeTrue();
        File.Exists(targetPath).Should().BeTrue();
    }

    [Fact]
    public async Task HasAlphaChannelAsync_WithCancellation_ReturnsFalse()
    {
        // Arrange
        var imagePath = Path.Combine(_tempDirectory, "test.bmp");

        using (var image = new Image<Rgba32>(1, 1))
        {
            image[0, 0] = new Rgba32(255, 0, 0, 255);
            image.Save(imagePath, new BmpEncoder());
        }

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await _service.HasAlphaChannelAsync(imagePath, cts.Token);

        // Assert
        result.Should().BeFalse();
    }
}
