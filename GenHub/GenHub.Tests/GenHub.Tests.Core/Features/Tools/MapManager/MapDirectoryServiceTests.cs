using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.MapManager;
using GenHub.Features.Tools.MapManager.Services;
using GenHub.Infrastructure.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.MapManager;

/// <summary>
/// Unit tests for <see cref="MapDirectoryService"/> path resolution and directory management.
/// </summary>
public sealed class MapDirectoryServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly MapNameParser _mapNameParser;

    /// <summary>
    /// Initializes a new instance of the <see cref="MapDirectoryServiceTests"/> class.
    /// </summary>
    public MapDirectoryServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "GenHub_MapDirTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _mapNameParser = new MapNameParser(NullLogger<MapNameParser>.Instance);
    }

    /// <summary>
    /// Verifies that RenameMapAsync renames both the directory, the .map file, and companion asset files like .tga.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RenameMapAsync_WithDirectoryAndTgaCompanion_RenamesDirectoryMapAndTgaAsync()
    {
        var mockPathProvider = new Mock<IGamePathProvider>();
        var fakeBasePath = Path.Combine(_tempDirectory, "TestRenameZHData");
        mockPathProvider
            .Setup(p => p.GetOptionsDirectory(GameType.ZeroHour))
            .Returns(fakeBasePath);

        var mapsDir = Path.Combine(fakeBasePath, MapManagerConstants.MapsSubdirectoryName);
        var oldDir = Path.Combine(mapsDir, "OldMap");
        Directory.CreateDirectory(oldDir);

        var oldMapFile = Path.Combine(oldDir, "OldMap.map");
        var oldTgaFile = Path.Combine(oldDir, "OldMap.tga");
        await File.WriteAllTextAsync(oldMapFile, "map content");
        await File.WriteAllTextAsync(oldTgaFile, "tga content");

        var service = new MapDirectoryService(
            _mapNameParser,
            NullLogger<MapDirectoryService>.Instance,
            pathProvider: mockPathProvider.Object);

        var map = new MapFile
        {
            FileName = "OldMap.map",
            FullPath = oldMapFile,
            DirectoryName = "OldMap",
            IsDirectory = true,
            GameType = GameType.ZeroHour,
            SizeBytes = 100,
            LastModified = DateTime.UtcNow,
        };

        var result = await service.RenameMapAsync(map, "NewMap");

        Assert.True(result);
        var newDir = Path.Combine(mapsDir, "NewMap");
        Assert.True(Directory.Exists(newDir));
        Assert.True(File.Exists(Path.Combine(newDir, "NewMap.map")));
        Assert.True(File.Exists(Path.Combine(newDir, "NewMap.tga")));
        Assert.False(Directory.Exists(oldDir));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Best effort cleanup in tests
            }
        }
    }

    /// <summary>
    /// Verifies that GetMapDirectory uses IGamePathProvider when supplied.
    /// </summary>
    /// <param name="gameType">The game type to test.</param>
    /// <param name="expectedFolder">The expected directory name.</param>
    [Theory]
    [InlineData(GameType.Generals, "Command and Conquer Generals Data")]
    [InlineData(GameType.ZeroHour, "Command and Conquer Generals Zero Hour Data")]
    public void GetMapDirectory_WithPathProvider_UsesProvidedOptionsDirectory(GameType gameType, string expectedFolder)
    {
        var mockPathProvider = new Mock<IGamePathProvider>();
        var fakeBasePath = Path.Combine(_tempDirectory, expectedFolder);
        mockPathProvider
            .Setup(p => p.GetOptionsDirectory(gameType))
            .Returns(fakeBasePath);

        var service = new MapDirectoryService(
            _mapNameParser,
            NullLogger<MapDirectoryService>.Instance,
            pathProvider: mockPathProvider.Object);

        var result = service.GetMapDirectory(gameType);

        var expectedPath = Path.Combine(fakeBasePath, MapManagerConstants.MapsSubdirectoryName);
        Assert.Equal(expectedPath, result);
        mockPathProvider.Verify(p => p.GetOptionsDirectory(gameType), Times.Once);
    }

    /// <summary>
    /// Verifies that GetMapDirectory falls back to SpecialFolder.MyDocuments when pathProvider is null.
    /// </summary>
    /// <param name="gameType">The game type to test.</param>
    /// <param name="folderName">The expected fallback directory name.</param>
    [Theory]
    [InlineData(GameType.Generals, MapManagerConstants.GeneralsDataDirectoryName)]
    [InlineData(GameType.ZeroHour, MapManagerConstants.ZeroHourDataDirectoryName)]
    public void GetMapDirectory_WithoutPathProvider_FallsBackToMyDocuments(GameType gameType, string folderName)
    {
        var service = new MapDirectoryService(
            _mapNameParser,
            NullLogger<MapDirectoryService>.Instance,
            pathProvider: null);

        var result = service.GetMapDirectory(gameType);

        var expectedPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            folderName,
            MapManagerConstants.MapsSubdirectoryName);

        Assert.Equal(expectedPath, result);
    }

    /// <summary>
    /// Verifies that GetMapDirectory throws ArgumentException for unsupported game types.
    /// </summary>
    /// <param name="invalidGameType">The invalid game type to test.</param>
    [Theory]
    [InlineData(GameType.Unknown)]
    [InlineData((GameType)999)]
    public void GetMapDirectory_WithUnsupportedGameType_ThrowsArgumentException(GameType invalidGameType)
    {
        var mockPathProvider = new Mock<IGamePathProvider>();
        var serviceWithPathProvider = new MapDirectoryService(
            _mapNameParser,
            NullLogger<MapDirectoryService>.Instance,
            pathProvider: mockPathProvider.Object);

        var serviceWithoutPathProvider = new MapDirectoryService(
            _mapNameParser,
            NullLogger<MapDirectoryService>.Instance,
            pathProvider: null);

        Assert.Throws<ArgumentException>(() => serviceWithPathProvider.GetMapDirectory(invalidGameType));
        Assert.Throws<ArgumentException>(() => serviceWithoutPathProvider.GetMapDirectory(invalidGameType));
    }

    /// <summary>
    /// Verifies that EnsureDirectoryExists creates the map directory if it does not exist.
    /// </summary>
    [Fact]
    public void EnsureDirectoryExists_CreatesDirectory()
    {
        var mockPathProvider = new Mock<IGamePathProvider>();
        var fakeBasePath = Path.Combine(_tempDirectory, "CustomData");
        mockPathProvider
            .Setup(p => p.GetOptionsDirectory(GameType.ZeroHour))
            .Returns(fakeBasePath);

        var service = new MapDirectoryService(
            _mapNameParser,
            NullLogger<MapDirectoryService>.Instance,
            pathProvider: mockPathProvider.Object);

        var mapDir = Path.Combine(fakeBasePath, MapManagerConstants.MapsSubdirectoryName);
        Assert.False(Directory.Exists(mapDir));

        service.EnsureDirectoryExists(GameType.ZeroHour);

        Assert.True(Directory.Exists(mapDir));
    }

    /// <summary>
    /// Verifies that GetMapsAsync discovers maps inside directory-based map folders.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetMapsAsync_WhenDirectoryMapExists_DiscoversMapAsync()
    {
        var mockPathProvider = new Mock<IGamePathProvider>();
        var fakeBasePath = Path.Combine(_tempDirectory, "TestZHData");
        mockPathProvider
            .Setup(p => p.GetOptionsDirectory(GameType.ZeroHour))
            .Returns(fakeBasePath);

        var mapsDir = Path.Combine(fakeBasePath, MapManagerConstants.MapsSubdirectoryName);
        var customMapDir = Path.Combine(mapsDir, "Tournament Desert");
        Directory.CreateDirectory(customMapDir);

        var mapFile = Path.Combine(customMapDir, "Tournament Desert.map");
        await File.WriteAllTextAsync(mapFile, "test map content");

        var service = new MapDirectoryService(
            _mapNameParser,
            NullLogger<MapDirectoryService>.Instance,
            pathProvider: mockPathProvider.Object);

        var results = await service.GetMapsAsync(GameType.ZeroHour);

        Assert.Single(results);
        var map = results[0];
        Assert.Equal("Tournament Desert.map", map.FileName);
        Assert.True(map.IsDirectory);
        Assert.Equal(GameType.ZeroHour, map.GameType);
    }
}
