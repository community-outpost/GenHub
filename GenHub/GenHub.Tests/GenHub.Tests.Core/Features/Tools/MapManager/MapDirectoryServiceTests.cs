using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.MapManager;
using GenHub.Features.Tools.MapManager.Services;
using GenHub.Infrastructure.Imaging;
using GenHub.Tests.Core.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
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

        Assert.True(result.Success);
        var newDir = Path.Combine(mapsDir, "NewMap");
        Assert.True(Directory.Exists(newDir));
        Assert.True(File.Exists(Path.Combine(newDir, "NewMap.map")));
        Assert.True(File.Exists(Path.Combine(newDir, "NewMap.tga")));
        Assert.False(Directory.Exists(oldDir));
    }

    /// <summary>
    /// Verifies that RenameMapAsync does not match or overwrite companion files with multi-dot names (e.g. OldMap.backup.tga).
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RenameMapAsync_WithMultiDotCompanionAsset_DoesNotRenameMultiDotFileAsync()
    {
        var mockPathProvider = new Mock<IGamePathProvider>();
        var fakeBasePath = Path.Combine(_tempDirectory, "TestRenameZHMultiDot");
        mockPathProvider
            .Setup(p => p.GetOptionsDirectory(GameType.ZeroHour))
            .Returns(fakeBasePath);

        var mapsDir = Path.Combine(fakeBasePath, MapManagerConstants.MapsSubdirectoryName);
        var oldDir = Path.Combine(mapsDir, "OldMap");
        Directory.CreateDirectory(oldDir);

        var oldMapFile = Path.Combine(oldDir, "OldMap.map");
        var oldTgaFile = Path.Combine(oldDir, "OldMap.tga");
        var oldBackupFile = Path.Combine(oldDir, "OldMap.backup.tga");
        await File.WriteAllTextAsync(oldMapFile, "map content");
        await File.WriteAllTextAsync(oldTgaFile, "tga content");
        await File.WriteAllTextAsync(oldBackupFile, "backup content");

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

        Assert.True(result.Success);
        var newDir = Path.Combine(mapsDir, "NewMap");
        Assert.True(Directory.Exists(newDir));
        Assert.True(File.Exists(Path.Combine(newDir, "NewMap.map")));
        Assert.True(File.Exists(Path.Combine(newDir, "NewMap.tga")));
        Assert.True(File.Exists(Path.Combine(newDir, "OldMap.backup.tga")));
        Assert.False(Directory.Exists(oldDir));
    }

    /// <summary>
    /// Verifies that RenameMapAsync renames companion asset when map file and directory have mixed casing.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RenameMapAsync_WithMixedCaseDirectoryAndMapName_RenamesCompanionAssetAsync()
    {
        var mockPathProvider = new Mock<IGamePathProvider>();
        var fakeBasePath = Path.Combine(_tempDirectory, "TestRenameZHMixedCase");
        mockPathProvider
            .Setup(p => p.GetOptionsDirectory(GameType.ZeroHour))
            .Returns(fakeBasePath);

        var mapsDir = Path.Combine(fakeBasePath, MapManagerConstants.MapsSubdirectoryName);
        var oldDir = Path.Combine(mapsDir, "OldMap");
        Directory.CreateDirectory(oldDir);

        var oldMapFile = Path.Combine(oldDir, "oldmap.map");
        var oldTgaFile = Path.Combine(oldDir, "OldMap.tga");
        await File.WriteAllTextAsync(oldMapFile, "map content");
        await File.WriteAllTextAsync(oldTgaFile, "tga content");

        var service = new MapDirectoryService(
            _mapNameParser,
            NullLogger<MapDirectoryService>.Instance,
            pathProvider: mockPathProvider.Object);

        var map = new MapFile
        {
            FileName = "oldmap.map",
            FullPath = oldMapFile,
            DirectoryName = "OldMap",
            IsDirectory = true,
            GameType = GameType.ZeroHour,
            SizeBytes = 100,
            LastModified = DateTime.UtcNow,
        };

        var result = await service.RenameMapAsync(map, "NewMap");

        Assert.True(result.Success);
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
                ReadOnlyFolderFixtures.RestoreWritable(_tempDirectory);
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

    /// <summary>
    /// Verifies that GetMapsAsync lists the .wak, map.ini and map.str companions as map assets so export keeps them.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetMapsAsync_WhenMapFolderHasWakIniAndStr_ListsThemAsAssetsAsync()
    {
        var mockPathProvider = new Mock<IGamePathProvider>();
        var fakeBasePath = Path.Combine(_tempDirectory, "TestZHAssets");
        mockPathProvider
            .Setup(p => p.GetOptionsDirectory(GameType.ZeroHour))
            .Returns(fakeBasePath);

        var mapDir = Path.Combine(fakeBasePath, MapManagerConstants.MapsSubdirectoryName, "Vendetta");
        Directory.CreateDirectory(mapDir);
        foreach (var name in new[] { "Vendetta.map", "Vendetta.tga", "Vendetta.wak", "map.ini", "map.str" })
        {
            await File.WriteAllTextAsync(Path.Combine(mapDir, name), name);
        }

        var service = new MapDirectoryService(
            _mapNameParser,
            NullLogger<MapDirectoryService>.Instance,
            pathProvider: mockPathProvider.Object);

        var results = await service.GetMapsAsync(GameType.ZeroHour);

        var map = Assert.Single(results);
        Assert.Equal(
            ["Vendetta.tga", "Vendetta.wak", "map.ini", "map.str"],
            map.AssetFiles.Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Verifies that deleting a map whose folder was made read-only outside GenHub removes the folder
    /// and leaves a read-only sibling folder untouched.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteMapsAsync_WithReadOnlyMapFolder_DeletesFolderAndLeavesSiblingReadOnlyAsync()
    {
        var (service, mapsDir) = CreateServiceWithMapsDirectory("TestDeleteReadOnly");
        var map = await CreateDirectoryMapAsync(mapsDir, "LockedMap");
        var sibling = await CreateDirectoryMapAsync(mapsDir, "OtherMap");
        ReadOnlyFolderFixtures.MakeReadOnly(Path.Combine(mapsDir, "LockedMap"));
        ReadOnlyFolderFixtures.MakeReadOnly(Path.Combine(mapsDir, "OtherMap"));

        var result = await service.DeleteMapsAsync([map]);

        Assert.True(result.Success, result.FirstError);
        Assert.False(Directory.Exists(Path.Combine(mapsDir, "LockedMap")));
        Assert.True(File.Exists(sibling.FullPath));
        Assert.True(ReadOnlyFolderFixtures.IsReadOnly(Path.Combine(mapsDir, "OtherMap")));
        Assert.True(ReadOnlyFolderFixtures.IsReadOnly(sibling.FullPath));
    }

    /// <summary>
    /// Verifies that renaming a map whose folder and files were made read-only outside GenHub renames the
    /// folder, the .map file and its companion assets.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RenameMapAsync_WithReadOnlyMapFolder_RenamesFolderMapAndCompanionAsync()
    {
        var (service, mapsDir) = CreateServiceWithMapsDirectory("TestRenameReadOnly");
        var map = await CreateDirectoryMapAsync(mapsDir, "LockedMap");
        ReadOnlyFolderFixtures.MakeReadOnly(Path.Combine(mapsDir, "LockedMap"));

        var result = await service.RenameMapAsync(map, "OpenMap");

        Assert.True(result.Success, result.FirstError);
        var newDir = Path.Combine(mapsDir, "OpenMap");
        Assert.True(File.Exists(Path.Combine(newDir, "OpenMap.map")));
        Assert.True(File.Exists(Path.Combine(newDir, "OpenMap.tga")));
        Assert.True(File.Exists(Path.Combine(newDir, "OpenMap.ini")));
        Assert.False(Directory.Exists(Path.Combine(mapsDir, "LockedMap")));
    }

    /// <summary>
    /// Verifies that a map folder whose permissions cannot be changed is reported by path instead of a bare failure,
    /// and that the folder is left intact.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteMapsAsync_WhenFolderCannotBeMadeWritable_ReturnsErrorNamingFolderAsync()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var (service, mapsDir) = CreateServiceWithMapsDirectory("TestDeleteImmutable");
        var map = await CreateDirectoryMapAsync(mapsDir, "ImmutableMap");
        var mapDir = Path.Combine(mapsDir, "ImmutableMap");
        ReadOnlyFolderFixtures.MakeReadOnly(mapDir);
        ReadOnlyFolderFixtures.LockImmutable(mapDir);

        var result = await service.DeleteMapsAsync([map]);

        Assert.False(result.Success);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, MapManagerConstants.FolderNotWritableFallbackMessage, mapDir), result.FirstError);
        Assert.True(File.Exists(map.FullPath));
    }

    /// <summary>
    /// Verifies that a rename blocked by a map folder whose permissions cannot be changed reports the folder by path.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RenameMapAsync_WhenFolderCannotBeMadeWritable_ReturnsErrorNamingFolderAsync()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var (service, mapsDir) = CreateServiceWithMapsDirectory("TestRenameImmutable");
        var map = await CreateDirectoryMapAsync(mapsDir, "ImmutableMap");
        var mapDir = Path.Combine(mapsDir, "ImmutableMap");
        ReadOnlyFolderFixtures.MakeReadOnly(mapDir);
        ReadOnlyFolderFixtures.LockImmutable(mapDir);

        var result = await service.RenameMapAsync(map, "OpenMap");

        Assert.False(result.Success);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, MapManagerConstants.FolderNotWritableFallbackMessage, mapDir), result.FirstError);
        Assert.True(File.Exists(map.FullPath));
        Assert.False(Directory.Exists(Path.Combine(mapsDir, "OpenMap")));
    }

    /// <summary>
    /// Verifies that the folder error is taken from the localization service when one is supplied.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteMapsAsync_WhenFolderCannotBeMadeWritable_UsesLocalizedMessageAsync()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var localization = new Mock<ILocalizationService>();
        string? localized = "localized folder error";
        localization
            .Setup(l => l.TryGetString(MapManagerConstants.FolderNotWritableMessageKey, out localized, It.IsAny<object?[]>()))
            .Returns(true);
        var (service, mapsDir) = CreateServiceWithMapsDirectory("TestDeleteLocalized", localization.Object);
        var map = await CreateDirectoryMapAsync(mapsDir, "ImmutableMap");
        var mapDir = Path.Combine(mapsDir, "ImmutableMap");
        ReadOnlyFolderFixtures.MakeReadOnly(mapDir);
        ReadOnlyFolderFixtures.LockImmutable(mapDir);

        var result = await service.DeleteMapsAsync([map]);

        Assert.False(result.Success);
        Assert.Equal("localized folder error", result.FirstError);
    }

    /// <summary>Cancellation between deletions is propagated, leaving later maps untouched.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task DeleteMapsAsync_CancelledBetweenMaps_DoesNotReportSuccessAsync()
    {
        var (service, mapsDir) = CreateServiceWithMapsDirectory("CancelDelete");
        var first = await CreateDirectoryMapAsync(mapsDir, "First");
        var second = await CreateDirectoryMapAsync(mapsDir, "Second");
        using var cancellation = new CancellationTokenSource();

        IEnumerable<MapFile> Maps()
        {
            yield return first;
            cancellation.Cancel();
            yield return second;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DeleteMapsAsync(Maps(), cancellation.Token));
        Assert.False(File.Exists(first.FullPath));
        Assert.True(File.Exists(second.FullPath));
    }

    private static async Task<MapFile> CreateDirectoryMapAsync(string mapsDir, string name)
    {
        var mapDir = Path.Combine(mapsDir, name);
        Directory.CreateDirectory(mapDir);
        var mapPath = Path.Combine(mapDir, name + ".map");
        await File.WriteAllTextAsync(mapPath, "map content");
        await File.WriteAllTextAsync(Path.Combine(mapDir, name + ".tga"), "tga content");
        await File.WriteAllTextAsync(Path.Combine(mapDir, name + ".ini"), "ini content");

        return new MapFile
        {
            FileName = name + ".map",
            FullPath = mapPath,
            DirectoryName = name,
            IsDirectory = true,
            GameType = GameType.ZeroHour,
            SizeBytes = 100,
            LastModified = DateTime.UtcNow,
        };
    }

    private (MapDirectoryService Service, string MapsDir) CreateServiceWithMapsDirectory(
        string baseName,
        ILocalizationService? localizationService = null)
    {
        var basePath = Path.Combine(_tempDirectory, baseName);
        var pathProvider = new Mock<IGamePathProvider>();
        pathProvider.Setup(p => p.GetOptionsDirectory(GameType.ZeroHour)).Returns(basePath);

        var mapsDir = Path.Combine(basePath, MapManagerConstants.MapsSubdirectoryName);
        Directory.CreateDirectory(mapsDir);

        var service = new MapDirectoryService(
            _mapNameParser,
            NullLogger<MapDirectoryService>.Instance,
            pathProvider.Object,
            localizationService);
        return (service, mapsDir);
    }
}
