using FluentAssertions;
using GenHub.Core.Models.Tools.WndEditor;
using GenHub.Features.Tools.WndEditor.Services;
using ImageMagick;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Services;

/// <summary>
/// Unit tests for <see cref="WndTextureImportService"/>.
/// </summary>
public sealed class WndTextureImportServiceTests : IDisposable
{
    private readonly string _projectDir;
    private readonly string _sourceDir;
    private readonly WndTextureImportService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="WndTextureImportServiceTests"/> class.
    /// </summary>
    public WndTextureImportServiceTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "GenHub_WndImportTests_" + Guid.NewGuid().ToString("N"));
        _projectDir = Path.Combine(root, "Project");
        _sourceDir = Path.Combine(root, "Sources");
        Directory.CreateDirectory(_projectDir);
        Directory.CreateDirectory(_sourceDir);
        _service = new WndTextureImportService(Mock.Of<ILogger<WndTextureImportService>>());
    }

    /// <summary>
    /// Cleans up the temporary directories.
    /// </summary>
    public void Dispose()
    {
        var root = Path.GetDirectoryName(_projectDir);
        if (root != null && Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Tests that a PNG source is normalized to TGA and registered as a full-page mapped image.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_PngSource_NormalizesToTgaAndRegistersDefinition()
    {
        // Arrange
        var source = WriteSource("My Button.png", 64, 32, MagickFormat.Png);

        // Act
        var result = await _service.ImportTextureAsync(source, _projectDir);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.MappedName.Should().Be("My_Button");
        result.Data.TextureFileName.Should().Be("My_Button.tga");
        result.Data.Width.Should().Be(64);
        result.Data.Height.Should().Be(32);
        File.Exists(result.Data.TexturePath).Should().BeTrue();
        File.Exists(result.Data.DefinitionsPath).Should().BeTrue();

        var definitions = WndMappedImage.ParseDefinitions(File.ReadAllText(result.Data.DefinitionsPath));
        definitions.Should().ContainSingle();
        definitions[0].Name.Should().Be("My_Button");
        definitions[0].Texture.Should().Be("My_Button.tga");
        definitions[0].Width.Should().Be(64);
        definitions[0].Height.Should().Be(32);
    }

    /// <summary>
    /// Tests that an explicit mapped name wins over the file stem.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_ExplicitName_UsesProvidedName()
    {
        // Arrange
        var source = WriteSource("whatever.png", 16, 16, MagickFormat.Png);

        // Act
        var result = await _service.ImportTextureAsync(source, _projectDir, "ZHMissingArt");

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.MappedName.Should().Be("ZHMissingArt");
        result.Data.TextureFileName.Should().Be("ZHMissingArt.tga");
    }

    /// <summary>
    /// Tests that re-importing the same name replaces the old block instead of duplicating it.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_SameNameTwice_ReplacesDefinitionBlock()
    {
        // Arrange
        var first = WriteSource("first.png", 16, 16, MagickFormat.Png);
        var second = WriteSource("second.png", 48, 24, MagickFormat.Png);

        // Act
        var firstResult = await _service.ImportTextureAsync(first, _projectDir, "Shared");
        var secondResult = await _service.ImportTextureAsync(second, _projectDir, "Shared");

        // Assert
        firstResult.Success.Should().BeTrue();
        secondResult.Success.Should().BeTrue();
        secondResult.Data.Should().NotBeNull();
        var definitions = WndMappedImage.ParseDefinitions(File.ReadAllText(secondResult.Data!.DefinitionsPath));
        definitions.Should().ContainSingle();
        definitions[0].Width.Should().Be(48);
        definitions[0].Height.Should().Be(24);
    }

    /// <summary>
    /// Tests that DDS sources are copied as-is without conversion.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_DdsSource_CopiesWithoutConversion()
    {
        // Arrange
        var source = WriteSource("Page.dds", 32, 32, MagickFormat.Dds);

        // Act
        var result = await _service.ImportTextureAsync(source, _projectDir);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.TextureFileName.Should().Be("Page.dds");
    }

    /// <summary>
    /// Tests that unsupported formats fail with a descriptive error.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_UnsupportedFormat_ReturnsFailure()
    {
        // Arrange
        var source = Path.Combine(_sourceDir, "notes.txt");
        File.WriteAllText(source, "not a texture");

        // Act
        var result = await _service.ImportTextureAsync(source, _projectDir);

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain(".txt");
    }

    /// <summary>
    /// Tests that a missing source file fails instead of throwing.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_MissingSource_ReturnsFailure()
    {
        // Act
        var result = await _service.ImportTextureAsync(Path.Combine(_sourceDir, "gone.png"), _projectDir);

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("not found");
    }

    /// <summary>
    /// Tests that the imported definition resolves through the preview asset service.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_ThenGetImages_ResolvesImportedArt()
    {
        // Arrange
        var source = WriteSource("RoundTrip.png", 24, 12, MagickFormat.Png);
        var import = await _service.ImportTextureAsync(source, _projectDir);
        import.Success.Should().BeTrue();
        var gameRoot = Path.Combine(Path.GetDirectoryName(_projectDir)!, "GameRoot");
        Directory.CreateDirectory(gameRoot);
        var assets = new WndImageAssetService(Mock.Of<ILogger<WndImageAssetService>>());

        // Act
        var result = await assets.GetImagesAsync(["RoundTrip"], gameRoot, null, _projectDir);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().ContainKey("RoundTrip");
    }

    /// <summary>
    /// Tests that name sanitization strips unusable characters and falls back when empty.
    /// </summary>
    /// <param name="raw">The raw input name.</param>
    /// <param name="expected">The expected sanitized name.</param>
    [Theory]
    [InlineData("Plain", "Plain")]
    [InlineData("My Button!", "My_Button")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("!!!", "ImportedTexture")]
    public void SanitizeMappedName_VariousInputs_Sanitizes(string raw, string expected)
    {
        WndTextureImportService.SanitizeMappedName(raw).Should().Be(expected);
    }

    /// <summary>
    /// Tests that importing a texture when it already exists in a custom project subfolder overwrites it in place.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_ExistingInCustomSubdirectory_OverwritesInPlace()
    {
        // Arrange
        var customSubdir = Path.Combine(_projectDir, "GameFilesEdited", "Data", "English", "Art", "Textures");
        Directory.CreateDirectory(customSubdir);
        var existingTexturePath = Path.Combine(customSubdir, "sclogosuserinterface512_001.tga");
        File.WriteAllBytes(existingTexturePath, [0x00, 0x01]);

        var source = WriteSource("sclogosuserinterface512_001.png", 32, 32, MagickFormat.Png);

        // Act
        var result = await _service.ImportTextureAsync(source, _projectDir);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.TexturePath.Should().Be(existingTexturePath);
        File.ReadAllBytes(existingTexturePath).Length.Should().BeGreaterThan(2);
    }

    /// <summary>
    /// Tests that a converted PNG writes an uncompressed TGA (type 2 true-color) without RLE compression.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_PngSource_WritesUncompressedTga()
    {
        // Arrange
        var source = WriteSource("UncompressedTest.png", 16, 16, MagickFormat.Png);

        // Act
        var result = await _service.ImportTextureAsync(source, _projectDir);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        File.Exists(result.Data!.TexturePath).Should().BeTrue();

        var tgaBytes = File.ReadAllBytes(result.Data.TexturePath);

        // TGA header byte 2 is the image type: 2 indicates uncompressed true-color image
        tgaBytes[2].Should().Be(2);
    }

    private string WriteSource(string fileName, uint width, uint height, MagickFormat format)
    {
        var path = Path.Combine(_sourceDir, fileName);
        using var image = new MagickImage(MagickColors.DodgerBlue, width, height);
        image.Format = format;
        image.Write(path);
        return path;
    }
}
