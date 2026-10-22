using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Tools.MapManager;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Core.Services.Tools.Checksum;
using GenHub.Features.Tools.ReplayManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for <see cref="ReplayMapLookupService"/>.
/// </summary>
public sealed class ReplayMapLookupServiceTests : IDisposable
{
    // The engine map CRC rotates left by one and adds each byte: 'A' (0x41) gives 0x41, then (0x41 << 1) + 'B' (0x42) gives 0xC4.
    private const string MapContents = "AB";
    private const uint MapContentsCrc = 0xC4;
    private const string OtherMapContents = "BA";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "genhub_replay_map_lookup_" + Guid.NewGuid().ToString("N"));
    private readonly string _userMapsDirectory;
    private readonly string _installDirectory;
    private readonly Mock<IMapDirectoryService> _mapDirectoryService = new();
    private readonly Mock<IGameInstallationService> _installationService = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayMapLookupServiceTests"/> class.
    /// </summary>
    public ReplayMapLookupServiceTests()
    {
        _userMapsDirectory = Path.Combine(_root, "UserData", "Maps");
        _installDirectory = Path.Combine(_root, "ZeroHour");
        Directory.CreateDirectory(_userMapsDirectory);
        Directory.CreateDirectory(_installDirectory);

        _mapDirectoryService.Setup(m => m.GetMapDirectory(GameType.ZeroHour)).Returns(_userMapsDirectory);
        SetInstallations(CreateZeroHourInstallation());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// Verifies that a user map whose CRC and size match the replay is reported as available.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveMapAvailabilityAsync_UserMapMatchesCrcAndSize_ReportsAvailableAsync()
    {
        WriteStockArchive(("Maps\\Stock Map\\Stock Map.map", "stock"));
        WriteUserMap("Custom Map", MapContents);
        var replay = CreateReplay(MapContentsCrc, (uint)MapContents.Length);

        await ResolveIntoAsync(CreateService(), GameType.ZeroHour, replay);

        Assert.Equal(ReplayMapAvailability.Available, replay.MapAvailability);
    }

    /// <summary>
    /// Verifies that a stock map inside an installation archive whose CRC and size match the replay is reported as available.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveMapAvailabilityAsync_StockArchiveMapMatches_ReportsAvailableAsync()
    {
        WriteStockArchive(("Maps\\Lone Eagle\\Lone Eagle.map", MapContents), ("Data\\INI\\Weapon.ini", MapContents));
        var replay = CreateReplay(MapContentsCrc, (uint)MapContents.Length);

        await ResolveIntoAsync(CreateService(), GameType.ZeroHour, replay);

        Assert.Equal(ReplayMapAvailability.Available, replay.MapAvailability);
    }

    /// <summary>
    /// Verifies that a map with the same size but different content is not treated as the replay map.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveMapAvailabilityAsync_SameSizeDifferentCrc_ReportsNotFoundAsync()
    {
        WriteStockArchive(("Maps\\Stock Map\\Stock Map.map", OtherMapContents));
        WriteUserMap("Custom Map", OtherMapContents);
        var replay = CreateReplay(MapContentsCrc, (uint)MapContents.Length);

        await ResolveIntoAsync(CreateService(), GameType.ZeroHour, replay);

        Assert.Equal(ReplayMapAvailability.NotFound, replay.MapAvailability);
    }

    /// <summary>
    /// Verifies that a map with a matching CRC but a different size is not treated as the replay map.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveMapAvailabilityAsync_MatchingCrcDifferentSize_ReportsNotFoundAsync()
    {
        WriteStockArchive(("Maps\\Stock Map\\Stock Map.map", "stock"));
        WriteUserMap("Custom Map", MapContents);
        var replay = CreateReplay(MapContentsCrc, (uint)MapContents.Length + 1);

        await ResolveIntoAsync(CreateService(), GameType.ZeroHour, replay);

        Assert.Equal(ReplayMapAvailability.NotFound, replay.MapAvailability);
    }

    /// <summary>
    /// Verifies that an unmatched map stays unknown when no stock maps could be read,
    /// so a stock map replay is not reported as missing just because no installation was found.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveMapAvailabilityAsync_NoInstallation_ReportsUnknownForUnmatchedMapAsync()
    {
        SetInstallations();
        WriteUserMap("Custom Map", MapContents);
        var matched = CreateReplay(MapContentsCrc, (uint)MapContents.Length);
        var unmatched = CreateReplay(0xDEADBEEF, 12345);

        await ResolveIntoAsync(CreateService(), GameType.ZeroHour, matched, unmatched);

        Assert.Equal(ReplayMapAvailability.Available, matched.MapAvailability);
        Assert.Equal(ReplayMapAvailability.Unknown, unmatched.MapAvailability);
    }

    /// <summary>
    /// Verifies that replays without a map CRC or size stay unknown and do not trigger a map scan.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveMapAvailabilityAsync_ReplayWithoutMapCrcOrSize_ReportsUnknownAsync()
    {
        WriteStockArchive(("Maps\\Stock Map\\Stock Map.map", MapContents));
        var withoutSize = CreateReplay(MapContentsCrc, null);
        var withoutMetadata = new ReplayFile
        {
            FullPath = "/replays/none.rep",
            FileName = "none.rep",
            SizeInBytes = 0,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
        };

        await ResolveIntoAsync(CreateService(), GameType.ZeroHour, withoutSize, withoutMetadata);

        Assert.Equal(ReplayMapAvailability.Unknown, withoutSize.MapAvailability);
        Assert.Equal(ReplayMapAvailability.Unknown, withoutMetadata.MapAvailability);
        _installationService.Verify(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a Generals replay is matched against the Generals installation, not the Zero Hour one.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveMapAvailabilityAsync_GeneralsReplay_UsesGeneralsInstallationAsync()
    {
        var generalsDirectory = Path.Combine(_root, "Generals");
        var generalsUserMaps = Path.Combine(_root, "GeneralsUserData", "Maps");
        Directory.CreateDirectory(generalsDirectory);
        Directory.CreateDirectory(generalsUserMaps);
        _mapDirectoryService.Setup(m => m.GetMapDirectory(GameType.Generals)).Returns(generalsUserMaps);
        WriteStockArchive(("Maps\\Zero Hour Map\\Zero Hour Map.map", MapContents));
        BigArchiveFixture.Write(Path.Combine(generalsDirectory, "Maps.big"), ("Maps\\Generals Map\\Generals Map.map", OtherMapContents));

        var installation = CreateZeroHourInstallation();
        installation.HasGenerals = true;
        installation.GeneralsPath = generalsDirectory;
        SetInstallations(installation);
        var replay = CreateReplay(MapContentsCrc, (uint)MapContents.Length);

        await ResolveIntoAsync(CreateService(), GameType.Generals, replay);

        Assert.Equal(ReplayMapAvailability.NotFound, replay.MapAvailability);
    }

    /// <summary>
    /// Verifies that a user map edited after a scan is checksummed again instead of reusing the cached CRC.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveMapAvailabilityAsync_UserMapChangedBetweenScans_RecomputesCrcAsync()
    {
        WriteStockArchive(("Maps\\Stock Map\\Stock Map.map", "stock"));
        var mapPath = WriteUserMap("Custom Map", MapContents);
        var service = CreateService();
        var first = CreateReplay(MapContentsCrc, (uint)MapContents.Length);
        await ResolveIntoAsync(service, GameType.ZeroHour, first);

        File.WriteAllText(mapPath, OtherMapContents);
        File.SetLastWriteTimeUtc(mapPath, DateTime.UtcNow.AddMinutes(5));
        var second = CreateReplay(MapContentsCrc, (uint)MapContents.Length);
        await ResolveIntoAsync(service, GameType.ZeroHour, second);

        Assert.Equal(ReplayMapAvailability.Available, first.MapAvailability);
        Assert.Equal(ReplayMapAvailability.NotFound, second.MapAvailability);
    }

    /// <summary>
    /// Verifies that a zero map CRC or size, which the engine writes when it has no map data, is treated as missing data.
    /// </summary>
    /// <param name="mapCrc">The replay map CRC.</param>
    /// <param name="mapSize">The replay map size.</param>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(0u, 2u)]
    [InlineData(MapContentsCrc, 0u)]
    public async Task ResolveMapAvailabilityAsync_ZeroMapCrcOrSize_ReportsUnknownAsync(uint mapCrc, uint mapSize)
    {
        WriteStockArchive(("Maps\\Stock Map\\Stock Map.map", MapContents));
        var replay = CreateReplay(mapCrc, mapSize);

        await ResolveIntoAsync(CreateService(), GameType.ZeroHour, replay);

        Assert.Equal(ReplayMapAvailability.Unknown, replay.MapAvailability);
    }

    /// <summary>
    /// Verifies that an unreadable archive in the installation leaves unmatched maps unknown instead of not found,
    /// while maps found in readable sources are still reported as available.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveMapAvailabilityAsync_UnreadableArchive_ReportsUnknownForUnmatchedMapAsync()
    {
        WriteStockArchive(("Maps\\Stock Map\\Stock Map.map", MapContents));
        File.WriteAllText(Path.Combine(_installDirectory, "Corrupt.big"), "not a big archive");
        var matched = CreateReplay(MapContentsCrc, (uint)MapContents.Length);
        var unmatched = CreateReplay(0xDEADBEEF, 12345);

        await ResolveIntoAsync(CreateService(), GameType.ZeroHour, matched, unmatched);

        Assert.Equal(ReplayMapAvailability.Available, matched.MapAvailability);
        Assert.Equal(ReplayMapAvailability.Unknown, unmatched.MapAvailability);
    }

    /// <summary>
    /// Verifies that the matching map is found when several stock entries share its size and only one matches its CRC.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveMapAvailabilityAsync_SeveralSameSizeEntries_MatchesByCrcAsync()
    {
        WriteStockArchive(
            ("Maps\\First\\First.map", OtherMapContents),
            ("Maps\\Second\\Second.map", MapContents),
            ("Art\\Textures\\Second.map", OtherMapContents));
        var replay = CreateReplay(MapContentsCrc, (uint)MapContents.Length);

        await ResolveIntoAsync(CreateService(), GameType.ZeroHour, replay);

        Assert.Equal(ReplayMapAvailability.Available, replay.MapAvailability);
    }

    /// <summary>
    /// Verifies that archive entries and user maps larger than the read buffer are checksummed in full.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveMapAvailabilityAsync_MapsLargerThanReadBuffer_MatchFullFileCrcAsync()
    {
        var stockContents = CreateLargeContents(200_003, 7);
        var userContents = CreateLargeContents(150_001, 13);
        WriteStockArchive(("Maps\\Big Stock\\Big Stock.map", stockContents), ("Maps\\Other\\Other.map", "x"));
        WriteUserMap("Big User", userContents);
        var stockReplay = CreateReplay(ComputeExpectedCrc(stockContents), (uint)stockContents.Length);
        var userReplay = CreateReplay(ComputeExpectedCrc(userContents), (uint)userContents.Length);
        var truncatedReplay = CreateReplay(ComputeExpectedCrc(stockContents[..81_920]), (uint)stockContents.Length);

        await ResolveIntoAsync(CreateService(), GameType.ZeroHour, stockReplay, userReplay, truncatedReplay);

        Assert.Equal(ReplayMapAvailability.Available, stockReplay.MapAvailability);
        Assert.Equal(ReplayMapAvailability.Available, userReplay.MapAvailability);
        Assert.Equal(ReplayMapAvailability.NotFound, truncatedReplay.MapAvailability);
    }

    /// <summary>
    /// Verifies that the lookup returns results without changing the replays it was given.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveMapAvailabilityAsync_ReturnsResultsWithoutModifyingReplaysAsync()
    {
        WriteStockArchive(("Maps\\Stock Map\\Stock Map.map", MapContents));
        var found = CreateReplay(MapContentsCrc, (uint)MapContents.Length);
        var missing = CreateReplay(0xDEADBEEF, 12345);
        var noData = CreateReplay(MapContentsCrc, null);

        var results = await CreateService().ResolveMapAvailabilityAsync(GameType.ZeroHour, [found, missing, noData]);

        Assert.Equal([ReplayMapAvailability.Available, ReplayMapAvailability.NotFound, ReplayMapAvailability.Unknown], results);
        Assert.All([found, missing, noData], r => Assert.Equal(ReplayMapAvailability.Unknown, r.MapAvailability));
    }

    /// <summary>
    /// Verifies that a changed map file replaces its cached CRC instead of adding a second entry for the same file.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveMapAvailabilityAsync_ChangedMap_ReplacesCachedCrcAsync()
    {
        var mapPath = WriteUserMap("Custom Map", MapContents);
        var service = CreateService();
        var replay = CreateReplay(MapContentsCrc, (uint)MapContents.Length);
        await service.ResolveMapAvailabilityAsync(GameType.ZeroHour, [replay]);
        Assert.Equal(1, service.CachedCrcCount);

        for (var i = 1; i <= 3; i++)
        {
            File.WriteAllText(mapPath, i % 2 == 0 ? MapContents : OtherMapContents);
            File.SetLastWriteTimeUtc(mapPath, DateTime.UtcNow.AddMinutes(i));
            await service.ResolveMapAvailabilityAsync(GameType.ZeroHour, [replay]);
        }

        Assert.Equal(1, service.CachedCrcCount);
    }

    /// <summary>
    /// Verifies that the CRC cache never grows past its capacity.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveMapAvailabilityAsync_CacheAtCapacity_StaysWithinCapacityAsync()
    {
        WriteUserMap("First", OtherMapContents);
        WriteUserMap("Second", OtherMapContents);
        WriteUserMap("Third", MapContents);
        var service = new ReplayMapLookupService(_mapDirectoryService.Object, _installationService.Object, NullLogger<ReplayMapLookupService>.Instance)
        {
            CrcCacheCapacity = 2,
        };
        var replay = CreateReplay(MapContentsCrc, (uint)MapContents.Length);

        var results = await service.ResolveMapAvailabilityAsync(GameType.ZeroHour, [replay]);

        Assert.Equal(ReplayMapAvailability.Available, results[0]);
        Assert.InRange(service.CachedCrcCount, 1, 2);
    }

    /// <summary>
    /// Verifies that cancellation during installation discovery is reported as cancellation, not as unknown results,
    /// even when discovery turns the cancellation into a failure result and there are no maps to scan.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveMapAvailabilityAsync_CanceledDuringDiscovery_ThrowsOperationCanceledAsync()
    {
        using var cts = new CancellationTokenSource();
        _installationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromResult(OperationResult<IReadOnlyList<GameInstallation>>.CreateFailure("canceled"));
            });
        var replay = CreateReplay(MapContentsCrc, (uint)MapContents.Length);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService().ResolveMapAvailabilityAsync(GameType.ZeroHour, [replay], cts.Token));
        _mapDirectoryService.Verify(m => m.GetMapDirectory(It.IsAny<GameType>()), Times.Never);
    }

    /// <summary>
    /// Verifies that installation discovery runs off the calling thread, so a slow first detection does not block
    /// the caller before the map scan starts.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveMapAvailabilityAsync_SlowInstallationDiscovery_DoesNotBlockCallerAsync()
    {
        using var discoveryReleased = new ManualResetEventSlim();
        var callReturned = false;
        var callReturnedBeforeDiscoveryFinished = false;
        _installationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                discoveryReleased.Wait(TimeSpan.FromSeconds(10));
                callReturnedBeforeDiscoveryFinished = Volatile.Read(ref callReturned);
                return Task.FromResult(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([CreateZeroHourInstallation()]));
            });
        WriteStockArchive(("Maps\\Stock Map\\Stock Map.map", MapContents));
        var replay = CreateReplay(MapContentsCrc, (uint)MapContents.Length);

        var resolve = CreateService().ResolveMapAvailabilityAsync(GameType.ZeroHour, [replay]);
        Volatile.Write(ref callReturned, true);
        discoveryReleased.Set();
        var results = await resolve;

        Assert.True(callReturnedBeforeDiscoveryFinished);
        Assert.Equal(ReplayMapAvailability.Available, results[0]);
    }

    private static async Task ResolveIntoAsync(ReplayMapLookupService service, GameType version, params ReplayFile[] replays)
    {
        var results = await service.ResolveMapAvailabilityAsync(version, replays);
        Assert.Equal(replays.Length, results.Count);
        for (var i = 0; i < replays.Length; i++)
        {
            replays[i].MapAvailability = results[i];
        }
    }

    private static string CreateLargeContents(int length, int seed)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)('A' + ((i * seed) % 26));
        }

        return new string(chars);
    }

    private static uint ComputeExpectedCrc(string contents)
    {
        var checksum = new LegacyChecksum();
        checksum.Add(Encoding.ASCII.GetBytes(contents));
        return checksum.Value;
    }

    private static ReplayFile CreateReplay(uint mapCrc, uint? mapSize) => new()
    {
        FullPath = "/replays/test.rep",
        FileName = "test.rep",
        SizeInBytes = 2048,
        LastModified = DateTime.UtcNow,
        GameVersion = GameType.ZeroHour,
        Metadata = new ReplayMetadata
        {
            MapName = "test",
            MapCrc = mapCrc,
            MapSize = mapSize,
        },
    };

    private GameInstallation CreateZeroHourInstallation() => new(_installDirectory, GameInstallationType.Retail)
    {
        HasZeroHour = true,
        ZeroHourPath = _installDirectory,
    };

    private void SetInstallations(params GameInstallation[] installations)
    {
        _installationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess(installations));
    }

    private void WriteStockArchive(params (string EntryPath, string Contents)[] entries) =>
        BigArchiveFixture.Write(Path.Combine(_installDirectory, "MapsZH.big"), entries);

    private string WriteUserMap(string name, string contents)
    {
        var directory = Path.Combine(_userMapsDirectory, name);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".map");
        File.WriteAllText(path, contents);
        return path;
    }

    private ReplayMapLookupService CreateService() =>
        new(_mapDirectoryService.Object, _installationService.Object, NullLogger<ReplayMapLookupService>.Instance);
}
