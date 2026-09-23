using FluentAssertions;
using GenHub.Core.Services.Tools.Checksum;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Services;

/// <summary>
/// Unit tests for <see cref="SageVirtualFileSystem"/> verifying case-insensitive resolution.
/// </summary>
public sealed class SageVirtualFileSystemTests : IDisposable
{
    private readonly string _tempRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="SageVirtualFileSystemTests"/> class.
    /// </summary>
    public SageVirtualFileSystemTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHub_VfsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>
    /// Cleans up the temporary directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// Tests that FilesUnder discovers files even when the directory casing on disk differs from the query casing.
    /// </summary>
    [Fact]
    public void FilesUnder_WithDifferentDirectoryCasing_DiscoversLooseIniFiles()
    {
        // Arrange: create directory structure with different casing on disk
        var diskDir = Path.Combine(_tempRoot, "data", "ini", "mappedimages");
        Directory.CreateDirectory(diskDir);
        var testIniPath = Path.Combine(diskDir, "custom_images.ini");
        File.WriteAllText(testIniPath, "MappedImage Sample\nEnd\n");

        var vfs = new SageVirtualFileSystem(_tempRoot, isZeroHour: true, logger: Mock.Of<ILogger>());

        // Act: query with SAGE standard casing "Data\\INI\\MappedImages"
        var files = vfs.FilesUnder("Data\\INI\\MappedImages");

        // Assert
        files.Should().NotBeEmpty();
        files.Should().Contain(f => f.EndsWith("custom_images.ini", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Tests that Read reads loose files case-insensitively.
    /// </summary>
    [Fact]
    public void Read_WithCaseMismatch_ReturnsFileBytes()
    {
        // Arrange
        var diskDir = Path.Combine(_tempRoot, "art", "textures");
        Directory.CreateDirectory(diskDir);
        var diskFile = Path.Combine(diskDir, "sampletexture.tga");
        byte[] expectedBytes = [0x01, 0x02, 0x03, 0x04];
        File.WriteAllBytes(diskFile, expectedBytes);

        var vfs = new SageVirtualFileSystem(_tempRoot, isZeroHour: true, logger: Mock.Of<ILogger>());

        // Act: query with mixed case
        var readBytes = vfs.Read("Art\\Textures\\SampleTexture.tga");

        // Assert
        readBytes.Should().NotBeNull();
        readBytes.Should().Equal(expectedBytes);
    }

    /// <summary>
    /// Tests that a tier band floor excludes lower-tier matches.
    /// </summary>
    [Fact]
    public void ReadInTierBand_WithMinTier_ExcludesLowerTiers()
    {
        // Arrange: base file only exists at the base game tier
        var baseDir = Path.Combine(_tempRoot, "Base");
        var baseFileDir = Path.Combine(baseDir, "Data");
        Directory.CreateDirectory(baseFileDir);
        File.WriteAllBytes(Path.Combine(baseFileDir, "Shared.ini"), [0x01]);

        var modDir = Path.Combine(_tempRoot, "Mod");
        Directory.CreateDirectory(modDir);

        var vfs = new SageVirtualFileSystem(baseDir, isZeroHour: false, logger: Mock.Of<ILogger>());
        vfs.AddMod(modDir);

        // Act
        var floored = vfs.ReadInTierBand("Data\\Shared.ini", SageFileTier.Mod, SageFileTier.LinkedAsset);
        var unbanded = vfs.Read("Data\\Shared.ini");

        // Assert
        floored.Should().BeNull();
        unbanded.Should().Equal([0x01]);
    }

    /// <summary>
    /// Tests that a tier band ceiling excludes higher-tier matches.
    /// </summary>
    [Fact]
    public void ReadInTierBand_WithMaxTier_ExcludesHigherTiers()
    {
        // Arrange: same relative path exists in base and mod layers
        var baseDir = Path.Combine(_tempRoot, "BaseOnly");
        var baseFileDir = Path.Combine(baseDir, "Data");
        Directory.CreateDirectory(baseFileDir);
        File.WriteAllBytes(Path.Combine(baseFileDir, "Shared.ini"), [0x01]);

        var modDir = Path.Combine(_tempRoot, "ModOnly");
        var modFileDir = Path.Combine(modDir, "Data");
        Directory.CreateDirectory(modFileDir);
        File.WriteAllBytes(Path.Combine(modFileDir, "Shared.ini"), [0x02]);

        var vfs = new SageVirtualFileSystem(baseDir, isZeroHour: false, logger: Mock.Of<ILogger>());
        vfs.AddMod(modDir);

        // Act
        var capped = vfs.ReadInTierBand("Data\\Shared.ini", SageFileTier.BaseGame, SageFileTier.BaseGame);
        var unbanded = vfs.Read("Data\\Shared.ini");

        // Assert
        capped.Should().Equal([0x01]);
        unbanded.Should().Equal([0x02]);
    }

    /// <summary>
    /// Tests that indexing a loose file with the same name from both a mod root and a linked asset root
    /// prioritizes the higher tier (LinkedAsset over Mod) regardless of addition order.
    /// </summary>
    /// <param name="addModFirst">True if mod directory is registered before linked asset directory.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TryReadModLooseFileByName_WhenIndexedInModAndLinkedAsset_HigherTierWins(bool addModFirst)
    {
        // Arrange: create mod and linked asset directories with same file name
        var modDir = Path.Combine(_tempRoot, "ModRoot_" + addModFirst);
        Directory.CreateDirectory(modDir);
        var modFile = Path.Combine(modDir, "texture.tga");
        File.WriteAllBytes(modFile, [0xAA]);

        var linkedDir = Path.Combine(_tempRoot, "LinkedRoot_" + addModFirst);
        Directory.CreateDirectory(linkedDir);
        var linkedFile = Path.Combine(linkedDir, "texture.tga");
        File.WriteAllBytes(linkedFile, [0xBB]);

        var vfs = new SageVirtualFileSystem(_tempRoot, isZeroHour: true, logger: Mock.Of<ILogger>());
        if (addModFirst)
        {
            vfs.AddMod(modDir);
            vfs.AddLinkedAsset(linkedDir);
        }
        else
        {
            vfs.AddLinkedAsset(linkedDir);
            vfs.AddMod(modDir);
        }

        // Act: lookup loose file by name
        var readBytes = vfs.TryReadModLooseFileByName("texture.tga");

        // Assert: LinkedAsset (0xBB) wins over Mod (0xAA) regardless of registration order
        readBytes.Should().NotBeNull();
        readBytes.Should().Equal([0xBB]);
    }
}
