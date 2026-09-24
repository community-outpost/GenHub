using FluentAssertions;
using GenHub.Core.Services.Tools.Checksum;
using GenHub.Tests.Core.Features.Tools.WndEditor.TestSupport;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Text;

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

    /// <summary>
    /// Tests that same-tier archives resolve same-path files in mount order: the later-sorted
    /// archive wins, so Zero Hour expansion archives (INIZH.big) override same-path base
    /// archives (INI.big) inside one installation, mirroring the engine.
    /// </summary>
    [Fact]
    public void Read_SamePathInBaseAndExpansionArchives_LaterSortedArchiveWins()
    {
        // Arrange: a Zero Hour root shipping both base and expansion archives
        var zhRoot = Path.Combine(_tempRoot, "ZeroHourRoot");
        Directory.CreateDirectory(zhRoot);
        var baseBytes = Encoding.UTF8.GetBytes("generals-base-gamedata");
        var zhBytes = Encoding.UTF8.GetBytes("zerohour-expansion-gamedata");
        WndTestAssets.CreateBigArchive(
            Path.Combine(zhRoot, "INI.big"),
            ("Data\\INI\\GameData.ini", baseBytes),
            ("Data\\INI\\BaseOnly.ini", baseBytes));
        WndTestAssets.CreateBigArchive(
            Path.Combine(zhRoot, "INIZH.big"),
            ("Data\\INI\\GameData.ini", zhBytes));

        var vfs = new SageVirtualFileSystem(
            zhRoot,
            isZeroHour: true,
            logger: Mock.Of<ILogger>(),
            initialTier: SageFileTier.Expansion);

        // Act
        var resolvedBytes = vfs.Read("Data\\INI\\GameData.ini");
        var resolvedTier = vfs.GetFileTier("Data\\INI\\GameData.ini");
        var baseOrderFound = vfs.TryGetSourceArchiveOrder("Data\\INI\\BaseOnly.ini", out var baseOrder);
        var sharedOrderFound = vfs.TryGetSourceArchiveOrder("Data\\INI\\GameData.ini", out var sharedOrder);

        // Assert: the expansion archive wins the shared path at the expansion tier
        resolvedBytes.Should().NotBeNull();
        resolvedBytes.Should().Equal(zhBytes);
        resolvedTier.Should().Be(SageFileTier.Expansion);
        baseOrderFound.Should().BeTrue();
        sharedOrderFound.Should().BeTrue();
        sharedOrder.Should().BeGreaterThan(baseOrder);
    }

    /// <summary>
    /// Tests that filename-only archive lookups apply the same later-sorted-wins rule
    /// within one tier, so expansion textures beat same-name base textures.
    /// </summary>
    [Fact]
    public void TryReadArchiveFileByName_SameTierDuplicates_LaterSortedArchiveWins()
    {
        // Arrange: same texture filename stored under different paths in base and expansion archives
        var zhRoot = Path.Combine(_tempRoot, "ZeroHourTextures");
        Directory.CreateDirectory(zhRoot);
        byte[] baseTexture = [0x54, 0x47, 0x41, 0x00];
        byte[] zhTexture = [0x54, 0x47, 0x41, 0x01];
        WndTestAssets.CreateBigArchive(
            Path.Combine(zhRoot, "Textures.big"),
            ("Art\\Textures\\Shared.tga", baseTexture));
        WndTestAssets.CreateBigArchive(
            Path.Combine(zhRoot, "TexturesZH.big"),
            ("Data\\Art\\Textures\\Shared.tga", zhTexture));

        var vfs = new SageVirtualFileSystem(
            zhRoot,
            isZeroHour: true,
            logger: Mock.Of<ILogger>(),
            initialTier: SageFileTier.Expansion);

        // Act
        var resolvedBytes = vfs.TryReadArchiveFileByName("Shared.tga");

        // Assert
        resolvedBytes.Should().NotBeNull();
        resolvedBytes.Should().Equal(zhTexture);
    }

    /// <summary>
    /// Tests that loose files keep precedence over same-tier archived files, matching
    /// the engine where loose game-directory files override archive contents.
    /// </summary>
    [Fact]
    public void Read_LooseFileBeatsSameTierArchive()
    {
        // Arrange: same path exists loose and inside an archive of the game root
        var gameRoot = Path.Combine(_tempRoot, "LooseBeatsArchive");
        var looseDir = Path.Combine(gameRoot, "Data", "INI");
        Directory.CreateDirectory(looseDir);
        var looseBytes = Encoding.UTF8.GetBytes("loose-override");
        File.WriteAllBytes(Path.Combine(looseDir, "Shared.ini"), looseBytes);
        WndTestAssets.CreateBigArchive(
            Path.Combine(gameRoot, "M.big"),
            ("Data\\INI\\Shared.ini", Encoding.UTF8.GetBytes("archived")));

        var vfs = new SageVirtualFileSystem(
            gameRoot,
            isZeroHour: false,
            logger: Mock.Of<ILogger>(),
            initialTier: SageFileTier.BaseGame);

        // Act
        var resolvedBytes = vfs.Read("Data\\INI\\Shared.ini");
        var missingOrderFound = vfs.TryGetSourceArchiveOrder("Data\\Missing.ini", out _);

        // Assert
        resolvedBytes.Should().NotBeNull();
        resolvedBytes.Should().Equal(looseBytes);
        missingOrderFound.Should().BeFalse();
    }

    /// <summary>
    /// Tests that a base fallback directory fills gaps only and never overrides
    /// same-tier primary entries.
    /// </summary>
    [Fact]
    public void AddBaseFallback_SameTier_DoesNotOverridePrimary()
    {
        // Arrange: primary and fallback archives share one path; fallback sorts later
        var primaryRoot = Path.Combine(_tempRoot, "Primary");
        var fallbackRoot = Path.Combine(_tempRoot, "Fallback");
        Directory.CreateDirectory(primaryRoot);
        Directory.CreateDirectory(fallbackRoot);
        var primaryBytes = Encoding.UTF8.GetBytes("primary");
        WndTestAssets.CreateBigArchive(
            Path.Combine(primaryRoot, "A_Primary.big"),
            ("Data\\INI\\Shared.ini", primaryBytes));
        WndTestAssets.CreateBigArchive(
            Path.Combine(fallbackRoot, "Z_Fallback.big"),
            ("Data\\INI\\Shared.ini", Encoding.UTF8.GetBytes("fallback")),
            ("Data\\INI\\FallbackOnly.ini", Encoding.UTF8.GetBytes("gap")));

        var vfs = new SageVirtualFileSystem(
            primaryRoot,
            isZeroHour: false,
            logger: Mock.Of<ILogger>(),
            initialTier: SageFileTier.BaseGame);
        vfs.AddBaseFallback(fallbackRoot);

        // Act
        var sharedBytes = vfs.Read("Data\\INI\\Shared.ini");
        var gapBytes = vfs.Read("Data\\INI\\FallbackOnly.ini");

        // Assert
        sharedBytes.Should().NotBeNull();
        sharedBytes.Should().Equal(primaryBytes);
        gapBytes.Should().NotBeNull();
        gapBytes.Should().Equal(Encoding.UTF8.GetBytes("gap"));
    }
}
