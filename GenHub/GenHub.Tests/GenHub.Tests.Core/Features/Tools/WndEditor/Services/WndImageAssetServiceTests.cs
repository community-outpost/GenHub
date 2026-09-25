using FluentAssertions;
using GenHub.Features.Tools.WndEditor.Services;
using ImageMagick;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <summary>
    /// Tests that when a primary mapped image definition's texture is missing,
    /// resolution falls back to an alternate mapped image definition with an existing texture.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetImagesAsync_PrimaryTextureMissing_FallsBackToAlternateMappedImage()
    {
        // Arrange: primary definition in Test.ini references missing texture
        WriteMappedImages(
            "MappedImage SharedIcon\n" +
            "  Texture = MissingPage\n" +
            "  Coords = Left:0 Top:0 Right:2 Bottom:2\n" +
            "  Status = NONE\n" +
            "End\n");

        // Arrange: alternate definition in TestAlt.ini references available texture
        var altIniPath = Path.Combine(_gameRoot, "Data", "INI", "MappedImages", "TextureSize_512", "TestAlt.ini");
        var altIniContent =
            "MappedImage SharedIcon\n" +
            "  Texture = AlternatePage\n" +
            "  Coords = Left:0 Top:0 Right:4 Bottom:4\n" +
            "  Status = NONE\n" +
            "End\n";
        File.WriteAllText(altIniPath, altIniContent);

        WriteTexture("AlternatePage.tga", 4, 4);

        // Act
        var result = await _service.GetImagesAsync(["SharedIcon"], _gameRoot, null, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().ContainKey("SharedIcon");
        using var decoded = new MagickImage(result.Data!["SharedIcon"]);
        decoded.Width.Should().Be(4);
        decoded.Height.Should().Be(4);
    }

    /// <summary>
    /// Tests that a mod texture with a different file extension wins over a retail texture
    /// whose extension exactly matches the mapped image reference.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetImagesAsync_ModDdsTexture_BeatsRetailTgaTexture()
    {
        // Arrange: retail ships a small Defeated.tga; the mod redefines the image
        // (6x6 crop) but ships the replacement as Defeated.dds.
        WriteTextureAt(_gameRoot, Path.Combine("Data", "English", "Art", "Textures"), "Defeated.tga", MagickColors.Red, 4, 4, MagickFormat.Tga);

        var modDir = Path.Combine(Path.GetTempPath(), "GenHub_ShadowTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(modDir, "Data", "INI", "MappedImages", "HandCreated"));
            var modIniContent =
                "MappedImage Defeated\n" +
                "  Texture = Defeated.tga\n" +
                "  Coords = Left:0 Top:0 Right:6 Bottom:6\n" +
                "  Status = NONE\n" +
                "End\n";
            File.WriteAllText(Path.Combine(modDir, "Data", "INI", "MappedImages", "HandCreated", "Mod.ini"), modIniContent);
            WriteTextureAt(modDir, Path.Combine("Data", "English", "Art", "Textures"), "Defeated.dds", MagickColors.Green, 16, 16, MagickFormat.Dds);

            // Act
            var result = await _service.GetImagesAsync(["Defeated"], _gameRoot, null, modDir);

            // Assert: the 6x6 green mod crop wins, not the 4x4-clamped red retail page.
            result.Success.Should().BeTrue();
            result.Data.Should().ContainKey("Defeated");
            using var decoded = new MagickImage(result.Data!["Defeated"]);
            decoded.Width.Should().Be(6);
            decoded.Height.Should().Be(6);
            PixelAt(decoded, 0, 0).ToString().Should().Be(MagickColors.Green.ToString());
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
    /// Tests that a mod definition referencing a retail-only texture still resolves
    /// through lower-tier fallback.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetImagesAsync_ModDefinitionWithRetailOnlyTexture_FallsBackToRetail()
    {
        // Arrange: retail ships the texture page; the mod only redefines the crop.
        WriteTextureAt(_gameRoot, Path.Combine("Art", "Textures"), "RetailPage.tga", MagickColors.Red, 16, 16, MagickFormat.Tga);

        var modDir = Path.Combine(Path.GetTempPath(), "GenHub_BandFallbackTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(modDir, "Data", "INI", "MappedImages", "HandCreated"));
            var modIniContent =
                "MappedImage RetailCrop\n" +
                "  Texture = RetailPage\n" +
                "  Coords = Left:0 Top:0 Right:4 Bottom:4\n" +
                "  Status = NONE\n" +
                "End\n";
            File.WriteAllText(Path.Combine(modDir, "Data", "INI", "MappedImages", "HandCreated", "Mod.ini"), modIniContent);

            // Act
            var result = await _service.GetImagesAsync(["RetailCrop"], _gameRoot, null, modDir);

            // Assert
            result.Success.Should().BeTrue();
            result.Data.Should().ContainKey("RetailCrop");
            using var decoded = new MagickImage(result.Data!["RetailCrop"]);
            decoded.Width.Should().Be(4);
            decoded.Height.Should().Be(4);
            PixelAt(decoded, 0, 0).ToString().Should().Be(MagickColors.Red.ToString());
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
    /// Tests that when the primary Zero Hour definition's texture is missing,
    /// fallback prefers a Zero Hour alternate over a Generals alternate.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetImagesAsync_PrimaryTextureMissing_PrefersZeroHourAlternateOverGenerals()
    {
        // Arrange: Zero Hour primary points at a missing page; its TextureSize alternate is valid.
        Directory.CreateDirectory(Path.Combine(_gameRoot, "Data", "INI", "MappedImages", "HandCreated"));
        var primaryIniContent =
            "MappedImage AltBtn\n" +
            "  Texture = MissingPage\n" +
            "  Coords = Left:0 Top:0 Right:9 Bottom:9\n" +
            "  Status = NONE\n" +
            "End\n";
        File.WriteAllText(Path.Combine(_gameRoot, "Data", "INI", "MappedImages", "HandCreated", "ZHUI.ini"), primaryIniContent);
        var alternateIniContent =
            "MappedImage AltBtn\n" +
            "  Texture = ZHAltPage\n" +
            "  Coords = Left:0 Top:0 Right:5 Bottom:5\n" +
            "  Status = NONE\n" +
            "End\n";
        File.WriteAllText(Path.Combine(_gameRoot, "Data", "INI", "MappedImages", "TextureSize_512", "ZH512.ini"), alternateIniContent);
        WriteTextureAt(_gameRoot, Path.Combine("Art", "Textures"), "ZHAltPage.tga", MagickColors.Blue, 16, 16, MagickFormat.Tga);

        // Arrange: Generals fallback defines the same image with a valid but smaller crop.
        var generalsRoot = Path.Combine(Path.GetTempPath(), "GenHub_AltTierTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(generalsRoot, "Data", "INI", "MappedImages", "HandCreated"));
            Directory.CreateDirectory(Path.Combine(generalsRoot, "Art", "Textures"));
            var generalsIniContent =
                "MappedImage AltBtn\n" +
                "  Texture = GenPage\n" +
                "  Coords = Left:0 Top:0 Right:2 Bottom:2\n" +
                "  Status = NONE\n" +
                "End\n";
            File.WriteAllText(Path.Combine(generalsRoot, "Data", "INI", "MappedImages", "HandCreated", "AAGenUI.ini"), generalsIniContent);
            WriteTextureAt(generalsRoot, Path.Combine("Art", "Textures"), "GenPage.tga", MagickColors.Red, 16, 16, MagickFormat.Tga);

            // Act
            var result = await _service.GetImagesAsync(["AltBtn"], _gameRoot, generalsRoot, null, null, true);

            // Assert: the 5x5 blue Zero Hour alternate wins over the 2x2 red Generals one.
            result.Success.Should().BeTrue();
            result.Data.Should().ContainKey("AltBtn");
            using var decoded = new MagickImage(result.Data!["AltBtn"]);
            decoded.Width.Should().Be(5);
            decoded.Height.Should().Be(5);
            PixelAt(decoded, 0, 0).ToString().Should().Be(MagickColors.Blue.ToString());
        }
        finally
        {
            if (Directory.Exists(generalsRoot))
            {
                Directory.Delete(generalsRoot, true);
            }
        }
    }

    /// <summary>
    /// Tests that Zero Hour mode never resolves mapped images defined only in the Generals fallback root.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetImagesAsync_ZhMode_IgnoresGeneralsOnlyDefinitions()
    {
        // Arrange: the image exists only in Generals; the Zero Hour root is empty.
        var generalsRoot = Path.Combine(Path.GetTempPath(), "GenHub_ZhStrictTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(generalsRoot, "Data", "INI", "MappedImages", "HandCreated"));
            Directory.CreateDirectory(Path.Combine(generalsRoot, "Art", "Textures"));
            var generalsIniContent =
                "MappedImage GenOnlyBtn\n" +
                "  Texture = GenPage\n" +
                "  Coords = Left:0 Top:0 Right:4 Bottom:4\n" +
                "  Status = NONE\n" +
                "End\n";
            File.WriteAllText(Path.Combine(generalsRoot, "Data", "INI", "MappedImages", "HandCreated", "GenUI.ini"), generalsIniContent);
            WriteTextureAt(generalsRoot, Path.Combine("Art", "Textures"), "GenPage.tga", MagickColors.Red, 16, 16, MagickFormat.Tga);

            // Act
            var result = await _service.GetImagesAsync(["GenOnlyBtn"], _gameRoot, generalsRoot, null, null, true);

            // Assert: strict per-game isolation leaves the Generals-only image unresolved.
            result.Success.Should().BeTrue();
            result.Data.Should().NotContainKey("GenOnlyBtn");
        }
        finally
        {
            if (Directory.Exists(generalsRoot))
            {
                Directory.Delete(generalsRoot, true);
            }
        }
    }

    /// <summary>
    /// Tests that a Zero Hour definition never decodes its texture from the Generals fallback root.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetImagesAsync_ZhMode_ZhDefinitionNeverUsesGeneralsTexture()
    {
        // Arrange: Zero Hour defines the crop but ships no texture page; Generals has the page.
        WriteMappedImages(
            "MappedImage ZhBtn\n" +
            "  Texture = ZhPage\n" +
            "  Coords = Left:0 Top:0 Right:4 Bottom:4\n" +
            "End\n");
        var generalsRoot = Path.Combine(Path.GetTempPath(), "GenHub_ZhStrictTexTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(generalsRoot, "Art", "Textures"));
            WriteTextureAt(generalsRoot, Path.Combine("Art", "Textures"), "ZhPage.tga", MagickColors.Red, 16, 16, MagickFormat.Tga);

            // Act
            var result = await _service.GetImagesAsync(["ZhBtn"], _gameRoot, generalsRoot, null, null, true);

            // Assert: no cross-game texture fallback; the image stays unresolved.
            result.Success.Should().BeTrue();
            result.Data.Should().NotContainKey("ZhBtn");
        }
        finally
        {
            if (Directory.Exists(generalsRoot))
            {
                Directory.Delete(generalsRoot, true);
            }
        }
    }

    /// <summary>
    /// Tests that an extensionless texture reference resolves a PNG file.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetImagesAsync_ExtensionlessTextureReference_ResolvesPngFile()
    {
        // Arrange
        WriteMappedImages(
            "MappedImage PngBtn\n" +
            "  Texture = PngPage\n" +
            "  Coords = Left:0 Top:0 Right:3 Bottom:3\n" +
            "  Status = NONE\n" +
            "End\n");
        WriteTextureAt(_gameRoot, Path.Combine("Art", "Textures"), "PngPage.png", MagickColors.Green, 8, 8, MagickFormat.Png);

        // Act
        var result = await _service.GetImagesAsync(["PngBtn"], _gameRoot, null, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().ContainKey("PngBtn");
        using var decoded = new MagickImage(result.Data!["PngBtn"]);
        decoded.Width.Should().Be(3);
        decoded.Height.Should().Be(3);
    }

    /// <summary>
    /// Tests that known mapped image names are listed in sorted order.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetKnownImageNamesAsync_WithDefinitions_ReturnsSortedNames()
    {
        // Arrange
        WriteMappedImages(
            "MappedImage Zebra\n" +
            "  Texture = TestPage\n" +
            "  Coords = Left:0 Top:0 Right:2 Bottom:2\n" +
            "End\n" +
            "MappedImage apple\n" +
            "  Texture = TestPage\n" +
            "  Coords = Left:0 Top:0 Right:2 Bottom:2\n" +
            "End\n");

        // Act
        var result = await _service.GetKnownImageNamesAsync(_gameRoot, null, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Equal("apple", "Zebra");
    }

    /// <summary>
    /// Tests that a shared Zero Hour / Generals image resolves its texture from the
    /// earliest-mounted archive (Zero Hour) even when the Generals copy lives at an
    /// internal path that speculative candidate probing would reach first.
    /// Mirrors the Steam layout: Zero Hour BIGs at the install root, base Generals
    /// BIGs in a subdirectory mounted later at the same tier.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetImagesAsync_SharedImageAtDifferentArchivePaths_PrefersZeroHourTexture()
    {
        // Arrange: both games define the same image with a bare texture reference.
        const string definition =
            "MappedImage ZhSharedBtn\n" +
            "  Texture = SharedPage.tga\n" +
            "  Coords = Left:0 Top:0 Right:8 Bottom:8\n" +
            "  Status = NONE\n" +
            "End\n";
        WriteBigArchive(
            Path.Combine(_gameRoot, "INIZH.big"),
            new Dictionary<string, byte[]>
            {
                [@"Data\INI\MappedImages\TextureSize_512\ZHUI.ini"] = System.Text.Encoding.UTF8.GetBytes(definition),
            });
        WriteBigArchive(
            Path.Combine(_gameRoot, "TexturesZH.big"),
            new Dictionary<string, byte[]>
            {
                [@"Data\Art\Textures\SharedPage.tga"] = SolidTga(MagickColors.Blue, 8, 8),
            });

        var generalsDir = Path.Combine(_gameRoot, "ZH_Generals");
        WriteBigArchive(
            Path.Combine(generalsDir, "INI.big"),
            new Dictionary<string, byte[]>
            {
                [@"Data\INI\MappedImages\TextureSize_512\BaseUI.ini"] = System.Text.Encoding.UTF8.GetBytes(definition),
            });
        WriteBigArchive(
            Path.Combine(generalsDir, "Textures.big"),
            new Dictionary<string, byte[]>
            {
                [@"Data\English\Textures\SharedPage.tga"] = SolidTga(MagickColors.Orange, 8, 8),
            });

        // Act
        var result = await _service.GetImagesAsync(["ZhSharedBtn"], _gameRoot, null, null, null, true);

        // Assert: the Zero Hour (blue) page wins over the Generals (orange) page.
        result.Success.Should().BeTrue();
        result.Data.Should().ContainKey("ZhSharedBtn");
        using var decoded = new MagickImage(result.Data!["ZhSharedBtn"]);
        decoded.Width.Should().Be(8);
        decoded.Height.Should().Be(8);
        PixelAt(decoded, 0, 0).ToString().Should().Be(MagickColors.Blue.ToString());
    }

    /// <summary>
    /// Tests that a same-path texture conflict between Zero Hour and Generals archives
    /// resolves to the earliest-mounted (Zero Hour) bytes.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetImagesAsync_SharedImageAtSameArchivePath_PrefersZeroHourTexture()
    {
        // Arrange
        const string definition =
            "MappedImage ZhSamePathBtn\n" +
            "  Texture = SamePage\n" +
            "  Coords = Left:0 Top:0 Right:8 Bottom:8\n" +
            "  Status = NONE\n" +
            "End\n";
        WriteBigArchive(
            Path.Combine(_gameRoot, "INIZH.big"),
            new Dictionary<string, byte[]>
            {
                [@"Data\INI\MappedImages\TextureSize_512\ZHUI.ini"] = System.Text.Encoding.UTF8.GetBytes(definition),
            });
        WriteBigArchive(
            Path.Combine(_gameRoot, "TexturesZH.big"),
            new Dictionary<string, byte[]>
            {
                [@"Data\Art\Textures\SamePage.tga"] = SolidTga(MagickColors.Blue, 8, 8),
            });

        var generalsDir = Path.Combine(_gameRoot, "ZH_Generals");
        WriteBigArchive(
            Path.Combine(generalsDir, "Textures.big"),
            new Dictionary<string, byte[]>
            {
                [@"Data\Art\Textures\SamePage.tga"] = SolidTga(MagickColors.Orange, 8, 8),
            });

        // Act
        var result = await _service.GetImagesAsync(["ZhSamePathBtn"], _gameRoot, null, null, null, true);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().ContainKey("ZhSamePathBtn");
        using var decoded = new MagickImage(result.Data!["ZhSamePathBtn"]);
        PixelAt(decoded, 0, 0).ToString().Should().Be(MagickColors.Blue.ToString());
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

    private static void WriteTextureAt(string root, string relativeDirectory, string fileName, MagickColor color, uint width, uint height, MagickFormat format)
    {
        var directory = Path.Combine(root, relativeDirectory);
        Directory.CreateDirectory(directory);
        using var image = new MagickImage(color, width, height);
        image.Format = format;
        image.Write(Path.Combine(directory, fileName));
    }

    private static IMagickColor<ushort> PixelAt(MagickImage image, int x, int y)
    {
        using var pixels = image.GetPixels();
        return pixels.GetPixel(x, y)!.ToColor()!;
    }

    private static byte[] SolidTga(MagickColor color, uint width, uint height)
    {
        using var image = new MagickImage(color, width, height);
        image.Format = MagickFormat.Tga;
        return image.ToByteArray();
    }

    private static void WriteBigArchive(string archivePath, Dictionary<string, byte[]> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        var names = entries.Keys.Select(name => System.Text.Encoding.Latin1.GetBytes(name)).ToList();
        var payloads = entries.Values.ToList();
        var directorySize = names.Sum(name => 8 + name.Length + 1);
        var dataStart = 16 + directorySize;

        using var stream = new FileStream(archivePath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None);
        Span<byte> header = stackalloc byte[16];
        System.Text.Encoding.ASCII.GetBytes("BIGF", header[..4]);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header[4..8], (uint)(dataStart + payloads.Sum(payload => payload.Length)));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header[8..12], (uint)entries.Count);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header[12..16], (uint)dataStart);
        stream.Write(header);

        var offset = dataStart;
        Span<byte> entry = stackalloc byte[8];
        for (var i = 0; i < names.Count; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(entry[..4], (uint)offset);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(entry[4..8], (uint)payloads[i].Length);
            stream.Write(entry);
            stream.Write(names[i]);
            stream.WriteByte(0);
            offset += payloads[i].Length;
        }

        foreach (var payload in payloads)
        {
            stream.Write(payload);
        }
    }
}
