using System.Linq;
using GenHub.Core.Constants;
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

        var definitions = WndMappedImage.ParseDefinitions(File.ReadAllText(result.Data.DefinitionsPath)).ToDictionary(d => d.Name);
        definitions.Should().ContainKey("My_Button");
        var def = definitions["My_Button"];
        def.Texture.Should().Be("My_Button.tga");
        def.Right.Should().Be(64);
        def.Bottom.Should().Be(32);
        def.TextureWidth.Should().Be(64);
        def.TextureHeight.Should().Be(32);
    }

    /// <summary>
    /// Tests that a DDS source preserves its extension and registers a definition.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_DdsSource_PreservesExtension()
    {
        // Arrange
        var source = WriteSource("Terrain.dds", 128, 128, MagickFormat.Dds);

        // Act
        var result = await _service.ImportTextureAsync(source, _projectDir);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.TextureFileName.Should().Be("Terrain.dds");
        result.Data.TexturePath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    /// <summary>
    /// Tests that importing with an explicit mapped name uses that name instead of the file stem.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_ExplicitMappedName_UsesExplicitName()
    {
        // Arrange
        var source = WriteSource("raw_export_001.png", 16, 16, MagickFormat.Png);

        // Act
        var result = await _service.ImportTextureAsync(source, _projectDir, "ControlBarLogo");

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.MappedName.Should().Be("ControlBarLogo");
        result.Data.TextureFileName.Should().Be("ControlBarLogo.tga");
    }

    /// <summary>
    /// Tests that importing an unsupported extension fails gracefully.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_UnsupportedExtension_Fails()
    {
        // Arrange
        var source = Path.Combine(_sourceDir, "document.txt");
        File.WriteAllText(source, "not an image");

        // Act
        var result = await _service.ImportTextureAsync(source, _projectDir);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported texture format");
    }

    /// <summary>
    /// Tests that importing a non-existent file fails gracefully.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_FileNotFound_Fails()
    {
        // Act
        var result = await _service.ImportTextureAsync(Path.Combine(_sourceDir, "ghost.png"), _projectDir);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    /// <summary>
    /// Tests that importing textures that exceed the maximum dimension fails.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_OversizedImage_Fails()
    {
        // Arrange
        var source = WriteSource("Huge.png", 5000, 100, MagickFormat.Png);

        // Act
        var result = await _service.ImportTextureAsync(source, _projectDir);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("exceed maximum permitted dimension");
    }

    /// <summary>
    /// Tests that re-importing the same texture updates the definition rather than duplicating it.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_DuplicateImport_UpdatesDefinitionInPlace()
    {
        // Arrange
        var source1 = WriteSource("Icon.png", 32, 32, MagickFormat.Png);
        await _service.ImportTextureAsync(source1, _projectDir);

        var source2 = WriteSource("Icon.png", 64, 64, MagickFormat.Png);

        // Act
        var result = await _service.ImportTextureAsync(source2, _projectDir);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Width.Should().Be(64);

        var content = File.ReadAllText(result.Data.DefinitionsPath);
        var matches = System.Text.RegularExpressions.Regex.Matches(content, @"MappedImage\s+Icon\b");
        matches.Count.Should().Be(1);

        var definitions = WndMappedImage.ParseDefinitions(content).ToDictionary(d => d.Name);
        definitions["Icon"].Right.Should().Be(64);
    }

    /// <summary>
    /// Tests that multiple distinct imports append correctly to the same definitions file.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_MultipleDistinctImports_AllPresentInDefinitions()
    {
        // Arrange
        var s1 = WriteSource("First.png", 16, 16, MagickFormat.Png);
        var s2 = WriteSource("Second.png", 32, 32, MagickFormat.Png);

        // Act
        await _service.ImportTextureAsync(s1, _projectDir);
        var r2 = await _service.ImportTextureAsync(s2, _projectDir);

        // Assert
        r2.Success.Should().BeTrue();
        var definitions = WndMappedImage.ParseDefinitions(File.ReadAllText(r2.Data!.DefinitionsPath)).ToDictionary(d => d.Name);
        definitions.Should().ContainKey("First");
        definitions.Should().ContainKey("Second");
    }

    /// <summary>
    /// Tests that imported art can be resolved by <see cref="WndImageAssetService.GetImagesAsync"/>.
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

    /// <summary>
    /// Tests that raw PNG bytes are imported, converted to TGA, and registered as a mapped image.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureFromBytesAsync_PngBytes_NormalizesToTgaAndRegistersDefinition()
    {
        // Arrange
        var bytes = CreateImageBytes(48, 24, MagickFormat.Png);

        // Act
        var result = await _service.ImportTextureFromBytesAsync(bytes, _projectDir, "PastedButton");

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.MappedName.Should().Be("PastedButton");
        result.Data.TextureFileName.Should().Be("PastedButton.tga");
        result.Data.Width.Should().Be(48);
        result.Data.Height.Should().Be(24);
        File.Exists(result.Data.TexturePath).Should().BeTrue();
        File.Exists(result.Data.DefinitionsPath).Should().BeTrue();

        var definitions = WndMappedImage.ParseDefinitions(File.ReadAllText(result.Data.DefinitionsPath)).ToDictionary(d => d.Name);
        definitions.Should().ContainKey("PastedButton");
        var def = definitions["PastedButton"];
        def.Texture.Should().Be("PastedButton.tga");
        def.Right.Should().Be(48);
        def.Bottom.Should().Be(24);
        def.TextureWidth.Should().Be(64);
        def.TextureHeight.Should().Be(32);
    }

    /// <summary>
    /// Tests that raw DIB bytes (as placed on Windows clipboard) are decoded, converted to TGA, and registered.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureFromBytesAsync_DibBytes_NormalizesToTgaAndRegistersDefinition()
    {
        // Arrange
        var dibBytes = CreateDibBytes(32, 16);

        // Act
        var result = await _service.ImportTextureFromBytesAsync(dibBytes, _projectDir, "ClipboardDib");

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.MappedName.Should().Be("ClipboardDib");
        result.Data.TextureFileName.Should().Be("ClipboardDib.tga");
        result.Data.Width.Should().Be(32);
        result.Data.Height.Should().Be(16);
        File.Exists(result.Data.TexturePath).Should().BeTrue();
    }

    /// <summary>
    /// Tests that empty byte array returns failure.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureFromBytesAsync_EmptyBytes_Fails()
    {
        // Act
        var result = await _service.ImportTextureFromBytesAsync(Array.Empty<byte>(), _projectDir, "EmptyBytes");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    /// <summary>
    /// Tests that non-power-of-two source images are padded to next power-of-two dimensions.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_NonPowerOfTwo_PadsCanvasToNextPowerOfTwo()
    {
        // Arrange (100x75 -> next POT is 128x128)
        var source = WriteSource("CustomLogo.png", 100, 75, MagickFormat.Png);

        // Act
        var result = await _service.ImportTextureAsync(source, _projectDir);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Width.Should().Be(100);
        result.Data.Height.Should().Be(75);

        using var textureImage = new MagickImage(result.Data.TexturePath);
        textureImage.Width.Should().Be(128);
        textureImage.Height.Should().Be(128);

        var definitions = WndMappedImage.ParseDefinitions(File.ReadAllText(result.Data.DefinitionsPath)).ToDictionary(d => d.Name);
        var def = definitions["CustomLogo"];
        def.TextureWidth.Should().Be(128);
        def.TextureHeight.Should().Be(128);
        def.Right.Should().Be(100);
        def.Bottom.Should().Be(75);
    }

    /// <summary>
    /// Tests that importing in a multi-language project synchronizes the texture to sibling language folders.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportTextureAsync_MultiLanguageProject_SyncsToSiblingLanguageFolders()
    {
        // Arrange
        var englishDir = Path.Combine(_projectDir, ModBuilderConstants.GameFilesEditedDir, "Data", "English", "Art", "Textures");
        var russianDir = Path.Combine(_projectDir, ModBuilderConstants.GameFilesEditedDir, "Data", "Russian", "Art", "Textures");
        Directory.CreateDirectory(englishDir);
        Directory.CreateDirectory(russianDir);

        var source = WriteSource("MenuLogo.png", 64, 64, MagickFormat.Png);

        // Act
        var result = await _service.ImportTextureAsync(source, _projectDir);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(englishDir, "MenuLogo.tga")).Should().BeTrue();
        File.Exists(Path.Combine(russianDir, "MenuLogo.tga")).Should().BeTrue();
    }

    private static byte[] CreateImageBytes(uint width, uint height, MagickFormat format)
    {
        using var image = new MagickImage(MagickColors.Crimson, width, height);
        image.Format = format;
        using var ms = new MemoryStream();
        image.Write(ms);
        return ms.ToArray();
    }

    private static byte[] CreateDibBytes(uint width, uint height)
    {
        using var image = new MagickImage(MagickColors.ForestGreen, width, height);
        image.Format = MagickFormat.Bmp;
        using var ms = new MemoryStream();
        image.Write(ms);
        var bmpBytes = ms.ToArray();
        return bmpBytes[14..];
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
