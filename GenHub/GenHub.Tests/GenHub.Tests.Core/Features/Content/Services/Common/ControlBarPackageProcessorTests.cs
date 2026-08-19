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
}
