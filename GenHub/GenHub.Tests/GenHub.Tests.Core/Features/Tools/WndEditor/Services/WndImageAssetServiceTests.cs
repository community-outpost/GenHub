using FluentAssertions;
using GenHub.Features.Tools.WndEditor.Services;
using ImageMagick;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Services;

/// <summary>
/// Unit tests for <see cref="WndImageAssetService"/>.
/// </summary>
public sealed class WndImageAssetServiceTests : IDisposable
{
    private readonly string _gameRoot;
    private readonly WndImageAssetService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="WndImageAssetServiceTests"/> class.
    /// </summary>
    public WndImageAssetServiceTests()
    {
        _gameRoot = Path.Combine(Path.GetTempPath(), "GenHub_WndAssetTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_gameRoot, "Data", "INI", "MappedImages", "TextureSize_512"));
        Directory.CreateDirectory(Path.Combine(_gameRoot, "Art", "Textures"));
        _service = new WndImageAssetService(Mock.Of<ILogger<WndImageAssetService>>());
    }

    /// <summary>
    /// Cleans up the temporary game root.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_gameRoot))
        {
            Directory.Delete(_gameRoot, recursive: true);
        }
    }

    /// <summary>
    /// Tests that a mapped image resolves to its cropped region.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetImagesAsync_KnownImage_ReturnsCrop()
    {
        // Arrange
        WriteMappedImages(
            "MappedImage TestButton\n" +
            "  Texture = TestPage\n" +
            "  Coords = Left:2 Top:1 Right:6 Bottom:3\n" +
            "  Status = NONE\n" +
            "End\n");
        WriteTexture("TestPage.tga", 8, 8);

        // Act
        var result = await _service.GetImagesAsync(["TestButton"], _gameRoot, null, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().ContainKey("TestButton");
        using var decoded = new MagickImage(result.Data!["TestButton"]);
        decoded.Width.Should().Be(4);
        decoded.Height.Should().Be(2);
        decoded.Format.Should().Be(MagickFormat.Png);
    }

    /// <summary>
    /// Tests that rotated images come back with swapped dimensions.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetImagesAsync_RotatedImage_SwapsDimensions()
    {
        // Arrange
        WriteMappedImages(
            "MappedImage PackedIcon\n" +
            "  Texture = TestPage\n" +
            "  Coords = Left:0 Top:0 Right:4 Bottom:2\n" +
            "  Status = ROTATED_90_CLOCKWISE\n" +
            "End\n");
        WriteTexture("TestPage.tga", 8, 8);

        // Act
        var result = await _service.GetImagesAsync(["PackedIcon"], _gameRoot, null, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().ContainKey("PackedIcon");
        using var decoded = new MagickImage(result.Data!["PackedIcon"]);
        decoded.Width.Should().Be(2);
        decoded.Height.Should().Be(4);
    }

    /// <summary>
    /// Tests that unknown and empty image names are absent from a successful result.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetImagesAsync_UnknownNames_AreAbsent()
    {
        // Arrange
        WriteMappedImages(
            "MappedImage Known\n" +
            "  Texture = TestPage\n" +
            "  Coords = Left:0 Top:0 Right:2 Bottom:2\n" +
            "End\n");
        WriteTexture("TestPage.tga", 8, 8);

        // Act
        var result = await _service.GetImagesAsync(["Known", "Missing", "NoImage", "  "], _gameRoot, null, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle();
        result.Data.Should().ContainKey("Known");
    }

    /// <summary>
    /// Tests that a missing game root returns a failure result.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetImagesAsync_MissingRoot_ReturnsFailure()
    {
        // Act
        var result = await _service.GetImagesAsync(["Anything"], Path.Combine(_gameRoot, "NoSuchDir"), null, null);

        // Assert
        result.Success.Should().BeFalse();
    }

    /// <summary>
    /// Tests that mod layer mapped image definitions override base game definitions.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetImagesAsync_ModTakesPrecedenceOverBaseGame()
    {
        // Arrange base
        WriteMappedImages(
            "MappedImage PriorityTest\n" +
            "  Texture = BaseTexture\n" +
            "  Coords = Left:0 Top:0 Right:4 Bottom:4\n" +
            "  Status = NONE\n" +
            "End\n");
        WriteTexture("BaseTexture.tga", 4, 4);

        // Arrange mod
        var modDir = Path.Combine(Path.GetTempPath(), "GenHub_ModAssetTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var modIniDir = Path.Combine(modDir, "Data", "INI", "MappedImages");
            var modArtDir = Path.Combine(modDir, "Art", "Textures");
            Directory.CreateDirectory(modIniDir);
            Directory.CreateDirectory(modArtDir);
            var modIniContent =
                "MappedImage PriorityTest\n" +
                "  Texture = ModTexture\n" +
                "  Coords = Left:0 Top:0 Right:2 Bottom:2\n" +
                "  Status = NONE\n" +
                "End\n";
            File.WriteAllText(Path.Combine(modIniDir, "ModImages.ini"), modIniContent);

            using (var blueImg = new MagickImage(MagickColors.Blue, 2, 2))
            {
                blueImg.Format = MagickFormat.Tga;
                blueImg.Write(Path.Combine(modArtDir, "ModTexture.tga"));
            }

            // Act
            var result = await _service.GetImagesAsync(["PriorityTest"], _gameRoot, null, modDir);

            // Assert
            result.Success.Should().BeTrue();
            result.Data.Should().ContainKey("PriorityTest");
            using var decoded = new MagickImage(result.Data!["PriorityTest"]);
            decoded.Width.Should().Be(2);
            decoded.Height.Should().Be(2);
        }
        finally
        {
            if (Directory.Exists(modDir))
            {
                Directory.Delete(modDir, true);
            }
        }
    }

    /// <summary>
    /// Tests that mod loose textures in arbitrary subdirectories resolve by filename.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetImagesAsync_ModLooseTextureByName_ResolvesDirectly()
    {
        var modDir = Path.Combine(Path.GetTempPath(), "GenHub_LooseAssetTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var deepDir = Path.Combine(modDir, "SubFolder", "DeepAssets");
            var modIniDir = Path.Combine(modDir, "Data", "INI", "MappedImages");
            Directory.CreateDirectory(deepDir);
            Directory.CreateDirectory(modIniDir);

            var looseIniContent =
                "MappedImage LooseBtn\n" +
                "  Texture = LooseTexture\n" +
                "  Coords = Left:0 Top:0 Right:3 Bottom:3\n" +
                "  Status = NONE\n" +
                "End\n";
            File.WriteAllText(Path.Combine(modIniDir, "LooseTest.ini"), looseIniContent);

            using (var greenImg = new MagickImage(MagickColors.Green, 4, 4))
            {
                greenImg.Format = MagickFormat.Tga;
                greenImg.Write(Path.Combine(deepDir, "LooseTexture.tga"));
            }

            // Act
            var result = await _service.GetImagesAsync(["LooseBtn"], _gameRoot, null, modDir);

            // Assert
            result.Success.Should().BeTrue();
            result.Data.Should().ContainKey("LooseBtn");
            using var decoded = new MagickImage(result.Data!["LooseBtn"]);
            decoded.Width.Should().Be(3);
            decoded.Height.Should().Be(3);
        }
        finally
        {
            if (Directory.Exists(modDir))
            {
                Directory.Delete(modDir, true);
            }
        }
    }

    private void WriteMappedImages(string content)
    {
        File.WriteAllText(Path.Combine(_gameRoot, "Data", "INI", "MappedImages", "TextureSize_512", "Test.ini"), content);
    }

    private void WriteTexture(string fileName, uint width, uint height)
    {
        using var image = new MagickImage(MagickColors.Red, width, height);
        image.Format = MagickFormat.Tga;
        image.Write(Path.Combine(_gameRoot, "Art", "Textures", fileName));
    }
}
