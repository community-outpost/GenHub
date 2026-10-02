using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Features.Tools.ReplayManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for <see cref="ReplayPinnedManifestProvider"/>.
/// </summary>
public sealed class ReplayPinnedManifestProviderTests
{
    /// <summary>
    /// Verifies that empty set is returned when no replays are found.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetPinnedManifestIdsAsync_NoReplays_ReturnsEmptySetAsync()
    {
        var replayDirMock = new Mock<IReplayDirectoryService>();
        var crcRegistryMock = new Mock<ICrcMappingRegistry>();

        replayDirMock.Setup(d => d.GetReplaysAsync(It.IsAny<GameType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReplayFile>());

        var provider = new ReplayPinnedManifestProvider(
            NullLogger<ReplayPinnedManifestProvider>.Instance,
            replayDirMock.Object,
            crcRegistryMock.Object);

        var pinned = await provider.GetPinnedManifestIdsAsync();
        Assert.Empty(pinned);
    }

    /// <summary>
    /// Verifies that matching replay CRCs return both gameclient and patch manifest IDs.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetPinnedManifestIdsAsync_MatchingReplay_ReturnsManifestAndDataPatchIdAsync()
    {
        var replayDirMock = new Mock<IReplayDirectoryService>();
        var crcRegistryMock = new Mock<ICrcMappingRegistry>();

        var replay = new ReplayFile
        {
            FullPath = "C:/replays/match1.rep",
            FileName = "match1.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x088DE24A,
                IniCrc = 0x81FB5632,
            },
        };

        replayDirMock.Setup(d => d.GetReplaysAsync(GameType.ZeroHour, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReplayFile> { replay });
        replayDirMock.Setup(d => d.GetReplaysAsync(GameType.Generals, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReplayFile>());

        var entry = new CrcMappingEntry
        {
            ExeCrc = "0x088DE24A",
            IniCrc = "0x81FB5632",
            ManifestId = "1.92826.generalsonline.gameclient.zerohour",
            DataPatchManifestId = "1.92826.generalsonline.patch.gamedata",
            Publisher = "generalsonline",
            GameType = "ZeroHour",
            Version = "092826",
            BuildDate = "2026-09-28",
            Description = "GeneralsOnline 092826",
        };

        CrcMappingEntry? outEntry = entry;
        crcRegistryMock.Setup(c => c.TryGetEntry("0x088DE24A", "0x81FB5632", out outEntry))
            .Returns(true);

        var provider = new ReplayPinnedManifestProvider(
            NullLogger<ReplayPinnedManifestProvider>.Instance,
            replayDirMock.Object,
            crcRegistryMock.Object);

        var pinned = await provider.GetPinnedManifestIdsAsync();

        Assert.Contains("1.92826.generalsonline.gameclient.zerohour", pinned);
        Assert.Contains("1.92826.generalsonline.patch.gamedata", pinned);
    }

    /// <summary>
    /// Verifies that exceptions in replay scanning recover gracefully.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetPinnedManifestIdsAsync_ReplayDirectoryThrows_RecoversGracefullyAsync()
    {
        var replayDirMock = new Mock<IReplayDirectoryService>();
        var crcRegistryMock = new Mock<ICrcMappingRegistry>();

        replayDirMock.Setup(d => d.GetReplaysAsync(It.IsAny<GameType>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.IO.DirectoryNotFoundException());

        var provider = new ReplayPinnedManifestProvider(
            NullLogger<ReplayPinnedManifestProvider>.Instance,
            replayDirMock.Object,
            crcRegistryMock.Object);

        var pinned = await provider.GetPinnedManifestIdsAsync();
        Assert.Empty(pinned);
    }

    /// <summary>
    /// Verifies that replays with only an IniCrc pin the corresponding data patch manifest ID.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetPinnedManifestIdsAsync_IniCrcOnly_PinsDataPatchManifestIdAsync()
    {
        var replayDirMock = new Mock<IReplayDirectoryService>();
        var crcRegistryMock = new Mock<ICrcMappingRegistry>();

        var replay = new ReplayFile
        {
            FullPath = "C:/replays/inionly.rep",
            FileName = "inionly.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                IniCrc = 0x81FB5632,
            },
        };

        replayDirMock.Setup(d => d.GetReplaysAsync(GameType.ZeroHour, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReplayFile> { replay });
        replayDirMock.Setup(d => d.GetReplaysAsync(GameType.Generals, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReplayFile>());

        var entry = new CrcMappingEntry
        {
            IniCrc = "0x81FB5632",
            ManifestId = "1.0.base.gameclient.zerohour",
            DataPatchManifestId = "1.92826.generalsonline.patch.gamedata",
            Publisher = "generalsonline",
            GameType = "ZeroHour",
        };

        CrcMappingEntry? outEntry = entry;
        crcRegistryMock.Setup(c => c.TryGetEntryByIniCrc("0x81FB5632", out outEntry))
            .Returns(true);

        var provider = new ReplayPinnedManifestProvider(
            NullLogger<ReplayPinnedManifestProvider>.Instance,
            replayDirMock.Object,
            crcRegistryMock.Object);

        var pinned = await provider.GetPinnedManifestIdsAsync();

        Assert.Contains("1.92826.generalsonline.patch.gamedata", pinned);
        Assert.DoesNotContain("1.0.base.gameclient.zerohour", pinned);
    }

    /// <summary>
    /// Verifies that replays with only an ExeCrc pin all candidate entries sharing that executable CRC,
    /// even if the catalog entry omits the 0x hex prefix or uses different casing.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetPinnedManifestIdsAsync_ExeCrcOnly_PinsAllCandidateEntriesWithMatchingExeCrcAsync()
    {
        var replayDirMock = new Mock<IReplayDirectoryService>();
        var crcRegistryMock = new Mock<ICrcMappingRegistry>();

        var replay = new ReplayFile
        {
            FullPath = "C:/replays/exeonly.rep",
            FileName = "exeonly.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x088DE24A,
            },
        };

        replayDirMock.Setup(d => d.GetReplaysAsync(GameType.ZeroHour, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReplayFile> { replay });
        replayDirMock.Setup(d => d.GetReplaysAsync(GameType.Generals, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReplayFile>());

        var entry1 = new CrcMappingEntry
        {
            ExeCrc = "0x088DE24A",
            ManifestId = "1.0.base.gameclient.zerohour",
            Publisher = "retail",
            GameType = "ZeroHour",
        };
        var entry2 = new CrcMappingEntry
        {
            ExeCrc = "088de24a", // Unprefixed and lowercase to verify NormalizeHex matching
            ManifestId = "1.92826.generalsonline.gameclient.zerohour",
            DataPatchManifestId = "1.92826.generalsonline.patch.gamedata",
            Publisher = "generalsonline",
            GameType = "ZeroHour",
        };

        CrcMappingEntry? outEntry = entry1;
        crcRegistryMock.Setup(c => c.TryGetEntryByExeCrc("0x088DE24A", out outEntry))
            .Returns(true);
        crcRegistryMock.Setup(c => c.GetAllEntries())
            .Returns(new List<CrcMappingEntry> { entry1, entry2 });

        var provider = new ReplayPinnedManifestProvider(
            NullLogger<ReplayPinnedManifestProvider>.Instance,
            replayDirMock.Object,
            crcRegistryMock.Object);

        var pinned = await provider.GetPinnedManifestIdsAsync();

        Assert.Contains("1.0.base.gameclient.zerohour", pinned);
        Assert.Contains("1.92826.generalsonline.gameclient.zerohour", pinned);
        Assert.Contains("1.92826.generalsonline.patch.gamedata", pinned);
    }

    /// <summary>
    /// Verifies that replays with known Exe and INI CRCs whose combined pair is absent in the catalog
    /// pin both the executable entries and the data patch matching the INI CRC.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetPinnedManifestIdsAsync_ReplayWithKnownExeAndIniButCombinedPairAbsent_PinsBothGameClientAndDataPatchAsync()
    {
        var replayDirMock = new Mock<IReplayDirectoryService>();
        var crcRegistryMock = new Mock<ICrcMappingRegistry>();

        var replay = new ReplayFile
        {
            FullPath = "C:/replays/paired.rep",
            FileName = "paired.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x088DE24A,
                IniCrc = 0x81FB5632,
            },
        };

        replayDirMock.Setup(d => d.GetReplaysAsync(GameType.ZeroHour, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReplayFile> { replay });
        replayDirMock.Setup(d => d.GetReplaysAsync(GameType.Generals, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReplayFile>());

        CrcMappingEntry? outMatched = null;
        crcRegistryMock.Setup(c => c.TryGetEntry("0x088DE24A", "0x81FB5632", out outMatched))
            .Returns(false);

        var iniEntry = new CrcMappingEntry
        {
            IniCrc = "0x81FB5632",
            DataPatchManifestId = "1.92826.generalsonline.patch.gamedata",
            Publisher = "generalsonline",
            GameType = "ZeroHour",
        };
        CrcMappingEntry? outIni = iniEntry;
        crcRegistryMock.Setup(c => c.TryGetEntryByIniCrc("0x81FB5632", out outIni))
            .Returns(true);

        var exeEntry = new CrcMappingEntry
        {
            ExeCrc = "0x088DE24A",
            ManifestId = "1.0.base.gameclient.zerohour",
            Publisher = "retail",
            GameType = "ZeroHour",
        };
        CrcMappingEntry? outExe = exeEntry;
        crcRegistryMock.Setup(c => c.TryGetEntryByExeCrc("0x088DE24A", out outExe))
            .Returns(true);
        crcRegistryMock.Setup(c => c.GetAllEntries())
            .Returns(new List<CrcMappingEntry> { exeEntry });

        var provider = new ReplayPinnedManifestProvider(
            NullLogger<ReplayPinnedManifestProvider>.Instance,
            replayDirMock.Object,
            crcRegistryMock.Object);

        var pinned = await provider.GetPinnedManifestIdsAsync();

        Assert.Contains("1.0.base.gameclient.zerohour", pinned);
        Assert.Contains("1.92826.generalsonline.patch.gamedata", pinned);
    }
}
