using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.Common;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.Common;

/// <summary>
/// Unit tests for <see cref="ControlBarPackageProcessor"/>.
/// </summary>
public sealed class ControlBarPackageProcessorTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try
            {
                Directory.Delete(_testDir, recursive: true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    /// <summary>
    /// Verifies that IsControlBarContent detects Control Bar manifests by identifier and name.
    /// </summary>
    [Fact]
    public void IsControlBarContent_WithControlBarManifest_ReturnsTrue()
    {
        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var processor = new ControlBarPackageProcessor(converter, NullLogger<ControlBarPackageProcessor>.Instance);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.103.github.addon.lemoncontrolbar1080p"),
            Name = "Control Bar Pro Lemon Edition ZH (1080p)",
            ContentType = ContentType.Addon,
        };

        var result = processor.IsControlBarContent(_testDir, manifest);

        Assert.True(result);
    }

    /// <summary>
    /// Verifies that GetControlBarVariantSuffix correctly normalizes resolution tokens, including 4K/2160p variants.
    /// </summary>
    /// <param name="variantId">The input variant ID.</param>
    /// <param name="expectedSuffix">The expected normalized variant suffix.</param>
    [Theory]
    [InlineData("1080p", "1080")]
    [InlineData("1440p", "1440")]
    [InlineData("720p", "720")]
    [InlineData("900p", "900")]
    [InlineData("2160p", "4K")]
    [InlineData("2160", "4K")]
    [InlineData("4k", "4K")]
    [InlineData("4K", "4K")]
    public void GetControlBarVariantSuffix_ResolvesExpectedSuffix(string variantId, string expectedSuffix)
    {
        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var processor = new ControlBarPackageProcessor(converter, NullLogger<ControlBarPackageProcessor>.Instance);

        var suffix = processor.GetControlBarVariantSuffix(variantId);

        Assert.Equal(expectedSuffix, suffix);
    }

    /// <summary>
    /// Verifies that nested resolution folder structure (ZH/1080p/BIG/...) is repacked into SAGE BIG archives.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task ProcessAndRepackControlBarAsync_WithNestedVariantStructure_RepacksToBigArchivesAsync()
    {
        // Arrange
        var variantRoot = Path.Combine(_testDir, "ZH", "1080p", "BIG");
        var windowDir = Path.Combine(variantRoot, "Window");
        var artDir = Path.Combine(variantRoot, "Art", "Textures");
        var genToolDir = Path.Combine(variantRoot, "GenTool");

        Directory.CreateDirectory(windowDir);
        Directory.CreateDirectory(artDir);
        Directory.CreateDirectory(genToolDir);

        await File.WriteAllTextAsync(Path.Combine(windowDir, "ControlBarPro.wnd"), "Window data");
        await File.WriteAllTextAsync(Path.Combine(artDir, "test.tga"), "TGA Texture data");
        await File.WriteAllTextAsync(Path.Combine(genToolDir, "fullviewport.dat"), "Viewport data");

        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var processor = new ControlBarPackageProcessor(converter, NullLogger<ControlBarPackageProcessor>.Instance);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.103.github.addon.lemoncontrolbar1080p"),
            Name = "Control Bar Pro Lemon Edition ZH (1080p)",
            ContentType = ContentType.Addon,
        };

        // Act
        var outputFiles = await processor.ProcessAndRepackControlBarAsync(_testDir, manifest);

        // Assert
        Assert.Contains("340_ControlBarProArt1080ZH.big", outputFiles);
        Assert.Contains("340_ControlBarProData1080ZH.big", outputFiles);
        Assert.Contains("340_ControlBarProZH.big", outputFiles);

        Assert.True(File.Exists(Path.Combine(_testDir, "340_ControlBarProArt1080ZH.big")));
        Assert.True(File.Exists(Path.Combine(_testDir, "340_ControlBarProData1080ZH.big")));
        Assert.True(File.Exists(Path.Combine(_testDir, "340_ControlBarProZH.big")));

        // Verify that raw source folder was cleaned up
        Assert.False(Directory.Exists(Path.Combine(_testDir, "ZH")));
    }

    /// <summary>
    /// Verifies that packaging multiple variants against the same directory with cleanupSources = false
    /// preserves sources across variant passes, and CleanupSourceDirectories cleans up afterwards.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task ProcessAndRepackControlBarAsync_MultiVariant_PreservesSourcesUntilExplicitCleanupAsync()
    {
        // Arrange: Setup both 1080p and 1440p variant directories in ZH
        var root1080 = Path.Combine(_testDir, "ZH", "1080p", "BIG");
        var root1440 = Path.Combine(_testDir, "ZH", "1440p", "BIG");

        Directory.CreateDirectory(Path.Combine(root1080, "Window"));
        Directory.CreateDirectory(Path.Combine(root1080, "Art"));
        await File.WriteAllTextAsync(Path.Combine(root1080, "Window", "ControlBarPro.wnd"), "1080 Window data");
        await File.WriteAllTextAsync(Path.Combine(root1080, "Art", "cb1080.tga"), "1080 TGA data");

        Directory.CreateDirectory(Path.Combine(root1440, "Window"));
        Directory.CreateDirectory(Path.Combine(root1440, "Art"));
        await File.WriteAllTextAsync(Path.Combine(root1440, "Window", "ControlBarPro.wnd"), "1440 Window data");
        await File.WriteAllTextAsync(Path.Combine(root1440, "Art", "cb1440.tga"), "1440 TGA data");

        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var processor = new ControlBarPackageProcessor(converter, NullLogger<ControlBarPackageProcessor>.Instance);

        var manifest1080 = new ContentManifest
        {
            Id = ManifestId.Create("1.103.communityoutpost.addon.cb-1080p"),
            Name = "Control Bar Pro (1080p)",
            ContentType = ContentType.Addon,
        };

        var manifest1440 = new ContentManifest
        {
            Id = ManifestId.Create("1.103.communityoutpost.addon.cb-1440p"),
            Name = "Control Bar Pro (1440p)",
            ContentType = ContentType.Addon,
        };

        // Act 1: Pack 1080p without cleanup
        var outputs1080 = await processor.ProcessAndRepackControlBarAsync(
            _testDir, manifest1080, "1080p", cleanupSources: false);

        // Sources must still exist for the next variant
        Assert.True(Directory.Exists(root1440), "1440p source root should not be deleted by 1080p pass");

        // Act 2: Pack 1440p without cleanup
        var outputs1440 = await processor.ProcessAndRepackControlBarAsync(
            _testDir, manifest1440, "1440p", cleanupSources: false);

        // Both variants' BIGs should now be present
        Assert.Contains("340_ControlBarProArt1080ZH.big", outputs1080);
        Assert.Contains("340_ControlBarProData1080ZH.big", outputs1080);
        Assert.Contains("340_ControlBarProArt1440ZH.big", outputs1440);
        Assert.Contains("340_ControlBarProData1440ZH.big", outputs1440);

        Assert.True(File.Exists(Path.Combine(_testDir, "340_ControlBarProArt1080ZH.big")));
        Assert.True(File.Exists(Path.Combine(_testDir, "340_ControlBarProData1080ZH.big")));
        Assert.True(File.Exists(Path.Combine(_testDir, "340_ControlBarProArt1440ZH.big")));
        Assert.True(File.Exists(Path.Combine(_testDir, "340_ControlBarProData1440ZH.big")));

        // Act 3: Explicitly trigger source cleanup
        var allOutputs = outputs1080.Concat(outputs1440).Distinct();
        processor.CleanupSourceDirectories(_testDir, allOutputs);

        // Verify source folder is cleaned up and all output BIGs remain
        Assert.False(Directory.Exists(Path.Combine(_testDir, "ZH")));
        Assert.True(File.Exists(Path.Combine(_testDir, "340_ControlBarProArt1080ZH.big")));
        Assert.True(File.Exists(Path.Combine(_testDir, "340_ControlBarProArt1440ZH.big")));
    }

    /// <summary>
    /// Verifies that flat prebuilt BIG files are identified and retained along with metadata BIG.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task ProcessAndRepackControlBarAsync_WithPrebuiltBigFiles_RetainsMatchingFilesAsync()
    {
        // Arrange
        Directory.CreateDirectory(_testDir);
        await File.WriteAllTextAsync(Path.Combine(_testDir, "340_ControlBarProArt1080ZH.big"), "BIG content");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "340_ControlBarProData1080ZH.big"), "BIG content");

        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var processor = new ControlBarPackageProcessor(converter, NullLogger<ControlBarPackageProcessor>.Instance);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.103.github.addon.lemoncontrolbar1080p"),
            Name = "Control Bar Pro Lemon Edition ZH (1080p)",
            ContentType = ContentType.Addon,
        };

        // Act
        var outputFiles = await processor.ProcessAndRepackControlBarAsync(_testDir, manifest);

        // Assert
        Assert.Contains("340_ControlBarProArt1080ZH.big", outputFiles);
        Assert.Contains("340_ControlBarProData1080ZH.big", outputFiles);
        Assert.Contains("340_ControlBarProZH.big", outputFiles);
    }

    /// <summary>
    /// Verifies that flat prebuilt Lemon Edition BIG files are identified and retained with existing Lemon Edition metadata.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task ProcessAndRepackControlBarAsync_WithLemonEditionPrebuiltBigFiles_RetainsLemonEditionFilesAsync()
    {
        // Arrange
        Directory.CreateDirectory(_testDir);
        await File.WriteAllTextAsync(Path.Combine(_testDir, "340_ControlBarProLemonEditionArt1080ZH.big"), "BIG art content");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "340_ControlBarProLemonEditionData1080ZH.big"), "BIG data content");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "340_ControlBarProLemonEditionZH.big"), "BIG base content");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "339_ControlBarProLemonEditionHideIpZH.big.BAK"), "BAK file");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "ReadMe.txt"), "readme");

        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var processor = new ControlBarPackageProcessor(converter, NullLogger<ControlBarPackageProcessor>.Instance);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.103.github.addon.lemoncontrolbar1080p"),
            Name = "Control Bar Pro Lemon Edition ZH (1080p)",
            ContentType = ContentType.Addon,
        };

        // Act
        var outputFiles = await processor.ProcessAndRepackControlBarAsync(_testDir, manifest);

        // Assert
        Assert.Contains("340_ControlBarProLemonEditionArt1080ZH.big", outputFiles);
        Assert.Contains("340_ControlBarProLemonEditionData1080ZH.big", outputFiles);
        Assert.Contains("340_ControlBarProLemonEditionZH.big", outputFiles);
        Assert.DoesNotContain("340_ControlBarProZH.big", outputFiles);
    }

    /// <summary>
    /// Verifies that generic game folders like ZH without Control Bar markers or assets do not trigger Control Bar classification.
    /// </summary>
    [Fact]
    public void IsControlBarContent_WithGenericGameDirectoryAndNoMarker_ReturnsFalse()
    {
        var zhDir = Path.Combine(_testDir, "ZH");
        Directory.CreateDirectory(zhDir);
        File.WriteAllText(Path.Combine(zhDir, "mod.big"), "some mod content");

        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var processor = new ControlBarPackageProcessor(converter, NullLogger<ControlBarPackageProcessor>.Instance);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.github.mod.somemod"),
            Name = "Regular Mod (ZH)",
            ContentType = ContentType.Mod,
        };

        var result = processor.IsControlBarContent(_testDir, manifest);

        Assert.False(result);
    }

    /// <summary>
    /// Verifies that FindControlBarVariantBigRoot finds a 2160p folder layout when requested variant is 4k.
    /// </summary>
    [Fact]
    public void FindControlBarVariantBigRoot_With2160pFolderAnd4kVariant_FindsRoot()
    {
        var variantDir = Path.Combine(_testDir, "ZH", "2160p", "BIG");
        Directory.CreateDirectory(variantDir);

        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var processor = new ControlBarPackageProcessor(converter, NullLogger<ControlBarPackageProcessor>.Instance);

        var result = processor.FindControlBarVariantBigRoot(_testDir, "4k");

        Assert.Equal(variantDir, result);
    }

    /// <summary>
    /// Verifies that an addon with Control Bar metadata/file but also unrelated assets returns false from IsControlBarContent.
    /// </summary>
    [Fact]
    public void IsControlBarContent_WithControlBarMetadataAndUnrelatedAssets_ReturnsFalse()
    {
        // Arrange
        Directory.CreateDirectory(_testDir);
        File.WriteAllText(Path.Combine(_testDir, "340_ControlBarPro1080ZH.big"), "cb-content");
        File.WriteAllText(Path.Combine(_testDir, "UnrelatedMod.big"), "mod-content");
        Directory.CreateDirectory(Path.Combine(_testDir, "Maps"));

        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var processor = new ControlBarPackageProcessor(converter, NullLogger<ControlBarPackageProcessor>.Instance);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.103.github.addon.mixedcontent"),
            Name = "Control Bar and Map Pack",
            ContentType = ContentType.Addon,
        };

        // Act
        var result = processor.IsControlBarContent(_testDir, manifest);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Verifies that an addon containing unrelated files nested inside an allowed directory (such as Data/INI) returns false from IsControlBarContent.
    /// </summary>
    [Fact]
    public void IsControlBarContent_WithNestedUnrelatedAssetsUnderAllowedDirectory_ReturnsFalse()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_testDir, "Data", "INI"));
        Directory.CreateDirectory(Path.Combine(_testDir, "Window"));
        File.WriteAllText(Path.Combine(_testDir, "Data", "INI", "GameData.ini"), "GameData = Invalidate");
        File.WriteAllText(Path.Combine(_testDir, "Window", "ControlBar.wnd"), "WindowData");

        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var processor = new ControlBarPackageProcessor(converter, NullLogger<ControlBarPackageProcessor>.Instance);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.103.github.addon.custombarwithmod"),
            Name = "Control Bar Pro With Mod Changes",
            ContentType = ContentType.Addon,
        };

        // Act
        var result = processor.IsControlBarContent(_testDir, manifest);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Verifies that flat source directories return the root only for the matching resolution and null for other resolutions.
    /// </summary>
    [Fact]
    public void FindControlBarVariantBigRoot_WithFlatLayout_ReturnsRootOnlyForMatchingVariant()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_testDir, "Window"));
        File.WriteAllText(Path.Combine(_testDir, "Window", "ControlBar.wnd"), "WindowData");

        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var processor = new ControlBarPackageProcessor(converter, NullLogger<ControlBarPackageProcessor>.Instance);

        // Act
        var result1080 = processor.FindControlBarVariantBigRoot(_testDir, "1080p");
        var result720 = processor.FindControlBarVariantBigRoot(_testDir, "720p");
        var result1440 = processor.FindControlBarVariantBigRoot(_testDir, "1440p");

        // Assert
        Assert.Equal(_testDir, result1080);
        Assert.Null(result720);
        Assert.Null(result1440);
    }

    /// <summary>
    /// Verifies that unrelated files under allowed directories (e.g. unknown .dat, unrelated .wnd, nested .json) return false from IsControlBarContent.
    /// </summary>
    /// <param name="relativeFilePath">The relative file path to test.</param>
    [Theory]
    [InlineData("GenTool/telemetry.dat")]
    [InlineData("Window/MainMenu.wnd")]
    [InlineData("Art/notes.txt")]
    [InlineData("Window/layout.json")]
    [InlineData("Art/W3D/other-mod.w3d")]
    [InlineData("Art/Models/unit.dds")]
    [InlineData("Art/Textures/other-mod.dds")]
    [InlineData("Art/Textures/cb_othermod.dds")]
    [InlineData("Window/other-mod.dds")]
    [InlineData("Window/wnd_border.tga")]
    [InlineData("other-mod.dds")]
    public void IsControlBarContent_WithUnrelatedFileBeneathAllowedDirectory_ReturnsFalse(string relativeFilePath)
    {
        // Arrange
        var fullPath = Path.Combine(_testDir, relativeFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "dummy-data");

        // Also add a valid control bar marker
        var validWnd = Path.Combine(_testDir, "Window", "ControlBarPro.wnd");
        Directory.CreateDirectory(Path.GetDirectoryName(validWnd)!);
        File.WriteAllText(validWnd, "ValidWindow");

        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var processor = new ControlBarPackageProcessor(converter, NullLogger<ControlBarPackageProcessor>.Instance);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.103.github.addon.mixedmarkeraddon"),
            Name = "Control Bar With Extra Assets",
            ContentType = ContentType.Addon,
        };

        // Act
        var result = processor.IsControlBarContent(_testDir, manifest);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Verifies that top-level JSON metadata does not block Control Bar detection.
    /// </summary>
    /// <param name="relativeFilePath">The top-level JSON file path.</param>
    [Theory]
    [InlineData("config.json")]
    [InlineData("modinfo.json")]
    public void IsControlBarContent_WithTopLevelJsonMetadata_ReturnsTrue(string relativeFilePath)
    {
        // Arrange
        Directory.CreateDirectory(_testDir);
        var fullPath = Path.Combine(_testDir, relativeFilePath);
        File.WriteAllText(fullPath, "{}");

        var validWnd = Path.Combine(_testDir, "Window", "ControlBarPro.wnd");
        Directory.CreateDirectory(Path.GetDirectoryName(validWnd)!);
        File.WriteAllText(validWnd, "ValidWindow");

        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var processor = new ControlBarPackageProcessor(converter, NullLogger<ControlBarPackageProcessor>.Instance);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.103.github.addon.cbpr"),
            Name = "Control Bar Pro",
            ContentType = ContentType.Addon,
        };

        // Act
        var result = processor.IsControlBarContent(_testDir, manifest);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Verifies that flat layout variant detection prioritizes Control Bar BIG files over documentation files.
    /// </summary>
    [Fact]
    public void FindControlBarVariantBigRoot_WithConflictingFileTokens_PrioritizesBigOverDocFiles()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_testDir, "Window"));
        File.WriteAllText(Path.Combine(_testDir, "Window", "ControlBar.wnd"), "WindowData");
        File.WriteAllText(Path.Combine(_testDir, "readme-720p.txt"), "Readme mentions 720p");
        File.WriteAllText(Path.Combine(_testDir, "340_ControlBarPro1080ZH.big"), "1080 BIG");

        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var processor = new ControlBarPackageProcessor(converter, NullLogger<ControlBarPackageProcessor>.Instance);

        // Act
        var result1080 = processor.FindControlBarVariantBigRoot(_testDir, "1080p");
        var result720 = processor.FindControlBarVariantBigRoot(_testDir, "720p");

        // Assert: 1080p should match because the BIG file has 1080p, even though readme-720p sorts first alphabetically
        Assert.Equal(_testDir, result1080);
        Assert.Null(result720);
    }

    /// <summary>
    /// Verifies that source cleanup deletes unselected variant BIG files while preserving selected outputs and metadata BIG.
    /// </summary>
    [Fact]
    public void CleanupSourceDirectories_WithUnselectedVariantBig_DeletesUnselectedVariantBig()
    {
        // Arrange
        Directory.CreateDirectory(_testDir);
        var selectedBig = "340_ControlBarProArt1080ZH.big";
        var unselectedBig = "340_ControlBarProArt1440ZH.big";
        var metadataBig = "340_ControlBarProZH.big";

        File.WriteAllText(Path.Combine(_testDir, selectedBig), "1080 data");
        File.WriteAllText(Path.Combine(_testDir, unselectedBig), "1440 data");
        File.WriteAllText(Path.Combine(_testDir, metadataBig), "meta data");
        File.WriteAllText(Path.Combine(_testDir, "config.json"), "{}");

        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var processor = new ControlBarPackageProcessor(converter, NullLogger<ControlBarPackageProcessor>.Instance);

        // Act
        processor.CleanupSourceDirectories(_testDir, [selectedBig]);

        // Assert
        Assert.True(File.Exists(Path.Combine(_testDir, selectedBig)));
        Assert.True(File.Exists(Path.Combine(_testDir, metadataBig)));
        Assert.True(File.Exists(Path.Combine(_testDir, "config.json")));
        Assert.False(File.Exists(Path.Combine(_testDir, unselectedBig)));
    }

    /// <summary>
    /// Verifies that supported Art/Textures and nested ZH/1080p/BIG/Art/Textures layouts pass IsControlBarContent.
    /// </summary>
    /// <param name="relativeFilePath">The relative file path to test.</param>
    [Theory]
    [InlineData("Art/Textures/cb.tga")]
    [InlineData("ZH/1080p/BIG/Art/Textures/cb.tga")]
    [InlineData("Window/cb.tga")]
    public void IsControlBarContent_WithValidControlBarTextures_ReturnsTrue(string relativeFilePath)
    {
        // Arrange
        var fullPath = Path.Combine(_testDir, relativeFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "dummy-image-data");

        var validWnd = Path.Combine(_testDir, "Window", "ControlBarPro.wnd");
        Directory.CreateDirectory(Path.GetDirectoryName(validWnd)!);
        File.WriteAllText(validWnd, "ValidWindow");

        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var processor = new ControlBarPackageProcessor(converter, NullLogger<ControlBarPackageProcessor>.Instance);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.103.github.addon.cbpro"),
            Name = "Control Bar Pro",
            ContentType = ContentType.Addon,
        };

        // Act
        var result = processor.IsControlBarContent(_testDir, manifest);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Verifies that ProcessAndRepackControlBarAsync returns empty outputs when requested variant assets do not exist.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task ProcessAndRepackControlBarAsync_MissingVariantAssets_ReturnsEmptyOutputsWithoutMetadataBig()
    {
        // Arrange: Only 1080p exists in extracted directory
        var variant1080Dir = Path.Combine(_testDir, "ZH", "1080p", "BIG", "Window");
        Directory.CreateDirectory(variant1080Dir);
        File.WriteAllText(Path.Combine(variant1080Dir, "ControlBarPro.wnd"), "wnd");

        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var processor = new ControlBarPackageProcessor(converter, NullLogger<ControlBarPackageProcessor>.Instance);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.103.communityoutpost.addon.cbpr"),
            Name = "Control Bar Pro",
            ContentType = ContentType.Addon,
        };

        // Act: Request 1440p which does not exist
        var outputs = await processor.ProcessAndRepackControlBarAsync(_testDir, manifest, "1440p", cleanupSources: false);

        // Assert: Returns empty, metadata BIG is not included
        Assert.Empty(outputs);
    }
}
