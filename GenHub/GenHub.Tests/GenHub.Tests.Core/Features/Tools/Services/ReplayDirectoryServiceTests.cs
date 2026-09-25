using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Tools.Checksum;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Features.Tools.ReplayManager.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for ReplayDirectoryService compatibility resolution, profile creation, and replay launch.
/// </summary>
public sealed class ReplayDirectoryServiceTests
{
    private readonly Mock<IReplayHeaderParser> _mockHeaderParser = new();
    private readonly Mock<ICrcMappingRegistry> _mockCrcRegistry = new();
    private readonly Mock<IContentManifestPool> _mockManifestPool = new();
    private readonly Mock<IGameProfileManager> _mockProfileManager = new();
    private readonly Mock<IGameInstallationService> _mockInstallationService = new();
    private readonly Mock<IProfileLauncherFacade> _mockLauncherFacade = new();
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory = new();
    private readonly Mock<IServiceScope> _mockScope = new();
    private readonly Mock<IServiceProvider> _mockServiceProvider = new();
    private readonly Mock<IDependencyResolver> _mockDependencyResolver = new();
    private readonly Mock<IConfigurationProviderService> _mockConfigurationProvider = new();
    private readonly Mock<IInstallationCasPoolService> _mockCasPoolService = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayDirectoryServiceTests"/> class.
    /// </summary>
    public ReplayDirectoryServiceTests()
    {
        _mockScope.Setup(s => s.ServiceProvider).Returns(_mockServiceProvider.Object);
        _mockScopeFactory.Setup(f => f.CreateScope()).Returns(_mockScope.Object);

        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IContentManifestPool)))
            .Returns(_mockManifestPool.Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IGameProfileManager)))
            .Returns(_mockProfileManager.Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IGameInstallationService)))
            .Returns(_mockInstallationService.Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IProfileLauncherFacade)))
            .Returns(_mockLauncherFacade.Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IDependencyResolver)))
            .Returns(_mockDependencyResolver.Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IConfigurationProviderService)))
            .Returns(_mockConfigurationProvider.Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IInstallationCasPoolService)))
            .Returns(_mockCasPoolService.Object);

        _mockInstallationService
            .Setup(s => s.CreateAndRegisterInstallationManifestsAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockManifestPool
            .Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));
        _mockManifestPool
            .Setup(m => m.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(null));

        _mockLauncherFacade
            .Setup(l => l.GetLaunchStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProcessInfo>.CreateSuccess(new GameProcessInfo { IsRunning = false }));

        _mockProfileManager
            .Setup(p => p.GetProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string profileId, CancellationToken _) =>
                ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile
                {
                    Id = profileId,
                    Name = $"Profile-{profileId}",
                    GameClient = new GameClient
                    {
                        Id = "1.104.retail.gameclient.zerohour",
                        GameType = GameType.ZeroHour,
                        PublisherType = "retail",
                    },
                    EnabledContentIds = ["1.104.retail.gameclient.zerohour"],
                }));
    }

    /// <summary>
    /// Verifies that profile creation succeeds and updates the replay state when a matched client is present.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_WhenMatchedClientExists_CreatesProfileAndUpdatesReplayAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "Match1.rep",
            FullPath = "/replays/Match1.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x27533BB0,
                IniCrc = 0x76B251A3,
            },
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0x27533BB0",
                IniCrc = "0x76B251A3",
                ManifestId = "1.20260821.thesuperhackers.gameclient.zerohour",
                Publisher = "thesuperhackers",
                GameType = "ZeroHour",
                Version = "2026-08-21",
                Description = "TheSuperHackers 2026-08-21",
            },
        };

        var installation = new GameInstallation("/games/ZeroHour", GameInstallationType.Retail)
        {
            HasZeroHour = true,
            ZeroHourPath = "/games/ZeroHour",
        };

        _mockDependencyResolver
            .Setup(r => r.ResolveDependenciesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) => new HashSet<string>(ids));

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        var createdProfile = new GameProfile
        {
            Id = "profile-zh-1",
            Name = "SuperHackers 2026-08-21 (Replay: Match1)",
        };

        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(createdProfile));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.CreateProfileForReplayAsync(replay);

        Assert.Null(result.FirstError);
        Assert.NotNull(result.Data);
        Assert.Equal("profile-zh-1", replay.MatchingProfileId);
        Assert.Equal(ReplayCompatibilityStatus.Compatible, replay.CompatibilityStatus);
        Assert.Equal("Profile Ready", replay.CompatibilityBadgeText);
    }

    /// <summary>
    /// Verifies that profile creation succeeds and creates a base game profile when replay is unmapped/orphaned.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_WhenNoMatchedClient_CreatesBaseGameProfileAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "Unknown.rep",
            FullPath = "/replays/Unknown.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x99999999,
                IniCrc = 0x11111111,
            },
            MatchedClient = null,
        };

        CrcMappingEntry? nullEntry = null;
        _mockCrcRegistry
            .Setup(r => r.TryGetEntry("0x99999999", "0x11111111", out nullEntry))
            .Returns(false);

        var installation = new GameInstallation("/games/ZeroHour", GameInstallationType.Retail)
        {
            HasZeroHour = true,
            ZeroHourPath = "/games/ZeroHour",
        };

        _mockDependencyResolver
            .Setup(r => r.ResolveDependenciesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) => new HashSet<string>(ids));

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        CreateProfileRequest? capturedRequest = null;
        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync((CreateProfileRequest req, CancellationToken _) =>
                ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "unmapped-profile-id", Name = req.Name }));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.CreateProfileForReplayAsync(replay);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.NotNull(capturedRequest);
        Assert.Equal("unmapped-profile-id", replay.MatchingProfileId);
        Assert.Equal(ReplayCompatibilityStatus.Compatible, replay.CompatibilityStatus);
        Assert.Contains("Zero Hour (Replay: Unknown)", capturedRequest.Name);
    }

    /// <summary>
    /// Verifies that profile creation fails gracefully when executable path cannot be resolved from installation.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_WhenExecutablePathCannotBeDetermined_ReturnsFailureAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "TestReplay.rep",
            FullPath = "/replays/TestReplay.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x401D89EA,
                IniCrc = 0x76B251A3,
            },
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0x401D89EA",
                IniCrc = "0x76B251A3",
                ManifestId = "1.104.steam.gameclient.zerohour",
                Publisher = "steam",
                GameType = "ZeroHour",
                Version = "1.04",
            },
        };

        var emptyInstallation = new GameInstallation(string.Empty, GameInstallationType.Retail)
        {
            HasZeroHour = true,
            ZeroHourPath = string.Empty,
        };

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([emptyInstallation]));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.CreateProfileForReplayAsync(replay);

        Assert.False(result.Success);
        Assert.Contains("Could not determine executable path", result.FirstError);
    }

    /// <summary>
    /// Verifies that launching a replay with an existing profile delegates to the launcher facade.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LaunchReplayAsync_WhenMatchingProfileExists_LaunchesProfileSuccessfullyAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "TestReplay.rep",
            FullPath = "/replays/TestReplay.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            MatchingProfileId = "existing-profile-id",
            CompatibilityStatus = ReplayCompatibilityStatus.Compatible,
        };

        var launchInfo = new GameLaunchInfo
        {
            LaunchId = "launch-123",
            ProfileId = "existing-profile-id",
            WorkspaceId = "ws-123",
            ProcessInfo = new GameProcessInfo
            {
                ProcessId = 9999,
                ExecutablePath = "/ws/generalszh.exe",
            },
        };

        _mockLauncherFacade
            .Setup(l => l.LaunchProfileAsync("existing-profile-id", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.LaunchReplayAsync(replay);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("launch-123", result.Data.LaunchId);
    }

    /// <summary>
    /// Verifies that ReplayFile helper properties correctly map all compatibility enum states to badges.
    /// </summary>
    [Fact]
    public void ReplayFile_CompatibilityBadge_ReflectsStatusAccurately()
    {
        // Compatible state
        var compatibleReplay = new ReplayFile
        {
            FileName = "Test.rep",
            FullPath = "/path/Test.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            CompatibilityStatus = ReplayCompatibilityStatus.Compatible,
            MatchingProfileId = "zh-sh",
            MatchingProfileName = "ZH SuperHackers",
        };
        Assert.Equal("Profile Ready", compatibleReplay.CompatibilityBadgeText);

        // RequiresProfile state
        var requiresProfileReplay = new ReplayFile
        {
            FileName = "Test.rep",
            FullPath = "/path/Test.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            CompatibilityStatus = ReplayCompatibilityStatus.RequiresProfile,
        };
        Assert.Equal("Profile Needed", requiresProfileReplay.CompatibilityBadgeText);

        // Downloadable state
        var downloadableReplay = new ReplayFile
        {
            FileName = "Test.rep",
            FullPath = "/path/Test.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            CompatibilityStatus = ReplayCompatibilityStatus.Downloadable,
        };
        Assert.Equal("Download Required", downloadableReplay.CompatibilityBadgeText);

        // Orphaned state
        var orphanedReplay = new ReplayFile
        {
            FileName = "Test.rep",
            FullPath = "/path/Test.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            CompatibilityStatus = ReplayCompatibilityStatus.Orphaned,
        };
        Assert.Equal("Custom / Unmapped", orphanedReplay.CompatibilityBadgeText);

        // Unknown state
        var unknownReplay = new ReplayFile
        {
            FileName = "Test.rep",
            FullPath = "/path/Test.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            CompatibilityStatus = ReplayCompatibilityStatus.Unknown,
        };
        Assert.Equal("Unknown", unknownReplay.CompatibilityBadgeText);

        // Verify computed convenience properties
        Assert.True(compatibleReplay.CanPlay);
        Assert.False(compatibleReplay.IsDownloadRequired);
        Assert.False(compatibleReplay.IsProfileNeeded);
        Assert.False(compatibleReplay.IsOrphaned);

        Assert.False(requiresProfileReplay.CanPlay);
        Assert.False(requiresProfileReplay.IsDownloadRequired);
        Assert.True(requiresProfileReplay.IsProfileNeeded);
        Assert.False(requiresProfileReplay.IsOrphaned);

        Assert.False(downloadableReplay.CanPlay);
        Assert.True(downloadableReplay.IsDownloadRequired);
        Assert.False(downloadableReplay.IsProfileNeeded);
        Assert.False(downloadableReplay.IsOrphaned);

        Assert.False(orphanedReplay.CanPlay);
        Assert.False(orphanedReplay.IsDownloadRequired);
        Assert.False(orphanedReplay.IsProfileNeeded);
        Assert.True(orphanedReplay.IsOrphaned);

        var replayWithMetadata = new ReplayFile
        {
            FileName = "CrcTest.rep",
            FullPath = "/test/CrcTest.rep",
            SizeInBytes = 100,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata { ExeCrc = 0x27533BB0, IniCrc = 0x76B251A3 },
        };
        Assert.Equal(0x27533BB0u, replayWithMetadata.ExeCrc);
        Assert.Equal(0x76B251A3u, replayWithMetadata.IniCrc);
    }

    /// <summary>
    /// Verifies that FindCompatibleProfiles returns profiles matching client and patch sorted by score.
    /// </summary>
    [Fact]
    public void FindCompatibleProfiles_MatchesAndSortsByScore()
    {
        var replay = new ReplayFile
        {
            FileName = "Match1.rep",
            FullPath = "/test/Match1.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            MatchedClient = new CrcMappingEntry
            {
                ManifestId = "1.104.steam.gameclient.zerohour",
                DataPatchManifestId = "1.04.patch.data",
            },
        };

        var profile1 = new GameProfile
        {
            Id = "p1",
            Name = "ZH Profile 1 (Replay: Match1)",
            GameClient = new GameClient { Id = "1.104.steam.gameclient.zerohour", GameType = GameType.ZeroHour },
            EnabledContentIds = ["1.04.patch.data"],
        };

        var profile2 = new GameProfile
        {
            Id = "p2",
            Name = "ZH Profile 2",
            GameClient = new GameClient { Id = "1.104.steam.gameclient.zerohour", GameType = GameType.ZeroHour },
            EnabledContentIds = ["1.04.patch.data"],
        };

        var profiles = new List<GameProfile> { profile2, profile1 };
        var results = ReplayDirectoryService.FindCompatibleProfiles(
            profiles,
            GameType.ZeroHour,
            "1.104.steam.gameclient.zerohour",
            "1.04.patch.data",
            replay);

        Assert.Equal(2, results.Count);
        Assert.Equal(profile1, results[0]);
        Assert.Equal(profile2, results[1]);
    }

    /// <summary>
    /// Verifies that FindCompatibleProfiles strictly excludes candidate profiles whose executable binary CRC does not match the replay executable CRC.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FindCompatibleProfiles_WhenProfileExeCrcMismatchesReplayExeCrc_ExcludesProfileAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "genhub_test_crc_mismatch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fakeExePath = Path.Combine(tempDir, "generals.exe");
        File.WriteAllText(fakeExePath, "fake-binary-content");

        try
        {
            var replay = new ReplayFile
            {
                FileName = "CustomExeReplay.rep",
                FullPath = "/test/CustomExeReplay.rep",
                SizeInBytes = 2048,
                LastModified = DateTime.UtcNow,
                GameVersion = GameType.ZeroHour,
                MatchedClient = new CrcMappingEntry
                {
                    ManifestId = "1.104.retail.gameclient.zerohour",
                    ExeCrc = "0x887B0CAA",
                },
            };

            var profile1 = new GameProfile
            {
                Id = "p1",
                Name = "ZH Profile with 0xDA2B4B18",
                GameClient = new GameClient
                {
                    Id = "1.104.retail.gameclient.zerohour",
                    GameType = GameType.ZeroHour,
                    ExecutablePath = fakeExePath,
                },
            };

            var mockCrcCalc = new Mock<IGameCrcCalculatorService>();
            mockCrcCalc
                .Setup(c => c.CalculateExeCrcAsync(fakeExePath, It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess("0xDA2B4B18"));

            var profiles = new List<GameProfile> { profile1 };
            var results = await ReplayDirectoryService.FindCompatibleProfilesAsync(
                profiles,
                replay,
                null,
                mockCrcCalc.Object);

            Assert.Empty(results);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Verifies that FindCompatibleProfiles includes candidate profiles when executable binary CRC matches target CRC.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FindCompatibleProfiles_WhenProfileExeCrcMatchesReplayExeCrc_IncludesProfileAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "genhub_test_crc_match_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fakeExePath = Path.Combine(tempDir, "generals.exe");
        File.WriteAllText(fakeExePath, "fake-binary-content");

        try
        {
            var replay = new ReplayFile
            {
                FileName = "ZH104Replay.rep",
                FullPath = "/test/ZH104Replay.rep",
                SizeInBytes = 2048,
                LastModified = DateTime.UtcNow,
                GameVersion = GameType.ZeroHour,
                MatchedClient = new CrcMappingEntry
                {
                    ManifestId = "1.104.retail.gameclient.zerohour",
                    ExeCrc = "0xDA2B4B18",
                },
            };

            var profile1 = new GameProfile
            {
                Id = "p1",
                Name = "ZH Profile 1.04",
                GameClient = new GameClient
                {
                    Id = "1.104.retail.gameclient.zerohour",
                    GameType = GameType.ZeroHour,
                    ExecutablePath = fakeExePath,
                },
            };

            var mockCrcCalc = new Mock<IGameCrcCalculatorService>();
            mockCrcCalc
                .Setup(c => c.CalculateExeCrcAsync(fakeExePath, It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess("0xDA2B4B18"));

            var profiles = new List<GameProfile> { profile1 };
            var results = await ReplayDirectoryService.FindCompatibleProfilesAsync(
                profiles,
                replay,
                null,
                mockCrcCalc.Object);

            Assert.Single(results);
            Assert.Equal(profile1, results[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Verifies that replay compatibility resolves to Compatible when an existing profile is found.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetReplaysAsync_WhenProfileMatchesClient_ResolvesToCompatibleAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "genhub_test_replays_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var replayFilePath = Path.Combine(tempDir, "TestReplay.rep");
        await File.WriteAllBytesAsync(replayFilePath, new byte[100]);

        try
        {
            var metadata = new ReplayMetadata
            {
                ExeCrc = 0x27533BB0,
                IniCrc = 0x76B251A3,
            };

            var entry = new CrcMappingEntry
            {
                ExeCrc = "0x27533BB0",
                IniCrc = "0x76B251A3",
                ManifestId = "1.20260821.thesuperhackers.gameclient.zerohour",
                Publisher = "thesuperhackers",
                GameType = "ZeroHour",
                Version = "2026-08-21",
                Description = "TheSuperHackers ZeroHour weekly 2026-08-21",
            };

            _mockHeaderParser
                .Setup(p => p.ParseHeaderAsync(replayFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<ReplayMetadata>.CreateSuccess(metadata));

            CrcMappingEntry? outEntry = entry;
            _mockCrcRegistry
                .Setup(r => r.TryGetEntry("0x27533BB0", "0x76B251A3", out outEntry))
                .Returns(true);

            var installation = new GameInstallation("/games/ZeroHour", GameInstallationType.Retail)
            {
                HasZeroHour = true,
                ZeroHourPath = "/games/ZeroHour",
            };

            _mockInstallationService
                .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

            _mockProfileManager
                .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CreateProfileRequest req, CancellationToken _) =>
                    ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "profile-123", Name = req.Name }));

            var service = new ReplayDirectoryService(
                _mockHeaderParser.Object,
                _mockCrcRegistry.Object,
                _mockScopeFactory.Object,
                NullLogger<ReplayDirectoryService>.Instance);

            // Directly test ProcessReplayFileAsync through reflection or GetReplayDirectory-aligned structure
            var replay = new ReplayFile
            {
                FileName = "TestReplay.rep",
                FullPath = replayFilePath,
                SizeInBytes = 100,
                LastModified = DateTime.UtcNow,
                GameVersion = GameType.ZeroHour,
                Metadata = metadata,
            };

            var result = await service.CreateProfileForReplayAsync(replay);
            Assert.True(result.Success);
            Assert.Equal(ReplayCompatibilityStatus.Compatible, replay.CompatibilityStatus);
            Assert.Equal("profile-123", replay.MatchingProfileId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that a retail Zero Hour replay on Steam uses the Steam installation client and generates valid installation manifests.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_WhenRetailReplayOnSteam_UsesSteamClientAndCreatesProfileAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "RetailMatch.rep",
            FullPath = "/replays/RetailMatch.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x401D89EA,
                IniCrc = 0x76B251A3,
            },
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0x401D89EA",
                IniCrc = "0x76B251A3",
                ManifestId = "1.104.retail.gameclient.zerohour",
                Publisher = "ea",
                GameType = "ZeroHour",
                Version = "1.04",
                Description = "Command & Conquer Zero Hour 1.04 Retail",
            },
        };

        var steamClient = new GameClient
        {
            Id = "1.104.steam.gameclient.zerohour",
            Name = "Command and Conquer Generals Zero Hour (Steam)",
            Version = "1.04",
            GameType = GameType.ZeroHour,
            PublisherType = "Steam",
            InstallationId = "steam-inst-1",
            ExecutablePath = "/steam/generalszh.exe",
            WorkingDirectory = "/steam",
        };

        var installation = new GameInstallation("/steam", GameInstallationType.Steam)
        {
            Id = "steam-inst-1",
            HasZeroHour = true,
            ZeroHourPath = "/steam",
            AvailableGameClients = [steamClient],
        };

        _mockDependencyResolver
            .Setup(r => r.ResolveDependenciesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) => new HashSet<string>(ids));

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        CreateProfileRequest? capturedRequest = null;
        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync((CreateProfileRequest req, CancellationToken _) =>
                ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "steam-zh-profile", Name = req.Name }));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.CreateProfileForReplayAsync(replay);

        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        Assert.Equal("1.104.steam.gameclient.zerohour", capturedRequest.GameClientId);
        Assert.NotNull(capturedRequest.EnabledContentIds);
        Assert.Contains("1.104.steam.gameinstallation.zerohour", capturedRequest.EnabledContentIds);
        Assert.Contains("1.104.steam.gameclient.zerohour", capturedRequest.EnabledContentIds);
        Assert.False(capturedRequest.UseSteamLaunch);
        Assert.Equal("steam-zh-profile", replay.MatchingProfileId);
        Assert.Equal(ReplayCompatibilityStatus.Compatible, replay.CompatibilityStatus);
    }

    /// <summary>
    /// Verifies that profile creation for GeneralsOnline gathers companion manifests (patch, map pack).
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_WhenGeneralsOnlineClient_AddsCompanionManifestsAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "MatchGO.rep",
            FullPath = "/replays/MatchGO.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x12345678,
                IniCrc = 0x87654321,
            },
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0x12345678",
                IniCrc = "0x87654321",
                ManifestId = "1.82826.generalsonline.gameclient.60hz",
                Publisher = "generalsonline",
                GameType = "ZeroHour",
                Version = "082826",
                Description = "GeneralsOnline 082826",
            },
        };

        var installation = new GameInstallation("/games/ZeroHour", GameInstallationType.Retail)
        {
            HasZeroHour = true,
            ZeroHourPath = "/games/ZeroHour",
        };

        _mockDependencyResolver
            .Setup(r => r.ResolveDependenciesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) => new HashSet<string>(ids));

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        var acquiredGameClient = new ContentManifest
        {
            Id = ManifestId.Create("1.82826.generalsonline.gameclient.60hz"),
            Name = "GeneralsOnline 60Hz",
            ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Version = "082826",
            Publisher = new PublisherInfo { PublisherType = "generalsonline", Name = "GeneralsOnline" },
            Dependencies =
            [
                new ContentDependency { Id = ManifestId.Create("1.82826.generalsonline.mappack.quickmatchmaps") },
                new ContentDependency { Id = ManifestId.Create("1.82826.generalsonline.patch.gamedata") },
            ],
        };

        var acquiredMapPack = new ContentManifest
        {
            Id = ManifestId.Create("1.82826.generalsonline.mappack.quickmatchmaps"),
            Name = "GeneralsOnline QuickMatch Maps",
            ContentType = GenHub.Core.Models.Enums.ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
            Version = "082826",
            Publisher = new PublisherInfo { PublisherType = "generalsonline", Name = "GeneralsOnline" },
        };

        var acquiredGameData = new ContentManifest
        {
            Id = ManifestId.Create("1.82826.generalsonline.patch.gamedata"),
            Name = "GeneralsOnline Game Data",
            ContentType = GenHub.Core.Models.Enums.ContentType.Patch,
            TargetGame = GameType.ZeroHour,
            Version = "082826",
            Publisher = new PublisherInfo { PublisherType = "generalsonline", Name = "GeneralsOnline" },
        };

        _mockManifestPool
            .Setup(m => m.GetManifestAsync(It.Is<ManifestId>(id => id.Value == "1.82826.generalsonline.gameclient.60hz"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(acquiredGameClient));

        _mockManifestPool
            .Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([acquiredGameClient, acquiredMapPack, acquiredGameData]));

        CreateProfileRequest? capturedRequest = null;
        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync((CreateProfileRequest req, CancellationToken _) =>
                ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "go-profile-1", Name = req.Name }));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.CreateProfileForReplayAsync(replay);

        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        Assert.Equal("1.82826.generalsonline.gameclient.60hz", capturedRequest.GameClientId);
        Assert.NotNull(capturedRequest.EnabledContentIds);
        Assert.Contains("1.104.retail.gameinstallation.zerohour", capturedRequest.EnabledContentIds);
        Assert.Contains("1.82826.generalsonline.gameclient.60hz", capturedRequest.EnabledContentIds);
        Assert.Contains("1.82826.generalsonline.mappack.quickmatchmaps", capturedRequest.EnabledContentIds);
        Assert.Contains("1.82826.generalsonline.patch.gamedata", capturedRequest.EnabledContentIds);
    }

    /// <summary>
    /// Verifies that LaunchReplayAsync automatically creates a profile if matching profile is absent, and launches it.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LaunchReplayAsync_WhenNoMatchingProfile_CreatesProfileAndLaunchesSuccessfullyAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "FreshMatch.rep",
            FullPath = "/replays/FreshMatch.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            MatchingProfileId = null,
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0x401D89EA",
                IniCrc = "0x76B251A3",
                ManifestId = "1.104.steam.gameclient.zerohour",
                Publisher = "steam",
                GameType = "ZeroHour",
                Version = "1.04",
                Description = "Command & Conquer Zero Hour 1.04 Steam",
            },
        };

        var steamClient = new GameClient
        {
            Id = "1.104.steam.gameclient.zerohour",
            Name = "Command and Conquer Generals Zero Hour (Steam)",
            Version = "1.04",
            GameType = GameType.ZeroHour,
            PublisherType = "Steam",
            InstallationId = "steam-inst-1",
            ExecutablePath = "/steam/generalszh.exe",
            WorkingDirectory = "/steam",
        };

        var installation = new GameInstallation("/steam", GameInstallationType.Steam)
        {
            Id = "steam-inst-1",
            HasZeroHour = true,
            ZeroHourPath = "/steam",
            AvailableGameClients = [steamClient],
        };

        _mockDependencyResolver
            .Setup(r => r.ResolveDependenciesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) => new HashSet<string>(ids));

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateProfileRequest req, CancellationToken _) =>
                ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "auto-created-profile-99", Name = req.Name }));

        var launchInfo = new GameLaunchInfo
        {
            LaunchId = "launch-auto-99",
            ProfileId = "auto-created-profile-99",
            WorkspaceId = "ws-auto-99",
            ProcessInfo = new GameProcessInfo
            {
                ProcessId = 12345,
                ExecutablePath = "/steam/generalszh.exe",
            },
        };

        _mockLauncherFacade
            .Setup(l => l.LaunchProfileAsync("auto-created-profile-99", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.LaunchReplayAsync(replay);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("auto-created-profile-99", replay.MatchingProfileId);
        Assert.Equal(ReplayCompatibilityStatus.Compatible, replay.CompatibilityStatus);
        Assert.Equal("launch-auto-99", result.Data.LaunchId);
    }

    /// <summary>
    /// Verifies that LaunchReplayAsync clears stale profile reference and auto-creates a new profile when the referenced profile no longer exists.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LaunchReplayAsync_WhenAssociatedProfileIsStale_ClearsStaleReferenceAndCreatesNewProfileAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "StaleMatch.rep",
            FullPath = "/replays/StaleMatch.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            MatchingProfileId = "stale-profile-id",
            MatchingProfileName = "Old Deleted Profile",
            CompatibilityStatus = ReplayCompatibilityStatus.Compatible,
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0x401D89EA",
                IniCrc = "0x76B251A3",
                ManifestId = "1.104.steam.gameclient.zerohour",
                Publisher = "steam",
                GameType = "ZeroHour",
                Version = "1.04",
                Description = "Command & Conquer Zero Hour 1.04 Steam",
            },
        };

        var steamClient = new GameClient
        {
            Id = "1.104.steam.gameclient.zerohour",
            Name = "Command and Conquer Generals Zero Hour (Steam)",
            Version = "1.04",
            GameType = GameType.ZeroHour,
            PublisherType = "Steam",
            InstallationId = "steam-inst-1",
            ExecutablePath = "/steam/generalszh.exe",
            WorkingDirectory = "/steam",
        };

        var installation = new GameInstallation("/steam", GameInstallationType.Steam)
        {
            Id = "steam-inst-1",
            HasZeroHour = true,
            ZeroHourPath = "/steam",
            AvailableGameClients = [steamClient],
        };

        _mockDependencyResolver
            .Setup(r => r.ResolveDependenciesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) => new HashSet<string>(ids));

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        _mockProfileManager
            .Setup(p => p.GetProfileAsync("stale-profile-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("Profile not found"));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateProfileRequest req, CancellationToken _) =>
                ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "recreated-profile-100", Name = req.Name }));

        var launchInfo = new GameLaunchInfo
        {
            LaunchId = "launch-recreated-100",
            ProfileId = "recreated-profile-100",
            WorkspaceId = "ws-recreated-100",
            ProcessInfo = new GameProcessInfo
            {
                ProcessId = 54321,
                ExecutablePath = "/steam/generalszh.exe",
            },
        };

        _mockLauncherFacade
            .Setup(l => l.LaunchProfileAsync("recreated-profile-100", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.LaunchReplayAsync(replay);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("recreated-profile-100", replay.MatchingProfileId);
        Assert.Equal(ReplayCompatibilityStatus.Compatible, replay.CompatibilityStatus);
        Assert.Equal("launch-recreated-100", result.Data.LaunchId);
    }

    /// <summary>
    /// Verifies that profile creation succeeds when targetClient on installation initially has a null ExecutablePath.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_WhenInstallationClientHasNullExecutablePath_ResolvesExecutablePathFromDirectoryAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "00000000.rep",
            FullPath = "/replays/00000000.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x401D89EA,
                IniCrc = 0x76B251A3,
            },
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0x401D89EA",
                IniCrc = "0x76B251A3",
                ManifestId = "1.104.retail.gameclient.zerohour",
                Publisher = "ea",
                GameType = "ZeroHour",
                Version = "1.04",
                Description = "Zero Hour 1.04 (Retail)",
            },
        };

        // Client loaded from manifest without ExecutablePath set
        var incompleteClient = new GameClient
        {
            Id = "1.104.retail.gameclient.zerohour",
            Name = "Zero Hour 1.04",
            Version = "1.04",
            GameType = GameType.ZeroHour,
            InstallationId = "retail-inst-1",
            ExecutablePath = string.Empty,
            WorkingDirectory = "/games/ZeroHour",
        };

        var installation = new GameInstallation("/games/ZeroHour", GameInstallationType.Retail)
        {
            Id = "retail-inst-1",
            HasZeroHour = true,
            ZeroHourPath = "/games/ZeroHour",
            AvailableGameClients = [incompleteClient],
        };

        _mockDependencyResolver
            .Setup(r => r.ResolveDependenciesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) => new HashSet<string>(ids));

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        CreateProfileRequest? capturedRequest = null;
        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync((CreateProfileRequest req, CancellationToken _) =>
                ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "created-zh-profile", Name = req.Name }));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.CreateProfileForReplayAsync(replay);

        var expectedExePath = Path.Combine("/games/ZeroHour", GameClientConstants.ZeroHourExecutable);
        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        Assert.NotNull(capturedRequest.GameClient);
        Assert.Equal(expectedExePath, capturedRequest.GameClient.ExecutablePath);
        Assert.Equal("created-zh-profile", replay.MatchingProfileId);
        Assert.Equal(ReplayCompatibilityStatus.Compatible, replay.CompatibilityStatus);
    }

    /// <summary>
    /// Verifies that replay compatibility resolves to Compatible when a GeneralsOnline profile exists with companion manifests.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetReplaysAsync_WhenGeneralsOnlineProfileExistsWithCompanionPatch_ResolvesToCompatibleAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "genhub_go_replays_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var replayFilePath = Path.Combine(tempDir, "match_3610187_replay.rep");
        await File.WriteAllBytesAsync(replayFilePath, new byte[100]);

        try
        {
            var metadata = new ReplayMetadata
            {
                ExeCrc = 0x6DBF4405,
                IniCrc = 0x51ACED23,
            };

            var entry = new CrcMappingEntry
            {
                ExeCrc = "0x6DBF4405",
                IniCrc = "0x51ACED23",
                ManifestId = "1.82826.generalsonline.gameclient.zerohour",
                DataPatchManifestId = "1.828261.generalsonline.patch.gamedata",
                Publisher = "generalsonline",
                GameType = "ZeroHour",
                Version = "082826_QFE1",
                Description = "GeneralsOnline 082826_QFE1",
            };

            _mockHeaderParser
                .Setup(p => p.ParseHeaderAsync(replayFilePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<ReplayMetadata>.CreateSuccess(metadata));

            CrcMappingEntry? outEntry = entry;
            _mockCrcRegistry
                .Setup(r => r.TryGetEntry("0x6DBF4405", "0x51ACED23", out outEntry))
                .Returns(true);

            var goProfile = new GameProfile
            {
                Id = "83cf88bdf7854d2da504b422b1d4e01e",
                Name = "GeneralsOnline 082826_QFE1 (Replay: match_3610187_replay)",
                Description = "Profile configured for GeneralsOnline 082826_QFE1 (Exe: 0x6DBF4405, INI: 0x51ACED23)",
                GameClient = new GameClient
                {
                    Id = "1.82826.generalsonline.gameclient.60hz",
                    Name = "GeneralsOnline 60Hz",
                    Version = "082826",
                    GameType = GameType.ZeroHour,
                    PublisherType = "generalsonline",
                },
                EnabledContentIds =
                [
                    "1.104.steam.gameinstallation.zerohour",
                    "1.82826.generalsonline.gameclient.60hz",
                    "1.82826.generalsonline.mappack.quickmatchmaps",
                    "1.82826.generalsonline.patch.gamedata",
                ],
            };

            _mockProfileManager
                .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([goProfile]));

            var acquiredGameClient = new ContentManifest
            {
                Id = ManifestId.Create("1.82826.generalsonline.gameclient.60hz"),
                Name = "GeneralsOnline 60Hz",
                ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
                TargetGame = GameType.ZeroHour,
                Publisher = new PublisherInfo { PublisherType = "generalsonline" },
            };

            var installation = new GameInstallation("/games/ZeroHour", GameInstallationType.Retail)
            {
                HasZeroHour = true,
                ZeroHourPath = "/games/ZeroHour",
            };

            _mockInstallationService
                .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

            _mockProfileManager
                .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(goProfile));

            _mockManifestPool
                .Setup(m => m.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(null));

            _mockManifestPool
                .Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([acquiredGameClient]));

            var replay = new ReplayFile
            {
                FileName = "match_3610187_replay.rep",
                FullPath = replayFilePath,
                SizeInBytes = 100,
                LastModified = DateTime.UtcNow,
                GameVersion = GameType.ZeroHour,
                Metadata = metadata,
                MatchedClient = entry,
            };

            var service = new ReplayDirectoryService(
                _mockHeaderParser.Object,
                _mockCrcRegistry.Object,
                _mockScopeFactory.Object,
                NullLogger<ReplayDirectoryService>.Instance);

            var result = await service.CreateProfileForReplayAsync(replay);

            Assert.True(result.Success, result.FirstError ?? "No error");
            Assert.Equal(ReplayCompatibilityStatus.Compatible, replay.CompatibilityStatus);
            Assert.Equal("83cf88bdf7854d2da504b422b1d4e01e", replay.MatchingProfileId);
            Assert.Equal("Profile Ready", replay.CompatibilityBadgeText);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that third-party client profile creation resolves to the pooled manifest ID when the catalog ID differs.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_WhenCatalogIdDiffersFromPooledClientVariant_ResolvesPooledManifestIdAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "match_variant.rep",
            FullPath = "/replays/match_variant.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x6DBF4405,
                IniCrc = 0x51ACED23,
            },
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0x6DBF4405",
                IniCrc = "0x51ACED23",
                ManifestId = "1.82826.generalsonline.gameclient.zerohour",
                Publisher = "generalsonline",
                GameType = "ZeroHour",
                Version = "082826",
                Description = "GeneralsOnline 082826",
            },
        };

        var installation = new GameInstallation("/games/ZeroHour", GameInstallationType.Retail)
        {
            HasZeroHour = true,
            ZeroHourPath = "/games/ZeroHour",
        };

        _mockDependencyResolver
            .Setup(r => r.ResolveDependenciesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) => new HashSet<string>(ids));

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        var pooledClient = new ContentManifest
        {
            Id = ManifestId.Create("1.82826.generalsonline.gameclient.60hz"),
            Name = "GeneralsOnline 60Hz",
            ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
            TargetGame = GameType.Unknown,
            Version = "082826",
            Publisher = new PublisherInfo { PublisherType = "generalsonline" },
        };

        _mockManifestPool
            .Setup(m => m.GetManifestAsync(ManifestId.Create("1.82826.generalsonline.gameclient.zerohour"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateFailure("Not found"));

        _mockManifestPool
            .Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([pooledClient]));

        CreateProfileRequest? capturedRequest = null;
        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync((CreateProfileRequest req, CancellationToken _) =>
                ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "profile-pooled-id", Name = req.Name }));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.CreateProfileForReplayAsync(replay);

        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        Assert.Equal("1.82826.generalsonline.gameclient.60hz", capturedRequest.GameClientId);
        Assert.NotNull(capturedRequest.GameClient);
        Assert.Equal("1.82826.generalsonline.gameclient.60hz", capturedRequest.GameClient.Id);
        Assert.Equal("profile-pooled-id", replay.MatchingProfileId);
        Assert.Equal(ReplayCompatibilityStatus.Compatible, replay.CompatibilityStatus);
    }

    /// <summary>
    /// Verifies that an existing profile with a different client version is not matched by IsProfileMatchingThirdParty, avoiding desyncs.
    /// </summary>
    [Fact]
    public void IsProfileMatchingThirdParty_WhenExistingProfileHasDifferentClientVersion_ReturnsFalse()
    {
        var olderProfile = new GameProfile
        {
            Id = "older-go-profile-060526",
            Name = "GeneralsOnline 060526 Profile",
            GameClient = new GameClient
            {
                Id = "1.60526.generalsonline.gameclient.zerohour",
                Name = "GeneralsOnline 060526",
                Version = "060526",
                GameType = GameType.ZeroHour,
                PublisherType = "generalsonline",
            },
            EnabledContentIds =
            [
                "1.104.retail.gameinstallation.zerohour",
                "1.60526.generalsonline.gameclient.zerohour",
            ],
        };

        var matchingProfile = new GameProfile
        {
            Id = "matching-go-profile-082826",
            Name = "GeneralsOnline 082826 Profile",
            GameClient = new GameClient
            {
                Id = "1.828261.generalsonline.gameclient.zerohour",
                Name = "GeneralsOnline 082826",
                Version = "082826",
                GameType = GameType.ZeroHour,
                PublisherType = "generalsonline",
            },
            EnabledContentIds =
            [
                "1.104.retail.gameinstallation.zerohour",
                "1.828261.generalsonline.gameclient.zerohour",
            ],
        };

        // When version differs: returns false
        var matchesOlder = ReplayDirectoryService.IsProfileMatchingThirdParty(
            olderProfile, "1.828261.generalsonline.gameclient.zerohour", null, "082826");
        Assert.False(matchesOlder);

        // When version has zero segment and target does not: returns false
        var matchesZero = ReplayDirectoryService.IsProfileMatchingThirdParty(
            olderProfile, "1.0.generalsonline.gameclient.zerohour", null, null);
        Assert.False(matchesZero);

        // When version matches: returns true
        var matchesCurrent = ReplayDirectoryService.IsProfileMatchingThirdParty(
            matchingProfile, "1.828261.generalsonline.gameclient.zerohour", null, "082826");
        Assert.True(matchesCurrent);
    }

    /// <summary>
    /// Verifies that ResolveCompatibility does not assign an existing profile when its client version differs.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveCompatibility_WhenExistingProfileHasDifferentClientVersion_DoesNotMatchOlderProfileAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "match_082826.rep",
            FullPath = "/replays/match_082826.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x6DBF4405,
                IniCrc = 0x51ACED23,
            },
        };

        var entry = new CrcMappingEntry
        {
            ExeCrc = "0x6DBF4405",
            IniCrc = "0x51ACED23",
            ManifestId = "1.82826.generalsonline.gameclient.zerohour",
            Publisher = "generalsonline",
            GameType = "ZeroHour",
            Version = "082826",
            Description = "GeneralsOnline 082826",
        };

        CrcMappingEntry? outEntry = entry;
        _mockCrcRegistry
            .Setup(r => r.TryGetEntry("0x6DBF4405", "0x51ACED23", out outEntry))
            .Returns(true);

        var olderProfile = new GameProfile
        {
            Id = "older-go-profile-060526",
            Name = "GeneralsOnline 060526 Profile",
            GameClient = new GameClient
            {
                Id = "1.60526.generalsonline.gameclient.zerohour",
                Name = "GeneralsOnline 060526",
                Version = "060526",
                GameType = GameType.ZeroHour,
                PublisherType = "generalsonline",
            },
            EnabledContentIds =
            [
                "1.104.retail.gameinstallation.zerohour",
                "1.60526.generalsonline.gameclient.zerohour",
            ],
        };

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        // With only older profile in list, should not match
        await service.ResolveCompatibilityAsync(replay, new HashSet<string>(), [olderProfile]);
        Assert.NotEqual("older-go-profile-060526", replay.MatchingProfileId);
        Assert.NotEqual(ReplayCompatibilityStatus.Compatible, replay.CompatibilityStatus);

        // With matching profile in list, should match
        var matchingProfile = new GameProfile
        {
            Id = "matching-go-profile-082826",
            Name = "GeneralsOnline 082826 Profile",
            GameClient = new GameClient
            {
                Id = "1.828261.generalsonline.gameclient.zerohour",
                Name = "GeneralsOnline 082826",
                Version = "082826",
                GameType = GameType.ZeroHour,
                PublisherType = "generalsonline",
            },
            EnabledContentIds =
            [
                "1.104.retail.gameinstallation.zerohour",
                "1.828261.generalsonline.gameclient.zerohour",
            ],
        };

        var replay2 = new ReplayFile
        {
            FileName = "match_082826.rep",
            FullPath = "/replays/match_082826.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x6DBF4405,
                IniCrc = 0x51ACED23,
            },
        };

        await service.ResolveCompatibilityAsync(replay2, new HashSet<string>(), [matchingProfile]);
        Assert.Equal("matching-go-profile-082826", replay2.MatchingProfileId);
        Assert.Equal(ReplayCompatibilityStatus.Compatible, replay2.CompatibilityStatus);
    }

    /// <summary>
    /// Verifies that ResolveCompatibility sets Unknown status when either ExeCrc or IniCrc is missing.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveCompatibility_WhenIniCrcOrExeCrcMissing_SetsUnknownStatusAsync()
    {
        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var replayNoIni = new ReplayFile
        {
            FileName = "no_ini.rep",
            FullPath = "/replays/no_ini.rep",
            GameVersion = GameType.ZeroHour,
            SizeInBytes = 100,
            LastModified = DateTime.UtcNow,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x6DBF4405,
                IniCrc = null,
            },
        };

        await service.ResolveCompatibilityAsync(replayNoIni, new HashSet<string>(), []);
        Assert.Equal(ReplayCompatibilityStatus.Unknown, replayNoIni.CompatibilityStatus);

        var replayNoExe = new ReplayFile
        {
            FileName = "no_exe.rep",
            FullPath = "/replays/no_exe.rep",
            GameVersion = GameType.ZeroHour,
            SizeInBytes = 100,
            LastModified = DateTime.UtcNow,
            Metadata = new ReplayMetadata
            {
                ExeCrc = null,
                IniCrc = 0x51ACED23,
            },
        };

        await service.ResolveCompatibilityAsync(replayNoExe, new HashSet<string>(), []);
        Assert.Equal(ReplayCompatibilityStatus.Unknown, replayNoExe.CompatibilityStatus);
    }

    /// <summary>
    /// Verifies that LaunchReplayAsync rejects launching when the matching profile is already running.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LaunchReplayAsync_WhenProfileAlreadyRunning_ReturnsFailureAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "Match1.rep",
            FullPath = "/replays/Match1.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            MatchingProfileId = "running-profile-id",
        };

        _mockLauncherFacade
            .Setup(l => l.GetLaunchStatusAsync("running-profile-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProcessInfo>.CreateSuccess(new GameProcessInfo { IsRunning = true }));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.LaunchReplayAsync(replay);

        Assert.False(result.Success);
        Assert.Contains("already running", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that CreateProfileForReplayAsync resolves client dependencies and uses the preferred workspace strategy.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_WhenClientHasDependencies_ResolvesAndIncludesDependenciesInProfileAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "MatchGO60.rep",
            FullPath = "/replays/MatchGO60.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x6DBF4405,
                IniCrc = 0x51ACED23,
            },
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0x6DBF4405",
                IniCrc = "0x51ACED23",
                ManifestId = "1.828261.generalsonline.gameclient.60hz",
                Publisher = "generalsonline",
                GameType = "ZeroHour",
                Version = "082826",
                Description = "GeneralsOnline 60Hz",
            },
        };

        var installation = new GameInstallation("/steam/zh", GameInstallationType.Steam)
        {
            Id = "steam-zh-1",
            HasZeroHour = true,
            ZeroHourPath = "/steam/zh",
        };

        _mockDependencyResolver
            .Setup(r => r.ResolveDependenciesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) => new HashSet<string>(ids));

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        _mockConfigurationProvider
            .Setup(c => c.GetDefaultWorkspaceStrategy())
            .Returns(WorkspaceStrategy.SymlinkOnly);

        var mapPackDependency = new ContentDependency
        {
            Id = ManifestId.Create("1.828261.generalsonline.mappack.quickmatch-maps"),
            DependencyType = GenHub.Core.Models.Enums.ContentType.MapPack,
            PublisherType = "generalsonline",
        };

        var clientManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.828261.generalsonline.gameclient.60hz"),
            Name = "GeneralsOnline 60Hz",
            ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "generalsonline" },
            Dependencies = [mapPackDependency],
        };

        var mapPackManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.828261.generalsonline.mappack.quickmatch-maps"),
            Name = "GeneralsOnline QuickMatch Maps",
            ContentType = GenHub.Core.Models.Enums.ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "generalsonline" },
        };

        _mockManifestPool
            .Setup(m => m.GetManifestAsync(ManifestId.Create("1.828261.generalsonline.gameclient.60hz"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(clientManifest));

        _mockManifestPool
            .Setup(m => m.GetManifestAsync(ManifestId.Create("1.828261.generalsonline.mappack.quickmatch-maps"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(mapPackManifest));

        _mockManifestPool
            .Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([clientManifest]));

        _mockDependencyResolver
            .Setup(d => d.ResolveDependenciesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) => new HashSet<string>(ids));

        CreateProfileRequest? capturedRequest = null;
        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync((CreateProfileRequest req, CancellationToken _) =>
                ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "go-dep-profile", Name = req.Name }));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.CreateProfileForReplayAsync(replay);

        Assert.True(result.Success, result.FirstError ?? "Profile creation failed");
        Assert.NotNull(capturedRequest);
        Assert.Equal(WorkspaceStrategy.SymlinkOnly, capturedRequest.WorkspaceStrategy);
        Assert.False(capturedRequest.UseSteamLaunch);
        Assert.NotNull(capturedRequest.EnabledContentIds);
        Assert.Contains("1.828261.generalsonline.mappack.quickmatch-maps", capturedRequest.EnabledContentIds);
    }

    /// <summary>
    /// Verifies that LaunchReplayAsync directly delegates to IProfileLauncherFacade without mutating the profile or forcing UseSteamLaunch.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LaunchReplayAsync_WhenExistingProfileMatches_DelegatesDirectlyToLauncherFacadeWithoutMutatingProfileAsync()
    {
        var existingProfileId = "1878b44e26d04d17a29b6c09dcbc0d69";
        var replay = new ReplayFile
        {
            FileName = "MatchGO_Existing.rep",
            FullPath = "/replays/MatchGO_Existing.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            MatchingProfileId = existingProfileId,
            CompatibilityStatus = ReplayCompatibilityStatus.Compatible,
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0x6DBF4405",
                IniCrc = "0x51ACED23",
                ManifestId = "1.828261.generalsonline.gameclient.60hz",
                Publisher = "generalsonline",
                GameType = "ZeroHour",
            },
        };

        var launchInfo = new GameLaunchInfo
        {
            LaunchId = "launch-test-1",
            ProfileId = existingProfileId,
            WorkspaceId = "ws-test-1",
            ProcessInfo = new GameProcessInfo
            {
                ProcessId = 12345,
                ExecutablePath = "/steam/zh/generals.exe",
            },
        };

        var existingProfile = new GameProfile
        {
            Id = existingProfileId,
            Name = "Existing Profile",
            GameClient = new GameClient
            {
                Id = "1.828261.generalsonline.gameclient.60hz",
                PublisherType = "generalsonline",
                GameType = GameType.ZeroHour,
            },
            EnabledContentIds = ["1.828261.generalsonline.gameclient.60hz"],
        };

        _mockProfileManager
            .Setup(p => p.GetProfileAsync(existingProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));

        _mockLauncherFacade
            .Setup(l => l.LaunchProfileAsync(existingProfileId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.LaunchReplayAsync(replay);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(12345, result.Data.ProcessInfo?.ProcessId);
        _mockLauncherFacade.Verify(l => l.LaunchProfileAsync(existingProfileId, true, It.IsAny<CancellationToken>()), Times.Once());
        _mockProfileManager.Verify(p => p.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    /// <summary>
    /// Verifies that FindMatchingProfile deterministically prefers a profile dedicated to the replay over a general profile or a profile created for another replay.
    /// </summary>
    [Fact]
    public void FindMatchingProfile_WhenDedicatedReplayProfileExists_PrefersDedicatedProfileOverGeneralAndOtherReplayProfiles()
    {
        var replay = new ReplayFile
        {
            FileName = "match_123.rep",
            FullPath = "/replays/match_123.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
        };

        var otherReplayProfile = new GameProfile
        {
            Id = "profile-other",
            Name = "GeneralsOnline 60Hz (Replay: other_replay)",
            GameClient = new GameClient { Id = "1.828261.generalsonline.gameclient.zerohour", GameType = GameType.ZeroHour, PublisherType = "generalsonline" },
            EnabledContentIds = ["1.828261.generalsonline.patch.gamedata"],
        };

        var generalProfile = new GameProfile
        {
            Id = "profile-general",
            Name = "GeneralsOnline 60Hz Custom",
            GameClient = new GameClient { Id = "1.828261.generalsonline.gameclient.zerohour", GameType = GameType.ZeroHour, PublisherType = "generalsonline" },
            EnabledContentIds = ["1.828261.generalsonline.patch.gamedata"],
        };

        var dedicatedProfile = new GameProfile
        {
            Id = "profile-dedicated",
            Name = "GeneralsOnline 60Hz (Replay: match_123)",
            GameClient = new GameClient { Id = "1.828261.generalsonline.gameclient.zerohour", GameType = GameType.ZeroHour, PublisherType = "generalsonline" },
            EnabledContentIds = ["1.828261.generalsonline.patch.gamedata"],
        };

        // Adversarial order: other replay profile first, then general, then dedicated
        var profiles = new[] { otherReplayProfile, generalProfile, dedicatedProfile };

        var match = ReplayDirectoryService.FindMatchingProfile(
            profiles,
            GameType.ZeroHour,
            "1.828261.generalsonline.gameclient.zerohour",
            "1.828261.generalsonline.patch.gamedata",
            replay);

        Assert.NotNull(match);
        Assert.Equal("profile-dedicated", match.Id);
    }

    /// <summary>
    /// Verifies that FindMatchingProfile prefers a general user profile over an auto-created profile for a different replay.
    /// </summary>
    [Fact]
    public void FindMatchingProfile_WhenNoDedicatedProfileExists_PrefersGeneralProfileOverOtherReplayProfile()
    {
        var replay = new ReplayFile
        {
            FileName = "new_match.rep",
            FullPath = "/replays/new_match.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
        };

        var otherReplayProfile = new GameProfile
        {
            Id = "profile-other-replay",
            Name = "GeneralsOnline 60Hz (Replay: old_match_999)",
            Description = "[replay:old_match_999.rep] Profile configured for replay old_match_999",
            GameClient = new GameClient { Id = "1.828261.generalsonline.gameclient.zerohour", GameType = GameType.ZeroHour, PublisherType = "generalsonline" },
            EnabledContentIds = ["1.828261.generalsonline.patch.gamedata"],
        };

        var generalProfile = new GameProfile
        {
            Id = "profile-user-general",
            Name = "My GeneralsOnline Setup",
            GameClient = new GameClient { Id = "1.828261.generalsonline.gameclient.zerohour", GameType = GameType.ZeroHour, PublisherType = "generalsonline" },
            EnabledContentIds = ["1.828261.generalsonline.patch.gamedata"],
        };

        var profiles = new[] { otherReplayProfile, generalProfile };

        var match = ReplayDirectoryService.FindMatchingProfile(
            profiles,
            GameType.ZeroHour,
            "1.828261.generalsonline.gameclient.zerohour",
            "1.828261.generalsonline.patch.gamedata",
            replay);

        Assert.NotNull(match);
        Assert.Equal("profile-user-general", match.Id);
    }

    /// <summary>
    /// Verifies that FindMatchingProfile prioritizes profiles with exact manifest and patch matches.
    /// </summary>
    [Fact]
    public void FindMatchingProfile_WhenExactManifestAndDataPatchMatch_PrefersExactMatchOverGenericPublisherMatch()
    {
        var replay = new ReplayFile
        {
            FileName = "match_test.rep",
            FullPath = "/replays/match_test.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
        };

        var genericPublisherProfile = new GameProfile
        {
            Id = "profile-generic",
            Name = "GeneralsOnline Generic",
            GameClient = new GameClient { Id = "1.828261.generalsonline.gameclient.standard", GameType = GameType.ZeroHour, PublisherType = "generalsonline" },
            EnabledContentIds = ["1.100.generalsonline.patch.old"],
        };

        var exactMatchProfile = new GameProfile
        {
            Id = "profile-exact",
            Name = "GeneralsOnline Exact",
            GameClient = new GameClient { Id = "1.828261.generalsonline.gameclient.60hz", GameType = GameType.ZeroHour, PublisherType = "generalsonline" },
            EnabledContentIds = ["1.828261.generalsonline.patch.gamedata"],
        };

        var profiles = new[] { genericPublisherProfile, exactMatchProfile };

        var match = ReplayDirectoryService.FindMatchingProfile(
            profiles,
            GameType.ZeroHour,
            "1.828261.generalsonline.gameclient.60hz",
            "1.828261.generalsonline.patch.gamedata",
            replay);

        Assert.NotNull(match);
        Assert.Equal("profile-exact", match.Id);
    }

    /// <summary>
    /// Verifies that FindMatchingProfile deterministically breaks ties alphabetically by Name, then by Id.
    /// </summary>
    [Fact]
    public void FindMatchingProfile_WhenProfilesTiedInScore_DeterministicallyBreaksTieByNameThenId()
    {
        var replay = new ReplayFile
        {
            FileName = "match_tie.rep",
            FullPath = "/replays/match_tie.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
        };

        var profileZ = new GameProfile
        {
            Id = "id-2",
            Name = "Zeta Profile",
            GameClient = new GameClient { Id = "1.828261.generalsonline.gameclient.60hz", GameType = GameType.ZeroHour, PublisherType = "generalsonline" },
            EnabledContentIds = ["1.828261.generalsonline.patch.gamedata"],
        };

        var profileA = new GameProfile
        {
            Id = "id-1",
            Name = "Alpha Profile",
            GameClient = new GameClient { Id = "1.828261.generalsonline.gameclient.60hz", GameType = GameType.ZeroHour, PublisherType = "generalsonline" },
            EnabledContentIds = ["1.828261.generalsonline.patch.gamedata"],
        };

        var profiles = new[] { profileZ, profileA };

        var match = ReplayDirectoryService.FindMatchingProfile(
            profiles,
            GameType.ZeroHour,
            "1.828261.generalsonline.gameclient.60hz",
            "1.828261.generalsonline.patch.gamedata",
            replay);

        Assert.NotNull(match);
        Assert.Equal("id-1", match.Id);
        Assert.Equal("Alpha Profile", match.Name);
    }

    /// <summary>
    /// Verifies that FindMatchingProfile matches a profile with Community Patch client to a retail 1.04 replay.
    /// </summary>
    [Fact]
    public void FindMatchingProfile_WhenRetailReplay_MatchesCommunityPatchProfile()
    {
        var replay = new ReplayFile
        {
            FileName = "00000000.rep",
            FullPath = "/replays/00000000.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            MatchedClient = new CrcMappingEntry
            {
                Publisher = "retail",
                ManifestId = "1.104.retail.gameclient.zerohour",
                Version = "1.04",
            },
        };

        var communityProfile = new GameProfile
        {
            Id = "community-id",
            Name = "Community Patch (TheSuperHackers Build)",
            GameClient = new GameClient
            {
                Id = "1.20260827.communityoutpost.gameclient.communitypatch",
                GameType = GameType.ZeroHour,
                PublisherType = "communityoutpost",
            },
            EnabledContentIds = ["1.20260827.communityoutpost.gameclient.communitypatch"],
        };

        var match = ReplayDirectoryService.FindMatchingProfile(
            [communityProfile],
            GameType.ZeroHour,
            "1.104.retail.gameclient.zerohour",
            null,
            replay);

        Assert.NotNull(match);
        Assert.Equal("community-id", match.Id);
    }

    /// <summary>
    /// Verifies that FindMatchingProfile does not match an incompatible third-party client (e.g. GeneralsOnline) to a retail replay.
    /// </summary>
    [Fact]
    public void FindMatchingProfile_WhenRetailReplay_DoesNotMatchIncompatibleThirdPartyProfile()
    {
        var replay = new ReplayFile
        {
            FileName = "00000000.rep",
            FullPath = "/replays/00000000.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            MatchedClient = new CrcMappingEntry
            {
                Publisher = "retail",
                ManifestId = "1.104.retail.gameclient.zerohour",
                Version = "1.04",
            },
        };

        var genOnlineProfile = new GameProfile
        {
            Id = "genonline-id",
            Name = "Generals Online Profile",
            GameClient = new GameClient
            {
                Id = "1.100.generalsonline.gameclient.zerohour",
                GameType = GameType.ZeroHour,
                PublisherType = "generalsonline",
            },
            EnabledContentIds = ["1.100.generalsonline.gameclient.zerohour"],
        };

        var match = ReplayDirectoryService.FindMatchingProfile(
            [genOnlineProfile],
            GameType.ZeroHour,
            "1.104.retail.gameclient.zerohour",
            null,
            replay);

        Assert.Null(match);
    }

    /// <summary>
    /// Verifies that FindMatchingProfile does not match a TheSuperHackers weekly profile to a retail 1.04 replay.
    /// </summary>
    [Fact]
    public void FindMatchingProfile_WhenRetailReplay_DoesNotMatchTheSuperHackersWeeklyProfile()
    {
        var replay = new ReplayFile
        {
            FileName = "00000000.rep",
            FullPath = "/replays/00000000.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            MatchedClient = new CrcMappingEntry
            {
                Publisher = "retail",
                ManifestId = "1.104.retail.gameclient.zerohour",
                Version = "1.04",
                ExeCrc = ReplayManagerConstants.RetailZeroHourExeCrcSteam,
            },
            Metadata = new ReplayMetadata
            {
                ExeCrc = ReplayManagerConstants.RetailZeroHourExeCrcSteamValue,
                IniCrc = 0x76B251A3,
            },
        };

        var tshProfile = new GameProfile
        {
            Id = "tsh-weekly-id",
            Name = "TheSuperHackers ZeroHour Weekly",
            GameClient = new GameClient
            {
                Id = "1.20260821.thesuperhackers.gameclient.zerohour",
                GameType = GameType.ZeroHour,
                PublisherType = PublisherTypeConstants.TheSuperHackers,
                ExecutablePath = GameClientConstants.SuperHackersZeroHourExecutable,
            },
            EnabledContentIds = ["1.20260821.thesuperhackers.gameclient.zerohour"],
        };

        var match = ReplayDirectoryService.FindMatchingProfile(
            [tshProfile],
            GameType.ZeroHour,
            "1.104.retail.gameclient.zerohour",
            null,
            replay);

        Assert.Null(match);
    }

    /// <summary>
    /// Verifies that FindCompatibleProfiles excludes a TheSuperHackers weekly profile with a non-retail EXE CRC for a retail 1.04 replay.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FindCompatibleProfilesAsync_WhenProfileIsTheSuperHackersWeekly_ExcludesProfileForRetailReplayAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "genhub_test_tsh_weekly_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fakeExePath = Path.Combine(tempDir, "generalszh.exe");
        File.WriteAllText(fakeExePath, "fake-binary-content");

        try
        {
            var replay = new ReplayFile
            {
                FileName = "RetailZH104.rep",
                FullPath = "/test/RetailZH104.rep",
                SizeInBytes = 2048,
                LastModified = DateTime.UtcNow,
                GameVersion = GameType.ZeroHour,
                MatchedClient = new CrcMappingEntry
                {
                    ManifestId = "1.104.retail.gameclient.zerohour",
                    ExeCrc = ReplayManagerConstants.RetailZeroHourExeCrcSteam,
                },
                Metadata = new ReplayMetadata
                {
                    ExeCrc = ReplayManagerConstants.RetailZeroHourExeCrcSteamValue,
                    IniCrc = 0x76B251A3,
                },
            };

            var tshProfile = new GameProfile
            {
                Id = "tsh-profile",
                Name = "TheSuperHackers Weekly Profile",
                GameClient = new GameClient
                {
                    Id = "1.20260821.thesuperhackers.gameclient.zerohour",
                    GameType = GameType.ZeroHour,
                    PublisherType = PublisherTypeConstants.TheSuperHackers,
                    ExecutablePath = fakeExePath,
                },
                EnabledContentIds = ["1.20260821.thesuperhackers.gameclient.zerohour"],
            };

            var mockCrcCalc = new Mock<IGameCrcCalculatorService>();
            mockCrcCalc
                .Setup(c => c.CalculateExeCrcAsync(fakeExePath, It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess("0x27533BB0"));

            var profiles = new List<GameProfile> { tshProfile };
            var results = await ReplayDirectoryService.FindCompatibleProfilesAsync(
                profiles,
                replay,
                null,
                mockCrcCalc.Object);

            Assert.Empty(results);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Verifies that LaunchReplayAsync clears incompatible profile reference and creates a compatible profile when the referenced profile is third-party but the replay is retail.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LaunchReplayAsync_WhenAssociatedProfileIsIncompatible_ClearsReferenceAndCreatesNewProfileAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "00000000.rep",
            FullPath = "/replays/00000000.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            MatchingProfileId = "incompatible-community-profile",
            MatchingProfileName = "Community Patch (TheSuperHackers Build)",
            CompatibilityStatus = ReplayCompatibilityStatus.Compatible,
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0x828261",
                IniCrc = "0x000000",
                ManifestId = "1.0.ea.gameinstallation.zerohour",
                Publisher = "ea",
                GameType = "ZeroHour",
                Version = "1.04",
                Description = "Command and Conquer Zero Hour 1.04 Retail",
            },
        };

        var incompatibleProfile = new GameProfile
        {
            Id = "incompatible-community-profile",
            Name = "Community Patch (TheSuperHackers Build)",
            GameClient = new GameClient
            {
                Id = "1.20260827.communityoutpost.gameclient.communitypatch",
                GameType = GameType.ZeroHour,
                PublisherType = "communityoutpost",
            },
            EnabledContentIds = ["1.20260827.communityoutpost.gameclient.communitypatch"],
        };

        var retailClient = new GameClient
        {
            Id = "1.104.retail.gameclient.zerohour",
            Name = "Command and Conquer Generals Zero Hour (Retail)",
            Version = "1.04",
            GameType = GameType.ZeroHour,
            PublisherType = "retail",
            InstallationId = "retail-inst-1",
            ExecutablePath = "/retail/generalszh.exe",
            WorkingDirectory = "/retail",
        };

        var installation = new GameInstallation("/retail", GameInstallationType.CDISO)
        {
            Id = "retail-inst-1",
            HasZeroHour = true,
            ZeroHourPath = "/retail",
            AvailableGameClients = [retailClient],
        };

        _mockDependencyResolver
            .Setup(r => r.ResolveDependenciesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) => new HashSet<string>(ids));

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        _mockProfileManager
            .Setup(p => p.GetProfileAsync("incompatible-community-profile", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(incompatibleProfile));

        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateProfileRequest req, CancellationToken _) =>
                ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "created-retail-profile", Name = req.Name }));

        var launchInfo = new GameLaunchInfo
        {
            LaunchId = "launch-retail-100",
            ProfileId = "created-retail-profile",
            WorkspaceId = "ws-retail-100",
            ProcessInfo = new GameProcessInfo
            {
                ProcessId = 65432,
                ExecutablePath = "/retail/generalszh.exe",
            },
        };

        _mockLauncherFacade
            .Setup(l => l.LaunchProfileAsync("created-retail-profile", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.LaunchReplayAsync(replay);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("created-retail-profile", replay.MatchingProfileId);
        Assert.Equal(ReplayCompatibilityStatus.Compatible, replay.CompatibilityStatus);
        Assert.Equal("launch-retail-100", result.Data.LaunchId);

        _mockProfileManager.Verify(p => p.GetProfileAsync("incompatible-community-profile", It.IsAny<CancellationToken>()), Times.Once);
        _mockProfileManager.Verify(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that CreateProfileForReplayAsync for a SuperHackers replay assigns generalszh.exe rather than retail generals.exe.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_WhenSuperHackersClient_ResolvesGeneralsZhExecutableAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "Match_TSH.rep",
            FullPath = "/replays/Match_TSH.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x27533BB0,
                IniCrc = 0x76B251A3,
            },
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0x27533BB0",
                IniCrc = "0x76B251A3",
                ManifestId = "1.20260821.thesuperhackers.gameclient.zerohour",
                Publisher = "thesuperhackers",
                GameType = "ZeroHour",
                Version = "2026-08-21",
                Description = "TheSuperHackers 2026-08-21",
            },
        };

        var installation = new GameInstallation("/games/ZeroHour", GameInstallationType.Retail)
        {
            HasZeroHour = true,
            ZeroHourPath = "/games/ZeroHour",
            AvailableGameClients =
            [
                new GameClient
                {
                    ExecutablePath = "/games/ZeroHour/generals.exe",
                    WorkingDirectory = "/games/ZeroHour",
                    GameType = GameType.ZeroHour,
                },
            ],
        };

        _mockDependencyResolver
            .Setup(r => r.ResolveDependenciesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) => new HashSet<string>(ids));

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        CreateProfileRequest? capturedRequest = null;
        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync((CreateProfileRequest req, CancellationToken _) =>
                ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "profile-tsh-1", Name = req.Name }));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.CreateProfileForReplayAsync(replay);

        Assert.True(result.Success);
        Assert.NotNull(capturedRequest?.GameClient);
        Assert.EndsWith(GameClientConstants.SuperHackersZeroHourExecutable, capturedRequest.GameClient.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.False(capturedRequest.UseSteamLaunch);
    }

    /// <summary>
    /// Verifies that CreateProfileForReplayAsync preserves relative subdirectory layout when detected client executable lives in a subdirectory.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_WhenDetectedClientInSubdirectory_PreservesSubdirectoryLayoutAsync()
    {
        var installDir = Path.Combine(Path.GetTempPath(), "GenHubTests", "ZH");
        var expectedExePath = Path.Combine(installDir, "mods", "custom", "superhackers.exe");

        var replay = new ReplayFile
        {
            FileName = "Match_Subdir.rep",
            FullPath = "/replays/Match_Subdir.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x27533BB0,
                IniCrc = 0x76B251A3,
            },
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0x27533BB0",
                IniCrc = "0x76B251A3",
                ManifestId = "1.20260821.thesuperhackers.gameclient.zerohour",
                Publisher = "thesuperhackers",
                GameType = "ZeroHour",
                Version = "2026-08-21",
                Description = "TheSuperHackers 2026-08-21",
            },
        };

        var installation = new GameInstallation(installDir, GameInstallationType.Retail)
        {
            HasZeroHour = true,
            ZeroHourPath = installDir,
            AvailableGameClients =
            [
                new GameClient
                {
                    ExecutablePath = expectedExePath,
                    WorkingDirectory = installDir,
                    PublisherType = "thesuperhackers",
                    GameType = GameType.ZeroHour,
                },
            ],
        };

        _mockDependencyResolver
            .Setup(r => r.ResolveDependenciesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) => new HashSet<string>(ids));

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        CreateProfileRequest? capturedRequest = null;
        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync((CreateProfileRequest req, CancellationToken _) =>
                ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "profile-sub-1", Name = req.Name }));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.CreateProfileForReplayAsync(replay);

        Assert.True(result.Success);
        Assert.NotNull(capturedRequest?.GameClient);
        Assert.Equal(expectedExePath, capturedRequest.GameClient.ExecutablePath);
    }

    /// <summary>
    /// Verifies that IsProfileMatchingThirdParty rejects a profile with an incompatible data patch version.
    /// </summary>
    [Fact]
    public void IsProfileMatchingThirdParty_WhenDataPatchVersionDiffers_ReturnsFalse()
    {
        var profile = new GameProfile
        {
            Id = "profile-qfe1",
            GameClient = new GameClient
            {
                Id = "1.82826.generalsonline.gameclient.zerohour",
                PublisherType = "generalsonline",
            },
            EnabledContentIds =
            [
                "1.828261.generalsonline.patch.gamedata",
            ],
        };

        // Target requires base patch 1.82826, but profile has QFE1 patch 1.828261
        var matches = ReplayDirectoryService.IsProfileMatchingThirdParty(
            profile,
            "1.82826.generalsonline.gameclient.zerohour",
            "1.82826.generalsonline.patch.gamedata");

        Assert.False(matches);
    }

    /// <summary>
    /// Verifies that ResolveInstallation prioritizes the installation whose publisher matches the replay client.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_WhenMultipleInstallationsExist_PrioritizesPublisherMatchAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "SteamMatch.rep",
            FullPath = "/replays/SteamMatch.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x401D89EA,
                IniCrc = 0x76B251A3,
            },
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0x401D89EA",
                IniCrc = "0x76B251A3",
                ManifestId = "1.104.steam.gameclient.zerohour",
                Publisher = "Steam",
                GameType = "ZeroHour",
                Version = "1.04",
                Description = "Zero Hour Steam",
            },
        };

        var retailInstall = new GameInstallation("/games/Retail", GameInstallationType.Retail)
        {
            Id = "retail-id",
            HasZeroHour = true,
            ZeroHourPath = "/games/Retail",
        };

        var steamInstall = new GameInstallation("/games/Steam", GameInstallationType.Steam)
        {
            Id = "steam-id",
            HasZeroHour = true,
            ZeroHourPath = "/games/Steam",
        };

        _mockDependencyResolver
            .Setup(r => r.ResolveDependenciesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) => new HashSet<string>(ids));

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([retailInstall, steamInstall]));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        CreateProfileRequest? capturedRequest = null;
        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync((CreateProfileRequest req, CancellationToken _) =>
                ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "steam-matched-profile", Name = req.Name }));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.CreateProfileForReplayAsync(replay);

        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        Assert.Equal("steam-id", capturedRequest.GameInstallationId);
    }

    /// <summary>
    /// Verifies that ResolveThirdPartyRelativeExePath preserves relative paths located within the working directory.
    /// </summary>
    [Fact]
    public void ResolveThirdPartyRelativeExePath_WhenPathIsWithinWorkingDir_PreservesRelativePath()
    {
        var workingDir = Path.Combine(Path.GetTempPath(), "genhub-test-dir");
        var relativeExe = Path.Combine("mods", "client", "genpatcher.exe");
        var client = new GameClient
        {
            PublisherType = "TheSuperHackers",
            ExecutablePath = relativeExe,
        };
        var replay = CreateTestReplayForPathResolution("TheSuperHackers");

        var result = ReplayDirectoryService.ResolveThirdPartyRelativeExePath(client, workingDir, null, replay);

        Assert.Equal(relativeExe, result);
    }

    /// <summary>
    /// Verifies that ResolveThirdPartyRelativeExePath falls back to the default executable name when a path escapes the working directory.
    /// </summary>
    [Fact]
    public void ResolveThirdPartyRelativeExePath_WhenPathEscapesWorkingDir_FallsBackToDefaultExecutableName()
    {
        var workingDir = Path.Combine(Path.GetTempPath(), "genhub-test-dir");
        var escapingExe = Path.Combine("..", "..", "escaped.exe");
        var client = new GameClient
        {
            PublisherType = "TheSuperHackers",
            ExecutablePath = escapingExe,
        };
        var replay = CreateTestReplayForPathResolution("TheSuperHackers");

        var result = ReplayDirectoryService.ResolveThirdPartyRelativeExePath(client, workingDir, null, replay);

        Assert.Equal(GameClientConstants.SuperHackersZeroHourExecutable, result);
    }

    /// <summary>
    /// Verifies that ResolveThirdPartyRelativeExePath falls back to the generals executable for the Generals game type when escaping.
    /// </summary>
    [Fact]
    public void ResolveThirdPartyRelativeExePath_WhenGeneralsGameTypeAndPathEscapes_FallsBackToGeneralsExecutable()
    {
        var workingDir = Path.Combine(Path.GetTempPath(), "genhub-test-dir");
        var escapingExe = Path.Combine("..", "..", "escaped.exe");
        var client = new GameClient
        {
            PublisherType = "GeneralsOnline",
            ExecutablePath = escapingExe,
        };
        var replay = new ReplayFile
        {
            FileName = "Generals.rep",
            FullPath = "/replays/Generals.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.Generals,
            MatchedClient = new CrcMappingEntry
            {
                Publisher = "GeneralsOnline",
            },
        };

        var result = ReplayDirectoryService.ResolveThirdPartyRelativeExePath(client, workingDir, null, replay);

        Assert.Equal("generals.exe", result);
    }

    /// <summary>
    /// Verifies that ResolveThirdPartyRelativeExePath falls back to the default executable name when the path equals the working directory.
    /// </summary>
    [Fact]
    public void ResolveThirdPartyRelativeExePath_WhenPathEqualsWorkingDir_FallsBackToDefaultExecutableName()
    {
        var workingDir = Path.Combine(Path.GetTempPath(), "genhub-test-dir");
        var client = new GameClient
        {
            PublisherType = "TheSuperHackers",
            ExecutablePath = workingDir,
        };
        var replay = CreateTestReplayForPathResolution("TheSuperHackers");

        var result = ReplayDirectoryService.ResolveThirdPartyRelativeExePath(client, workingDir, null, replay);

        Assert.Equal(GameClientConstants.SuperHackersZeroHourExecutable, result);
    }

    /// <summary>
    /// Verifies that ResolveThirdPartyRelativeExePath does not misclassify a valid filename starting with dots as an escaping path.
    /// </summary>
    [Fact]
    public void ResolveThirdPartyRelativeExePath_WhenPathStartsWithDotsInName_PreservesRelativePath()
    {
        var workingDir = Path.Combine(Path.GetTempPath(), "genhub-test-dir");
        var dotPrefixedExe = "..patched.exe";
        var client = new GameClient
        {
            PublisherType = "TheSuperHackers",
            ExecutablePath = dotPrefixedExe,
        };
        var replay = CreateTestReplayForPathResolution("TheSuperHackers");

        var result = ReplayDirectoryService.ResolveThirdPartyRelativeExePath(client, workingDir, null, replay);

        Assert.Equal(dotPrefixedExe, result);
    }

    /// <summary>
    /// Verifies that profile creation initializes the dynamic installation CAS pool path for cross-drive compatibility.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_InitializesDynamicCasPoolPathAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "PoolTest.rep",
            FullPath = "/replays/PoolTest.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x12345678,
                IniCrc = 0x87654321,
            },
            MatchedClient = null,
        };

        CrcMappingEntry? nullEntry = null;
        _mockCrcRegistry
            .Setup(r => r.TryGetEntry("0x12345678", "0x87654321", out nullEntry))
            .Returns(false);

        var installation = new GameInstallation("/games/ZeroHour", GameInstallationType.Retail)
        {
            HasZeroHour = true,
            ZeroHourPath = "/games/ZeroHour",
        };

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "test-profile-id", Name = "Test Profile" }));

        _mockCasPoolService
            .Setup(c => c.EnsurePoolPathAsync(It.IsAny<IReadOnlyList<GameInstallation>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.CreateProfileForReplayAsync(replay);

        Assert.True(result.Success);
        _mockCasPoolService.Verify(
            c => c.EnsurePoolPathAsync(It.Is<IReadOnlyList<GameInstallation>>(list => list.Contains(installation)), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that ResolveCompatibility resolves to RequiresProfile when the client manifest is installed in acquiredIds but no profile exists.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveCompatibility_WhenClientInstalledButNoProfile_ResolvesToRequiresProfileAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "MatchInstalled.rep",
            FullPath = "/replays/MatchInstalled.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x27533BB0,
                IniCrc = 0x76B251A3,
            },
        };

        var entry = new CrcMappingEntry
        {
            ExeCrc = "0x27533BB0",
            IniCrc = "0x76B251A3",
            ManifestId = "1.20260821.thesuperhackers.gameclient.zerohour",
            Publisher = "thesuperhackers",
            GameType = "ZeroHour",
            Version = "2026-08-21",
        };

        CrcMappingEntry? outEntry = entry;
        _mockCrcRegistry
            .Setup(r => r.TryGetEntry("0x27533BB0", "0x76B251A3", out outEntry))
            .Returns(true);

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var acquiredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "1.20260821.thesuperhackers.gameclient.zerohour",
        };

        await service.ResolveCompatibilityAsync(replay, acquiredIds, []);

        Assert.Equal(ReplayCompatibilityStatus.RequiresProfile, replay.CompatibilityStatus);
        Assert.Null(replay.MatchingProfileId);
        Assert.Equal("Profile Needed", replay.CompatibilityBadgeText);
    }

    /// <summary>
    /// Verifies that ResolveCompatibility resolves to Downloadable when client is not installed but has a CDN URL or third-party manifest ID.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveCompatibility_WhenNotInstalledAndHasCdnUrlOrNonRetailManifest_ResolvesToDownloadableAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "MatchDownloadable.rep",
            FullPath = "/replays/MatchDownloadable.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x6DBF4405,
                IniCrc = 0x51ACED23,
            },
        };

        var entry = new CrcMappingEntry
        {
            ExeCrc = "0x6DBF4405",
            IniCrc = "0x51ACED23",
            ManifestId = "1.82826.generalsonline.gameclient.zerohour",
            CdnUrl = "https://cdn.playgenerals.online/GeneralsOnline_portable_081326_QFE3.zip",
            Publisher = "generalsonline",
            GameType = "ZeroHour",
            Version = "082826",
        };

        CrcMappingEntry? outEntry = entry;
        _mockCrcRegistry
            .Setup(r => r.TryGetEntry("0x6DBF4405", "0x51ACED23", out outEntry))
            .Returns(true);

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        await service.ResolveCompatibilityAsync(replay, new HashSet<string>(), []);

        Assert.Equal(ReplayCompatibilityStatus.Downloadable, replay.CompatibilityStatus);
        Assert.Null(replay.MatchingProfileId);
        Assert.Equal("Download Required", replay.CompatibilityBadgeText);
    }

    /// <summary>
    /// Verifies that ResolveCompatibility resolves to Orphaned when a retail client is not installed on the system and has no CDN URL.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveCompatibility_WhenRetailClientNotInstalled_ResolvesToOrphanedAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "MatchRetailOrphaned.rep",
            FullPath = "/replays/MatchRetailOrphaned.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0xDA2B4B18,
                IniCrc = 0xFEAAE3F3,
            },
        };

        var entry = new CrcMappingEntry
        {
            ExeCrc = "0xDA2B4B18",
            IniCrc = "0xFEAAE3F3",
            ManifestId = "1.104.retail.gameclient.zerohour",
            Publisher = "retail",
            GameType = "ZeroHour",
            Version = "1.04",
            CdnUrl = null,
        };

        CrcMappingEntry? outEntry = entry;
        _mockCrcRegistry
            .Setup(r => r.TryGetEntry("0xDA2B4B18", "0xFEAAE3F3", out outEntry))
            .Returns(true);

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        await service.ResolveCompatibilityAsync(replay, new HashSet<string>(), []);

        Assert.Equal(ReplayCompatibilityStatus.Orphaned, replay.CompatibilityStatus);
        Assert.Null(replay.MatchingProfileId);
        Assert.Equal("Custom / Unmapped", replay.CompatibilityBadgeText);
    }

    /// <summary>
    /// Verifies that when an exact pair is missing, base client is not stitched to a local acquired manifest.
    /// Under the engine rule, exact (exeCRC, iniCRC) pair is required.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveCompatibility_WhenExactPairMissing_DoesNotStitchLocalAcquiredManifest_ResolvesToOrphanedAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "LocalPatchMatch.rep",
            FullPath = "/replays/LocalPatchMatch.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0xB9DB8815,
                IniCrc = 0x81FB5632,
            },
        };

        var baseClient = new CrcMappingEntry
        {
            ExeCrc = "0xB9DB8815",
            IniCrc = "0x5CB7992C",
            ManifestId = "1.828261.generalsonline.gameclient.zerohour",
            Publisher = "generalsonline",
            GameType = "ZeroHour",
            Version = "082826_QFE1",
            CdnUrl = "https://cdn.playgenerals.online/client.zip",
        };

        var knownDataPatch = new CrcMappingEntry
        {
            ExeCrc = "0x00000000",
            IniCrc = "0x81FB5632",
            ManifestId = "1.828261.generalsonline.patch.gamedata",
            DataPatchManifestId = "1.828261.generalsonline.patch.gamedata",
            DataPatchName = "GeneralsOnline GameData Patch",
            Publisher = "generalsonline",
            GameType = "ZeroHour",
            Version = "082826_QFE1",
        };

        CrcMappingEntry? nullEntry = null;
        CrcMappingEntry? outBase = baseClient;
        CrcMappingEntry? outDataPatch = knownDataPatch;
        _mockCrcRegistry
            .Setup(r => r.TryGetEntry("0xB9DB8815", "0x81FB5632", out nullEntry))
            .Returns(false);
        _mockCrcRegistry
            .Setup(r => r.TryGetEntryByExeCrc("0xB9DB8815", out outBase))
            .Returns(true);
        _mockCrcRegistry
            .Setup(r => r.TryGetEntryByIniCrc("0x81FB5632", out outDataPatch))
            .Returns(true);

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var acquiredIds = new HashSet<string>
        {
            "1.828261.generalsonline.gameclient.zerohour",
            "1.828261.generalsonline.patch.gamedata",
        };

        await service.ResolveCompatibilityAsync(replay, acquiredIds, []);

        Assert.Equal(ReplayCompatibilityStatus.Orphaned, replay.CompatibilityStatus);
        Assert.Null(replay.MatchedClient);
    }

    /// <summary>
    /// Verifies that when an exact pair is missing, catalog data patches are not stitched across base clients.
    /// Under the engine rule, exact (exeCRC, iniCRC) pair is required.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveCompatibility_WhenExactPairMissing_DoesNotStitchCatalogDataPatch_ResolvesToOrphanedAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "CatalogPatchMatch.rep",
            FullPath = "/replays/CatalogPatchMatch.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0xB9DB8815,
                IniCrc = 0x81FB5632,
            },
        };

        var baseClient = new CrcMappingEntry
        {
            ExeCrc = "0xB9DB8815",
            IniCrc = "0x5CB7992C",
            ManifestId = "1.828261.generalsonline.gameclient.zerohour",
            Publisher = "generalsonline",
            GameType = "ZeroHour",
            Version = "082826_QFE1",
            CdnUrl = "https://cdn.playgenerals.online/client.zip",
        };

        var catalogPatch = new CrcMappingEntry
        {
            ExeCrc = "0x00000000",
            IniCrc = "0x81FB5632",
            ManifestId = "1.101.thesuperhackers.patch.gamedata",
            DataPatchManifestId = "1.101.thesuperhackers.patch.gamedata",
            DataPatchName = "CommunityPatch Core INI 1.0.1 (81FB5632)",
            DataPatchCdnUrl = "https://github.com/TheSuperHackers/GeneralsGamePatch2/releases/download/1.0.1/500_900_CommunityPatch_CoreINI.zip",
            Publisher = "thesuperhackers",
            GameType = "ZeroHour",
            Version = "1.0.1",
        };

        CrcMappingEntry? nullEntry = null;
        CrcMappingEntry? outBase = baseClient;
        CrcMappingEntry? outPatch = catalogPatch;
        _mockCrcRegistry
            .Setup(r => r.TryGetEntry("0xB9DB8815", "0x81FB5632", out nullEntry))
            .Returns(false);
        _mockCrcRegistry
            .Setup(r => r.TryGetEntryByExeCrc("0xB9DB8815", out outBase))
            .Returns(true);
        _mockCrcRegistry
            .Setup(r => r.TryGetEntryByIniCrc("0x81FB5632", out outPatch))
            .Returns(true);

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        await service.ResolveCompatibilityAsync(replay, new HashSet<string>(), []);

        Assert.Equal(ReplayCompatibilityStatus.Orphaned, replay.CompatibilityStatus);
        Assert.Null(replay.MatchedClient);
    }

    /// <summary>
    /// Verifies that when an exact pair is not in the catalog, a replay is not stitched to a base client with vanilla INI.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveCompatibility_WhenGeneralsOnlineClientWithVanillaIni_WhenExactPairNotInCatalog_ResolvesToOrphanedAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "TWTF.rep",
            FullPath = "/replays/TWTF.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x45BF602F,
                IniCrc = 0xFEAAE3F3,
            },
        };

        var baseClient = new CrcMappingEntry
        {
            ExeCrc = "0x45BF602F",
            IniCrc = "0x5CB7992C",
            ManifestId = "1.329261.generalsonline.gameclient.zerohour",
            Publisher = "generalsonline",
            GameType = "ZeroHour",
            Version = "032926_QFE1",
            Description = "GeneralsOnline 032926_QFE1",
            CdnUrl = "https://cdn.playgenerals.online/GeneralsOnline_portable_032926_QFE1.zip",
        };

        CrcMappingEntry? nullEntry = null;
        CrcMappingEntry? outBase = baseClient;
        _mockCrcRegistry
            .Setup(r => r.TryGetEntry("0x45BF602F", "0xFEAAE3F3", out nullEntry))
            .Returns(false);
        _mockCrcRegistry
            .Setup(r => r.TryGetEntryByExeCrc("0x45BF602F", out outBase))
            .Returns(true);

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var acquiredIds = new HashSet<string>
        {
            "1.329261.generalsonline.gameclient.zerohour",
        };

        await service.ResolveCompatibilityAsync(replay, acquiredIds, []);

        Assert.Equal(ReplayCompatibilityStatus.Orphaned, replay.CompatibilityStatus);
        Assert.Null(replay.MatchedClient);
    }

    /// <summary>
    /// Verifies that an unknown replay with modern build time but no GeneralsOnline filename/version pattern
    /// and no matching catalog date does not resolve via heuristic to GeneralsOnline.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveCompatibility_WhenUnknownModernReplayWithoutPatternOrDateMatch_DoesNotMatchGeneralsOnlineViaHeuristicAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "UnknownCustomMatch.rep",
            FullPath = "/replays/UnknownCustomMatch.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x99999999,
                IniCrc = 0x88888888,
                BuildTimeString = "Jan 15 2025 12:00:00",
            },
        };

        var goClient = new CrcMappingEntry
        {
            ExeCrc = "0x3EAC0C69",
            IniCrc = "0x5CB7992C",
            ManifestId = "1.329262.generalsonline.gameclient.zerohour",
            Publisher = "generalsonline",
            GameType = "ZeroHour",
            Version = "032926_QFE2",
            BuildDate = "2026-03-31",
            Description = "GeneralsOnline 032926_QFE2",
            CdnUrl = "https://cdn.playgenerals.online/GeneralsOnline_portable_032926_QFE2.zip",
        };

        CrcMappingEntry? nullEntry = null;
        _mockCrcRegistry
            .Setup(r => r.TryGetEntry(It.IsAny<string>(), It.IsAny<string>(), out nullEntry))
            .Returns(false);
        _mockCrcRegistry
            .Setup(r => r.TryGetEntryByExeCrc(It.IsAny<string>(), out nullEntry))
            .Returns(false);
        _mockCrcRegistry
            .Setup(r => r.GetAllEntries())
            .Returns([goClient]);

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        await service.ResolveCompatibilityAsync(replay, [], []);

        Assert.Equal(ReplayCompatibilityStatus.Orphaned, replay.CompatibilityStatus);
        Assert.Null(replay.MatchedClient);
    }

    /// <summary>
    /// Verifies that when exact CRC pair is missing, a replay matching GeneralsOnline pattern
    /// is NOT heuristically mapped to the latest GO client and instead resolves to Orphaned.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveCompatibility_WhenGeneralsOnlineLadderReplayWithoutExactCrc_DoesNotResolveViaHeuristic_ResolvesToOrphanedAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "match_1374991_user_35b24c0a6c98950114114a3a7bfd5fe6_replay.rep",
            FullPath = "/replays/match_1374991_user_35b24c0a6c98950114114a3a7bfd5fe6_replay.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0xE981A0B4,
                IniCrc = 0xFEAAE3F3,
                BuildTimeString = "Mar 31 2026 18:16:29",
            },
        };

        var goClient = new CrcMappingEntry
        {
            ExeCrc = "0x3EAC0C69",
            IniCrc = "0x5CB7992C",
            ManifestId = "1.329262.generalsonline.gameclient.zerohour",
            Publisher = "generalsonline",
            GameType = "ZeroHour",
            Version = "032926_QFE2",
            BuildDate = "2026-03-31",
            Description = "GeneralsOnline 032926_QFE2",
            CdnUrl = "https://cdn.playgenerals.online/GeneralsOnline_portable_032926_QFE2.zip",
        };

        CrcMappingEntry? nullEntry = null;
        _mockCrcRegistry
            .Setup(r => r.TryGetEntry(It.IsAny<string>(), It.IsAny<string>(), out nullEntry))
            .Returns(false);
        _mockCrcRegistry
            .Setup(r => r.TryGetEntryByExeCrc(It.IsAny<string>(), out nullEntry))
            .Returns(false);
        _mockCrcRegistry
            .Setup(r => r.GetAllEntries())
            .Returns([goClient]);

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var acquiredIds = new HashSet<string>
        {
            "1.329262.generalsonline.gameclient.zerohour",
        };

        await service.ResolveCompatibilityAsync(replay, acquiredIds, []);

        Assert.Equal(ReplayCompatibilityStatus.Orphaned, replay.CompatibilityStatus);
        Assert.Null(replay.MatchedClient);
    }

    /// <summary>
    /// Verifies that CreateProfileForReplayAsync uses the custom game client when one is provided.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_WhenCustomGameClientProvided_UsesCustomGameClientAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "CustomClientReplay.rep",
            FullPath = "/replays/CustomClientReplay.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
        };

        var customClient = new GameClient
        {
            Id = "custom-community-client-id",
            Name = "Community Patch 1.06",
            Version = "1.06",
            PublisherType = "community",
            GameType = GameType.ZeroHour,
            ExecutablePath = "generalszh.exe",
        };

        var installation = new GameInstallation("/games/ZeroHour", GameInstallationType.Retail)
        {
            HasZeroHour = true,
            ZeroHourPath = "/games/ZeroHour",
            AvailableGameClients = [customClient],
        };

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        CreateProfileRequest? capturedRequest = null;
        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "created-custom-profile-id", Name = "Community Patch 1.06 (Replay: CustomClientReplay)" }));

        _mockCasPoolService
            .Setup(c => c.EnsurePoolPathAsync(It.IsAny<IReadOnlyList<GameInstallation>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.CreateProfileForReplayAsync(replay, customGameClient: customClient, customClientManifestId: "custom-community-client-id");

        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        Assert.NotNull(capturedRequest!.GameClient);
        Assert.Equal("Community Patch 1.06", capturedRequest.GameClient!.Name);
        Assert.Equal("created-custom-profile-id", replay.MatchingProfileId);
        Assert.Equal(ReplayCompatibilityStatus.Compatible, replay.CompatibilityStatus);
    }

    /// <summary>
    /// Verifies that CreateProfileForReplayAsync resolves the installation's base game client manifest
    /// when a retail client card is selected from the client selection dialog.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_WhenRetailCustomGameClientProvided_ResolvesInstallationBaseClientManifestAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "RetailReplay.rep",
            FullPath = "/replays/RetailReplay.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0xDA2B4B18,
            },
        };

        var customClient = new GameClient
        {
            Id = "1.104.retail.gameclient.zerohour",
            Name = "Retail 1.04",
            Version = "1.04",
            PublisherType = "Retail",
            GameType = GameType.ZeroHour,
            ExecutablePath = string.Empty,
        };

        var steamClient = new GameClient
        {
            Id = "1.104.steam.gameclient.zerohour",
            Name = "Steam Client",
            Version = "1.04",
            PublisherType = "steam",
            GameType = GameType.ZeroHour,
            ExecutablePath = "generals.exe",
            WorkingDirectory = "/games/ZeroHour",
        };

        var installation = new GameInstallation("/games/ZeroHour", GameInstallationType.Steam)
        {
            HasZeroHour = true,
            ZeroHourPath = "/games/ZeroHour",
            AvailableGameClients = [steamClient],
        };

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        CreateProfileRequest? capturedRequest = null;
        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "created-retail-profile-id", Name = "Retail 1.04 (Replay: RetailReplay)" }));

        _mockCasPoolService
            .Setup(c => c.EnsurePoolPathAsync(It.IsAny<IReadOnlyList<GameInstallation>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.CreateProfileForReplayAsync(replay, customGameClient: customClient, customClientManifestId: "1.104.retail.gameclient.zerohour");

        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        Assert.NotNull(capturedRequest!.GameClient);
        Assert.Equal("Retail 1.04", capturedRequest.GameClient!.Name);
        Assert.Equal("1.104.steam.gameclient.zerohour", capturedRequest.GameClient!.Id);
        Assert.NotNull(capturedRequest.EnabledContentIds);
        Assert.Contains("1.104.steam.gameclient.zerohour", capturedRequest.EnabledContentIds!);
        Assert.Contains("1.104.steam.gameinstallation.zerohour", capturedRequest.EnabledContentIds!);
        Assert.Equal("created-retail-profile-id", replay.MatchingProfileId);
        Assert.Equal(ReplayCompatibilityStatus.Compatible, replay.CompatibilityStatus);
    }

    /// <summary>
    /// Verifies that FindMatchingProfile reuses a compatible profile across replays when the client and patch match.
    /// </summary>
    [Fact]
    public void FindMatchingProfile_WhenProfileDedicatedToAnotherReplayWithMatchingClient_ReusesCompatibleProfile()
    {
        var replayA = new ReplayFile
        {
            FileName = "MatchA.rep",
            FullPath = "/replays/MatchA.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0xDA2B4B18,
            },
        };

        var profileForB = new GameProfile
        {
            Id = "profile-for-match-b",
            Name = "Zero Hour 1.04 (Replay: MatchB)",
            Description = "[replay:MatchB.rep] Dedicated profile for MatchB",
            GameClient = new GameClient
            {
                Id = "1.104.steam.gameclient.zerohour",
                GameType = GameType.ZeroHour,
                PublisherType = "steam",
            },
            EnabledContentIds = ["1.104.steam.gameinstallation.zerohour", "1.104.steam.gameclient.zerohour"],
        };

        var match = ReplayDirectoryService.FindMatchingProfile(
            [profileForB],
            GameType.ZeroHour,
            "1.104.steam.gameclient.zerohour",
            null,
            replayA);

        Assert.NotNull(match);
        Assert.Equal("profile-for-match-b", match.Id);
    }

    /// <summary>
    /// Verifies that LaunchReplayAsync uses explicit profile ID when provided.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LaunchReplayAsync_WhenExplicitProfileIdProvided_LaunchesExplicitProfileAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "ExplicitProfile.rep",
            FullPath = "/replays/ExplicitProfile.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
        };

        var explicitProfile = new GameProfile
        {
            Id = "explicit-user-profile-id",
            Name = "MP Custom Profile",
            GameClient = new GameClient
            {
                Id = "mp-custom-client-id",
                Name = "MP Custom",
                GameType = GameType.ZeroHour,
                PublisherType = "community",
            },
        };

        var launchInfo = new GameLaunchInfo
        {
            LaunchId = "launch-123",
            ProfileId = "explicit-user-profile-id",
            WorkspaceId = "ws-123",
            ProcessInfo = new GameProcessInfo
            {
                ProcessId = 9999,
                ExecutablePath = "/games/ZeroHour/generalszh.exe",
            },
        };

        _mockProfileManager
            .Setup(p => p.GetProfileAsync("explicit-user-profile-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(explicitProfile));

        _mockLauncherFacade
            .Setup(l => l.LaunchProfileAsync("explicit-user-profile-id", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.LaunchReplayAsync(replay, profileId: "explicit-user-profile-id");

        Assert.True(result.Success);
        Assert.Equal("explicit-user-profile-id", replay.MatchingProfileId);
        Assert.Equal("MP Custom Profile", replay.MatchingProfileName);
        _mockLauncherFacade.Verify(
            l => l.LaunchProfileAsync("explicit-user-profile-id", true, It.IsAny<CancellationToken>()),
            Times.Once());
    }

    /// <summary>
    /// Verifies that when launching an explicit profile fails, the replay is not marked as compatible with it.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LaunchReplayAsync_WhenExplicitProfileLaunchFails_DoesNotMarkReplayAsCompatibleAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "FailedExplicit.rep",
            FullPath = "/replays/FailedExplicit.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            CompatibilityStatus = ReplayCompatibilityStatus.Unknown,
        };

        _mockLauncherFacade
            .Setup(l => l.LaunchProfileAsync("failing-profile-id", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateFailure("Game process failed to start"));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.LaunchReplayAsync(replay, profileId: "failing-profile-id");

        Assert.False(result.Success);
        Assert.Null(replay.MatchingProfileId);
        Assert.Equal(ReplayCompatibilityStatus.Unknown, replay.CompatibilityStatus);
    }

    /// <summary>
    /// Verifies that when custom profile executable and INI CRCs match the replay,
    /// ResolveCompatibilityAsync resolves compatibility to that profile.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveCompatibility_WhenCustomProfileExecutableCrcMatches_ResolvesToCompatibleProfileAsync()
    {
        var tempExe = Path.GetTempFileName();
        try
        {
            var replay = new ReplayFile
            {
                FileName = "CommunityMatch.rep",
                FullPath = "/replays/CommunityMatch.rep",
                SizeInBytes = 2048,
                LastModified = DateTime.UtcNow,
                GameVersion = GameType.ZeroHour,
                Metadata = new ReplayMetadata
                {
                    ExeCrc = 0x88BEB180,
                    IniCrc = 0xFEAAE3F3,
                    BuildTimeString = "Oct 17 2005 17:31:25",
                },
            };

            var profileClient = new GameClient
            {
                Id = "custom-cp-client",
                Name = "Community Patch GameClient",
                GameType = GameType.ZeroHour,
                ExecutablePath = tempExe,
                PublisherType = "community",
                Version = "1.06",
            };

            var profile = new GameProfile
            {
                Id = "custom-cp-profile-id",
                Name = "Community Patch Profile",
                GameClient = profileClient,
            };

            var tempDir = Path.GetDirectoryName(tempExe) ?? string.Empty;

            var crcCalcMock = new Mock<IGameCrcCalculatorService>();
            crcCalcMock
                .Setup(c => c.CalculateExeCrcAsync(tempExe, It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess("0x88BEB180"));
            crcCalcMock
                .Setup(c => c.CalculateIniCrcAsync(tempDir, GameType.ZeroHour, It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess("0xFEAAE3F3"));

            CrcMappingEntry? nullEntry = null;
            _mockCrcRegistry
                .Setup(r => r.TryGetEntry("0x88BEB180", "0xFEAAE3F3", out nullEntry))
                .Returns(false);
            _mockCrcRegistry
                .Setup(r => r.TryGetEntryByExeCrc("0x88BEB180", out nullEntry))
                .Returns(false);
            _mockCrcRegistry
                .Setup(r => r.GetAllEntries())
                .Returns([]);

            var service = new ReplayDirectoryService(
                _mockHeaderParser.Object,
                _mockCrcRegistry.Object,
                _mockScopeFactory.Object,
                NullLogger<ReplayDirectoryService>.Instance,
                crcCalculator: crcCalcMock.Object);

            var acquiredIds = new HashSet<string> { "custom-cp-client" };

            await service.ResolveCompatibilityAsync(replay, acquiredIds, [profile]);

            Assert.Equal(ReplayCompatibilityStatus.Compatible, replay.CompatibilityStatus);
            Assert.Equal("custom-cp-profile-id", replay.MatchingProfileId);
            Assert.Equal("Community Patch Profile", replay.MatchingProfileName);
            Assert.NotNull(replay.MatchedClient);
            Assert.Equal("0x88BEB180", replay.MatchedClient.ExeCrc);
        }
        finally
        {
            if (File.Exists(tempExe))
            {
                File.Delete(tempExe);
            }
        }
    }

    /// <summary>
    /// Verifies that IsClientManifestInstalled recognizes acquired Community Patch as satisfying client installation for 1.04 replays.
    /// </summary>
    [Fact]
    public void IsClientManifestInstalled_WhenZh104ReplayAndCommunityPatchAcquired_ReturnsTrue()
    {
        var match = new CrcMappingEntry
        {
            Publisher = "retail",
            ManifestId = "1.104.retail.gameclient.zerohour",
            Version = "1.04",
        };

        var acquiredIds = new HashSet<string>
        {
            "1.20260827.communityoutpost.gameclient.community-patch",
        };

        var isInstalled = ReplayDirectoryService.IsClientManifestInstalled(
            match,
            GameType.ZeroHour,
            acquiredIds);

        Assert.True(isInstalled);
    }

    /// <summary>
    /// Verifies that IsClientManifestInstalled returns false for retail Zero Hour when only non-retail Community Patch is installed.
    /// </summary>
    [Fact]
    public void IsClientManifestInstalled_WhenOnlyNonRetailCommunityPatchInstalled_ReturnsFalseForRetail()
    {
        var match = new CrcMappingEntry
        {
            Publisher = "retail",
            ManifestId = "1.104.retail.gameclient.zerohour",
            Version = "1.04",
        };

        var acquiredIds = new HashSet<string>
        {
            "1.11092026.communityoutpost.gameclient.community-patch-nonret",
        };

        var isInstalled = ReplayDirectoryService.IsClientManifestInstalled(
            match,
            GameType.ZeroHour,
            acquiredIds);

        Assert.False(isInstalled);
    }

    /// <summary>
    /// Verifies that IsCommunityPatchProfile returns false for non-retail Community Patch profile.
    /// </summary>
    [Fact]
    public void IsCommunityPatchProfile_NonRetailProfile_ReturnsFalse()
    {
        var profile = new GameProfile
        {
            Id = "profile-cp-nonret",
            Name = "CP NonRet Profile",
            GameClient = new GameClient
            {
                Id = "1.11092026.communityoutpost.gameclient.community-patch-nonret",
                Name = CommunityOutpostConstants.CommunityPatchNonRetDisplayName,
                GameType = GameType.ZeroHour,
            },
        };

        Assert.False(ReplayDirectoryService.IsCommunityPatchProfile(profile));
    }

    /// <summary>
    /// Verifies that CreateProfileForReplayAsync throws OperationCanceledException when cancellation is requested.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_WhenCancelled_ThrowsOperationCanceledExceptionAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "Match_Cancel.rep",
            FullPath = "/replays/Match_Cancel.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
        };

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CreateProfileForReplayAsync(replay, ct: cts.Token));
    }

    /// <summary>
    /// Verifies that GetReplaysAsync throws OperationCanceledException when cancellation is requested.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetReplaysAsync_WhenCancelled_ThrowsOperationCanceledExceptionAsync()
    {
        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetReplaysAsync(GameType.ZeroHour, cts.Token));
    }

    /// <summary>
    /// Verifies that ReplayFile.ClientAndPatchDisplay displays the friendly INI patch name when the replay is unmapped but INI CRC is known.
    /// </summary>
    [Fact]
    public void ReplayFile_ClientAndPatchDisplay_WhenUnmappedWithKnownIni_DisplaysUnmappedWithIniPatch()
    {
        var replay = new ReplayFile
        {
            FileName = "TWTF.rep",
            FullPath = "/replays/TWTF.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            MatchedClient = null,
            MatchedIniPatchName = "Vanilla 1.04 INI",
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x45BF602F,
                IniCrc = 0xFEAAE3F3,
                BuildTimeString = "Mar 30 2026 13:48:19",
            },
        };

        var display = replay.ClientAndPatchDisplay;
        Assert.Equal("Unmapped Build (Mar 30 2026 13:48:19) • Vanilla 1.04 INI", display);
    }

    /// <summary>
    /// Verifies that ReplayFile.ClientAndPatchDisplay displays MatchedIniPatchName when MatchedClient has no DataPatchName.
    /// </summary>
    [Fact]
    public void ReplayFile_ClientAndPatchDisplay_WhenMatchedClientHasIniFallback_DisplaysMatchedClientWithIniPatch()
    {
        var replay = new ReplayFile
        {
            FileName = "GeneralsOnlineMatch.rep",
            FullPath = "/replays/GeneralsOnlineMatch.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            MatchedClient = new CrcMappingEntry
            {
                Description = "GeneralsOnline 032926_QFE1",
                DataPatchName = null,
            },
            MatchedIniPatchName = "Vanilla 1.04 INI",
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x45BF602F,
                IniCrc = 0xFEAAE3F3,
            },
        };

        var display = replay.ClientAndPatchDisplay;
        Assert.Equal("GeneralsOnline 032926_QFE1 • Vanilla 1.04 INI", display);
    }

    /// <summary>
    /// Verifies that ResolveCompatibilityAsync sets MatchedIniPatchName when INI CRC matches a known catalog patch.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveCompatibility_WhenIniCrcMatchesKnownPatch_PopulatesMatchedIniPatchNameAsync()
    {
        var replay = new ReplayFile
        {
            FileName = "UnknownWithVanillaIni.rep",
            FullPath = "/replays/UnknownWithVanillaIni.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x12345678,
                IniCrc = 0xFEAAE3F3,
                BuildTimeString = "Jan 01 2026 10:00:00",
            },
        };

        var iniEntry = new CrcMappingEntry
        {
            IniCrc = "0xFEAAE3F3",
            DataPatchName = "Vanilla 1.04 INI",
        };

        CrcMappingEntry? nullEntry = null;
        CrcMappingEntry? outIni = iniEntry;
        _mockCrcRegistry
            .Setup(r => r.TryGetEntry("0x12345678", "0xFEAAE3F3", out nullEntry))
            .Returns(false);
        _mockCrcRegistry
            .Setup(r => r.TryGetEntryByExeCrc("0x12345678", out nullEntry))
            .Returns(false);
        _mockCrcRegistry
            .Setup(r => r.TryGetEntryByIniCrc("0xFEAAE3F3", out outIni))
            .Returns(true);

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        await service.ResolveCompatibilityAsync(replay, [], []);

        Assert.Equal("Vanilla 1.04 INI", replay.MatchedIniPatchName);
        Assert.Equal("Unmapped Build (Jan 01 2026 10:00:00) • Vanilla 1.04 INI", replay.ClientAndPatchDisplay);
    }

    /// <summary>
    /// Verifies that BuildReplayProfileName generates the standard format when lengths are within bounds.
    /// </summary>
    [Fact]
    public void BuildReplayProfileName_WhenNormalLength_ReturnsExpectedFormat()
    {
        var name = ReplayDirectoryService.BuildReplayProfileName("Zero Hour", "Match1.rep");
        Assert.Equal("Zero Hour (Replay: Match1)", name);
        Assert.True(name.Length <= ProfileConstants.MaxProfileNameLength);
    }

    /// <summary>
    /// Verifies that BuildReplayProfileName truncates overlong replay names so that the profile name does not exceed MaxProfileNameLength.
    /// </summary>
    [Fact]
    public void BuildReplayProfileName_WhenReplayFileNameExceedsLimit_TruncatesToMaxLength()
    {
        var longReplayName = new string('a', 150) + ".rep";
        var name = ReplayDirectoryService.BuildReplayProfileName("Zero Hour", longReplayName);

        Assert.True(name.Length <= ProfileConstants.MaxProfileNameLength);
        Assert.StartsWith("Zero Hour (Replay: ", name);
        Assert.EndsWith(")", name);
    }

    /// <summary>
    /// Verifies that BuildReplayProfileName respects MaxProfileNameLength when both client title and replay name are long.
    /// </summary>
    [Fact]
    public void BuildReplayProfileName_WhenBothClientTitleAndReplayNameAreLong_RespectsMaxLength()
    {
        var longTitle = new string('T', 80);
        var longReplay = new string('R', 80) + ".rep";
        var name = ReplayDirectoryService.BuildReplayProfileName(longTitle, longReplay);

        Assert.True(name.Length <= ProfileConstants.MaxProfileNameLength);
        Assert.Contains("(Replay: ", name);
        Assert.EndsWith(")", name);
    }

    /// <summary>
    /// Verifies that BuildReplayProfileName clamps correctly when a custom maximum length is specified.
    /// </summary>
    [Fact]
    public void BuildReplayProfileName_WithCustomMaxLength_ClampsAccurately()
    {
        var name = ReplayDirectoryService.BuildReplayProfileName("My Long Custom Title", "My Long Replay Name.rep", maxLength: 30);
        Assert.True(name.Length <= 30);
        Assert.EndsWith(")", name);
    }

    /// <summary>
    /// Verifies that CreateProfileForReplayAsync ensures the profile request name does not exceed the maximum profile name length.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_WhenReplayFileNameIsLong_ProfileNameDoesNotExceedMaxLimitAsync()
    {
        var longBaseName = new string('x', 120);
        var replay = new ReplayFile
        {
            FileName = $"{longBaseName}.rep",
            FullPath = $"/replays/{longBaseName}.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x401D89EA,
                IniCrc = 0x76B251A3,
            },
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0x401D89EA",
                IniCrc = "0x76B251A3",
                ManifestId = "1.104.steam.gameclient.zerohour",
                Publisher = "steam",
                GameType = "ZeroHour",
                Version = "1.04",
            },
        };

        var steamClient = new GameClient
        {
            Id = "1.104.steam.gameclient.zerohour",
            Name = "Command and Conquer Generals Zero Hour (Steam)",
            Version = "1.04",
            GameType = GameType.ZeroHour,
            PublisherType = "Steam",
            InstallationId = "steam-inst-1",
            ExecutablePath = "/steam/generalszh.exe",
            WorkingDirectory = "/steam",
        };

        var installation = new GameInstallation("/steam", GameInstallationType.Steam)
        {
            Id = "steam-inst-1",
            HasZeroHour = true,
            ZeroHourPath = "/steam",
            AvailableGameClients = [steamClient],
        };

        _mockDependencyResolver
            .Setup(r => r.ResolveDependenciesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) => new HashSet<string>(ids));

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        CreateProfileRequest? capturedRequest = null;
        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync((CreateProfileRequest req, CancellationToken _) =>
                ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "long-name-profile", Name = req.Name }));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.CreateProfileForReplayAsync(replay);

        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest.Name.Length <= ProfileConstants.MaxProfileNameLength);
    }

    /// <summary>
    /// Verifies that FindMatchingProfile matches a profile whose name was truncated due to the maximum length limit.
    /// </summary>
    [Fact]
    public void FindMatchingProfile_WhenProfileNameIsTruncated_MatchesReplay()
    {
        var longBaseName = "super_long_replay_match_file_name_that_causes_the_profile_name_to_be_truncated_completely";
        var replay = new ReplayFile
        {
            FileName = $"{longBaseName}.rep",
            FullPath = $"/replays/{longBaseName}.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
        };

        var truncatedProfileName = ReplayDirectoryService.BuildReplayProfileName("Zero Hour", replay.FileName);
        Assert.True(truncatedProfileName.Length <= ProfileConstants.MaxProfileNameLength);

        var truncatedProfile = new GameProfile
        {
            Id = "truncated-profile-id",
            Name = truncatedProfileName,
            GameClient = new GameClient
            {
                Id = "1.104.steam.gameclient.zerohour",
                GameType = GameType.ZeroHour,
                PublisherType = "steam",
            },
        };

        var match = ReplayDirectoryService.FindMatchingProfile(
            [truncatedProfile],
            GameType.ZeroHour,
            "1.104.steam.gameclient.zerohour",
            null,
            replay);

        Assert.NotNull(match);
        Assert.Equal("truncated-profile-id", match.Id);
    }

    /// <summary>
    /// Verifies that FindMatchingProfile does not match a profile dedicated to another replay when the profile name is not truncated even if its replay marker is a prefix of the replay file name.
    /// </summary>
    [Fact]
    public void FindMatchingProfile_WhenNonTruncatedProfileSharesReplayPrefix_DoesNotTreatAsDedicated()
    {
        var replay = new ReplayFile
        {
            FileName = "Match10.rep",
            FullPath = "/replays/Match10.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
        };

        var genericProfile = new GameProfile
        {
            Id = "profile-generic",
            Name = "Zero Hour Standard",
            GameClient = new GameClient
            {
                Id = "1.104.steam.gameclient.zerohour",
                GameType = GameType.ZeroHour,
                PublisherType = "steam",
            },
        };

        var prefixProfile = new GameProfile
        {
            Id = "profile-match1",
            Name = "Zero Hour (Replay: Match1)",
            GameClient = new GameClient
            {
                Id = "1.104.steam.gameclient.zerohour",
                GameType = GameType.ZeroHour,
                PublisherType = "steam",
            },
        };

        var match = ReplayDirectoryService.FindMatchingProfile(
            [prefixProfile, genericProfile],
            GameType.ZeroHour,
            "1.104.steam.gameclient.zerohour",
            null,
            replay);

        Assert.NotNull(match);
        Assert.Equal("profile-generic", match.Id);
    }

    /// <summary>
    /// Verifies that FindRecoveryProfiles returns matching recovery profile for Zero Hour 1.04 replay.
    /// </summary>
    [Fact]
    public void FindRecoveryProfiles_WhenZh104Replay_FindsMatchingRecoveryProfile()
    {
        var replay = new ReplayFile
        {
            FileName = "Match104.rep",
            FullPath = "/replays/Match104.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0xDA2B4B18",
                IniCrc = "0xFEAAE3F3",
                Description = "Zero Hour 1.04 Retail",
                Version = "1.04",
                GameType = "ZeroHour",
                Publisher = "ea",
                ManifestId = "1.104.retail.gameclient.zerohour",
            },
        };

        var vanillaProfile = new GameProfile
        {
            Id = "vanilla-profile",
            Name = "Vanilla 1.04",
            GameClient = new GameClient
            {
                Id = "1.104.retail.gameclient.zerohour",
                Name = "Vanilla Retail Client",
                GameType = GameType.ZeroHour,
                PublisherType = "ea",
            },
        };

        var recoveryProfile = new GameProfile
        {
            Id = "recovery-profile-1",
            Name = "Zero Hour 1.04 (MP-Recovery)",
            GameClient = new GameClient
            {
                Id = "1.0.local.gameclient.generalszh-mp-recovery-c2a5e77d70-f643cae1",
                Name = "generalszh_mp-recovery_c2a5e77d70_F643CAE1",
                GameType = GameType.ZeroHour,
                PublisherType = "thesuperhackers",
                Capabilities = GameClientCapabilities.AllRecoveryFeatures,
            },
            EnabledContentIds = ["1.0.local.gameclient.generalszh-mp-recovery-c2a5e77d70-f643cae1"],
        };

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var matches = service.FindRecoveryProfiles(replay, [vanillaProfile, recoveryProfile]);

        Assert.Single(matches);
        Assert.Equal("recovery-profile-1", matches[0].Id);
    }

    /// <summary>
    /// Verifies that FindRecoveryProfiles isolates GeneralsOnline replays to GeneralsOnline recovery profiles.
    /// </summary>
    [Fact]
    public void FindRecoveryProfiles_WhenGeneralsOnlineReplay_IsolatesToGeneralsOnlineRecoveryProfiles()
    {
        var replay = new ReplayFile
        {
            FileName = "MatchGO.rep",
            FullPath = "/replays/MatchGO.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0x6DBF4405",
                IniCrc = "0x51ACED23",
                Description = "GeneralsOnline 60Hz",
                Version = "082826",
                GameType = "ZeroHour",
                Publisher = "generalsonline",
                ManifestId = "1.828261.generalsonline.gameclient.60hz",
            },
        };

        var retailRecoveryProfile = new GameProfile
        {
            Id = "retail-recovery",
            Name = "ZH 1.04 Recovery",
            GameClient = new GameClient
            {
                Id = "1.0.local.gameclient.generalszh-mp-recovery",
                GameType = GameType.ZeroHour,
                PublisherType = "thesuperhackers",
                Capabilities = GameClientCapabilities.AllRecoveryFeatures,
            },
        };

        var goRecoveryProfile = new GameProfile
        {
            Id = "go-recovery",
            Name = "GeneralsOnline Checkpoint Build",
            GameClient = new GameClient
            {
                Id = "1.828261.generalsonline.gameclient.checkpoint",
                GameType = GameType.ZeroHour,
                PublisherType = "generalsonline",
                Capabilities = GameClientCapabilities.AllRecoveryFeatures,
            },
        };

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var matches = service.FindRecoveryProfiles(replay, [retailRecoveryProfile, goRecoveryProfile]);

        Assert.Single(matches);
        Assert.Equal("go-recovery", matches[0].Id);
    }

    /// <summary>
    /// Verifies that FindRecoveryProfiles excludes GeneralsOnline profiles for retail 1.04 replays to prevent engine drift desync.
    /// </summary>
    [Fact]
    public void FindRecoveryProfiles_WhenRetailReplay_ExcludesGeneralsOnlineRecoveryProfiles()
    {
        var replay = new ReplayFile
        {
            FileName = "Classic.rep",
            FullPath = "/replays/Classic.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            MatchedClient = new CrcMappingEntry
            {
                ExeCrc = "0xDA2B4B18",
                IniCrc = "0xFEAAE3F3",
                Description = "Zero Hour 1.04 Retail",
                Version = "1.04",
                GameType = "ZeroHour",
                Publisher = "ea",
                ManifestId = "1.104.retail.gameclient.zerohour",
            },
        };

        var goRecoveryProfile = new GameProfile
        {
            Id = "go-recovery",
            Name = "GeneralsOnline Checkpoint Build",
            GameClient = new GameClient
            {
                Id = "1.828261.generalsonline.gameclient.checkpoint",
                GameType = GameType.ZeroHour,
                PublisherType = "generalsonline",
                Capabilities = GameClientCapabilities.AllRecoveryFeatures,
            },
        };

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var matches = service.FindRecoveryProfiles(replay, [goRecoveryProfile]);

        Assert.Empty(matches);
    }

    /// <summary>
    /// Verifies that FindRecoveryProfiles excludes profiles that possess only CheckpointSaves rather than the full AllRecoveryFeatures mask.
    /// </summary>
    [Fact]
    public void FindRecoveryProfiles_WhenClientHasCheckpointSavesOnly_ExcludesProfile()
    {
        var replay = new ReplayFile
        {
            FileName = "ZH_Game.rep",
            FullPath = "/replays/ZH_Game.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0xDA2B4B18,
                IniCrc = 0xFEAAE3F3,
            },
        };

        var partialCapabilityProfile = new GameProfile
        {
            Id = "partial-profile",
            Name = "Zero Hour Checkpoint Only",
            GameClient = new GameClient
            {
                Id = "1.0.local.gameclient.generalszh-checkpoint-only",
                Name = "Zero Hour Checkpoint Client",
                GameType = GameType.ZeroHour,
                PublisherType = "custom",
                Capabilities = GameClientCapabilities.CheckpointSaves,
            },
        };

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var matches = service.FindRecoveryProfiles(replay, [partialCapabilityProfile]);

        Assert.Empty(matches);
    }

    /// <summary>
    /// Verifies that ResolveCompatibility sets SupportsCheckpoints and RecoveryProfile properties on retail 1.04 replay.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveCompatibilityAsync_WhenRetailReplayAndRecoveryProfileExists_SetsSupportsCheckpointsAndRecoveryProfile()
    {
        var replay = new ReplayFile
        {
            FileName = "RECOVER.rep",
            FullPath = "/replays/RECOVER.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0xDA2B4B18,
                IniCrc = 0xFEAAE3F3,
            },
        };

        var entry = new CrcMappingEntry
        {
            ExeCrc = "0xDA2B4B18",
            IniCrc = "0xFEAAE3F3",
            Description = "Zero Hour 1.04 Retail",
            Version = "1.04",
            GameType = "ZeroHour",
            Publisher = "ea",
            ManifestId = "1.104.retail.gameclient.zerohour",
        };

        CrcMappingEntry? outEntry = entry;
        _mockCrcRegistry
            .Setup(r => r.TryGetEntry("0xDA2B4B18", "0xFEAAE3F3", out outEntry))
            .Returns(true);

        var recoveryProfile = new GameProfile
        {
            Id = "fd09bf9b12bc41e294c051028b5085f9",
            Name = "Zero Hour 1.04 (Recovery)",
            GameClient = new GameClient
            {
                Id = "1.0.local.gameclient.generalszh-mp-recovery-c2a5e77d70-f643cae1",
                Name = "generalszh_mp-recovery_c2a5e77d70_F643CAE1",
                GameType = GameType.ZeroHour,
                PublisherType = "thesuperhackers",
                Capabilities = GameClientCapabilities.AllRecoveryFeatures,
            },
        };

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        await service.ResolveCompatibilityAsync(replay, new HashSet<string>(), [recoveryProfile]);

        Assert.True(replay.SupportsCheckpoints);
        Assert.Equal("fd09bf9b12bc41e294c051028b5085f9", replay.RecoveryProfileId);
        Assert.Equal("Zero Hour 1.04 (Recovery)", replay.RecoveryProfileName);
    }

    /// <summary>
    /// Verifies that ResolveCompatibility leaves SupportsCheckpoints false when no matching recovery profile exists.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveCompatibilityAsync_WhenNoMatchingRecoveryProfile_DoesNotSupportCheckpoints()
    {
        var replay = new ReplayFile
        {
            FileName = "RECOVER.rep",
            FullPath = "/replays/RECOVER.rep",
            SizeInBytes = 2048,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0xDA2B4B18,
                IniCrc = 0xFEAAE3F3,
            },
        };

        var entry = new CrcMappingEntry
        {
            ExeCrc = "0xDA2B4B18",
            IniCrc = "0xFEAAE3F3",
            Description = "Zero Hour 1.04 Retail",
            Version = "1.04",
            GameType = "ZeroHour",
            Publisher = "ea",
            ManifestId = "1.104.retail.gameclient.zerohour",
        };

        CrcMappingEntry? outEntry = entry;
        _mockCrcRegistry
            .Setup(r => r.TryGetEntry("0xDA2B4B18", "0xFEAAE3F3", out outEntry))
            .Returns(true);

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        await service.ResolveCompatibilityAsync(replay, new HashSet<string>(), []);

        Assert.False(replay.SupportsCheckpoints);
        Assert.Null(replay.RecoveryProfileId);
        Assert.Null(replay.RecoveryProfileName);
    }

    /// <summary>
    /// Verifies that a GeneralsOnline replay resolves to Compatible once the 60Hz client variant
    /// is acquired and a profile using it exists, even though the CRC catalog declares the
    /// legacy zerohour content name for the same publisher and version.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveCompatibility_WhenGeneralsOnline60HzVariantAcquiredAndProfileExists_ResolvesToCompatibleAsync()
    {
        var replay = CreateCrcReplayFile("i-fly.rep", 0x88BEB180, 0xFEAAE3F3);

        CrcMappingEntry? outEntry = CreateGeneralsOnlineMappingEntry();
        _mockCrcRegistry
            .Setup(r => r.TryGetEntry("0x88BEB180", "0xFEAAE3F3", out outEntry))
            .Returns(true);

        var acquiredIds = new HashSet<string>
        {
            "1.104.steam.gameinstallation.zerohour",
            "1.213262.generalsonline.gameclient.60hz",
            "1.213262.generalsonline.mappack.quickmatchmaps",
            "1.213262.generalsonline.patch.gamedata",
        };

        var profile = CreateGeneralsOnlineProfile(
            "GeneralsOnline 021326_QFE2 (Replay: i-fly)",
            "1.104.steam.gameinstallation.zerohour",
            "1.213262.generalsonline.gameclient.60hz",
            "1.213262.generalsonline.mappack.quickmatchmaps",
            "1.213262.generalsonline.patch.gamedata");

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        await service.ResolveCompatibilityAsync(replay, acquiredIds, [profile]);

        Assert.Equal(ReplayCompatibilityStatus.Compatible, replay.CompatibilityStatus);
        Assert.Equal("go-profile-1", replay.MatchingProfileId);
    }

    /// <summary>
    /// Verifies that the acquired GeneralsOnline 60Hz client variant satisfies the catalog
    /// zerohour client requirement, even though the catalog declares the zerohour content name.
    /// </summary>
    [Fact]
    public void IsClientManifestInstalled_WhenGeneralsOnline60HzVariantAcquired_ReturnsTrue()
    {
        var acquiredIds = new HashSet<string>
        {
            "1.104.steam.gameinstallation.zerohour",
            "1.213262.generalsonline.gameclient.60hz",
        };

        Assert.True(ReplayDirectoryService.IsClientManifestInstalled(CreateGeneralsOnlineMappingEntry(), GameType.ZeroHour, acquiredIds));
    }

    /// <summary>
    /// Verifies that third-party profile matching treats the GeneralsOnline 60Hz client variant
    /// as equivalent to the catalog zerohour client when publisher and version match.
    /// </summary>
    [Fact]
    public void IsProfileMatchingThirdParty_WhenGeneralsOnline60HzVariantWithSameVersion_ReturnsTrue()
    {
        var profile = CreateGeneralsOnlineProfile(
            "GeneralsOnline 021326_QFE2 (Replay: i-fly)",
            "1.104.steam.gameinstallation.zerohour",
            "1.213262.generalsonline.gameclient.60hz");

        Assert.True(ReplayDirectoryService.IsProfileMatchingThirdParty(
            profile,
            "1.213262.generalsonline.gameclient.zerohour",
            null,
            "021326_QFE2"));
    }

    /// <summary>
    /// Verifies that third-party profile matching still rejects GeneralsOnline 60Hz clients
    /// from a different release version to prevent cross-version replay mismatches.
    /// </summary>
    [Fact]
    public void IsProfileMatchingThirdParty_WhenGeneralsOnline60HzVariantWithDifferentVersion_ReturnsFalse()
    {
        var profile = CreateGeneralsOnlineProfile(
            "GeneralsOnline 021326_QFE2 (Replay: i-fly)",
            "1.104.steam.gameinstallation.zerohour",
            "1.213262.generalsonline.gameclient.60hz");

        Assert.False(ReplayDirectoryService.IsProfileMatchingThirdParty(
            profile,
            "1.329261.generalsonline.gameclient.zerohour",
            null,
            "032926_QFE1"));
    }

    /// <summary>
    /// Verifies that a retail Zero Hour 1.04 replay resolves to Compatible for retail-provenance
    /// profiles on both stock retail executables and user-modified retail executables (GenTool
    /// and similar patches preserve the simulation while changing the binary checksum). Both rows
    /// pin the retail-provenance bypass, which intentionally does not consult the live executable
    /// CRC; plain retail profiles additionally yield no checkpoint support.
    /// </summary>
    /// <param name="mockedExeCrc">The live executable CRC reported for the profile executable.</param>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("0x401D89EA")]
    [InlineData("0xAB6F4BE0")]
    public async Task ResolveCompatibility_WhenRetailReplayAndRetailProvenanceProfileExists_ResolvesToCompatibleAsync(string mockedExeCrc)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "genhub_test_retail_provenance_" + Guid.NewGuid().ToString("N"));
        var fakeExePath = Path.Combine(tempDir, "game.dat");

        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(fakeExePath, "fake-steam-binary-content");

            var mockCrcCalc = new Mock<IGameCrcCalculatorService>();
            mockCrcCalc
                .Setup(c => c.CalculateExeCrcAsync(fakeExePath, It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess(mockedExeCrc));

            // Mirror production GetReplaysAsync cache preloading before compatibility resolution.
            await ReplayCrcMatchingHelper.GetOrCalculateProfileExeCrcAsync(fakeExePath, mockCrcCalc.Object);

            var replay = CreateCrcReplayFile("RECOVER.rep", 0xDA2B4B18, 0xFEAAE3F3);

            CrcMappingEntry? outEntry = CreateRetailMappingEntry();
            _mockCrcRegistry
                .Setup(r => r.TryGetEntry("0xDA2B4B18", "0xFEAAE3F3", out outEntry))
                .Returns(true);

            var acquiredIds = new HashSet<string>
            {
                "1.104.steam.gameinstallation.zerohour",
                "1.104.steam.gameclient.zerohour",
            };

            var profile = new GameProfile
            {
                Id = "retail-profile-1",
                Name = "Zero Hour 1.04 (Retail) (Replay: RECOVER)",
                GameClient = new GameClient
                {
                    Id = "1.104.steam.gameclient.zerohour",
                    Name = "Zero Hour 1.04 (Retail)",
                    Version = "1.04",
                    GameType = GameType.ZeroHour,
                    PublisherType = "steam",
                    ExecutablePath = fakeExePath,
                    WorkingDirectory = tempDir,
                },
                EnabledContentIds =
                [
                    "1.104.steam.gameinstallation.zerohour",
                    "1.104.steam.gameclient.zerohour",
                ],
            };

            var service = new ReplayDirectoryService(
                _mockHeaderParser.Object,
                _mockCrcRegistry.Object,
                _mockScopeFactory.Object,
                NullLogger<ReplayDirectoryService>.Instance,
                mockCrcCalc.Object);

            await service.ResolveCompatibilityAsync(replay, acquiredIds, [profile]);

            Assert.Equal(ReplayCompatibilityStatus.Compatible, replay.CompatibilityStatus);
            Assert.Equal("retail-profile-1", replay.MatchingProfileId);
            Assert.False(replay.SupportsCheckpoints);
            Assert.Null(replay.RecoveryProfileId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Verifies that FindRecoveryProfiles excludes recovery-capable profiles whose executable CRC
    /// verifiably mismatches the replay executable CRC.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FindRecoveryProfiles_WhenProfileExeCrcMismatchesReplayExeCrc_ExcludesProfileAsync()
    {
        var matches = await FindRecoveryProfilesWithCachedExeCrcAsync("0x887B0CAA");

        Assert.Empty(matches);
    }

    /// <summary>
    /// Verifies that FindRecoveryProfiles keeps recovery-capable profiles whose executable CRC
    /// matches the replay executable CRC.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FindRecoveryProfiles_WhenProfileExeCrcMatchesReplayExeCrc_IncludesProfileAsync()
    {
        var matches = await FindRecoveryProfilesWithCachedExeCrcAsync("0xDA2B4B18");

        Assert.Single(matches);
        Assert.Equal("recovery-profile-1", matches[0].Id);
    }

    /// <summary>
    /// Verifies GeneralsOnline game client variant identity matching across catalog and delivery names.
    /// </summary>
    /// <param name="requiredId">The required manifest ID.</param>
    /// <param name="candidateId">The candidate manifest ID.</param>
    /// <param name="expected">The expected match result.</param>
    [Theory]
    [InlineData("1.213262.generalsonline.gameclient.zerohour", "1.213262.generalsonline.gameclient.60hz", true)]
    [InlineData("1.428262.generalsonline.gameclient.eac-zerohour", "1.428262.generalsonline.gameclient.60hz", true)]
    [InlineData("1.0.generalsonline.gameclient.zerohour-generalsonline-60hz", "1.213262.generalsonline.gameclient.zerohour", true)]
    [InlineData("1.213262.generalsonline.gameclient.zerohour", "1.213262.generalsonline.gameclient.30hz", false)]
    [InlineData("1.213262.generalsonline.gameclient.zerohour", "1.213262.thesuperhackers.gameclient.zerohour", false)]
    [InlineData("1.213262.generalsonline.gameclient.zerohour", "1.213262.generalsonline.mappack.quickmatchmaps", false)]
    [InlineData("1.213262.generalsonline.gameclient.zerohour", "1.213262.generalsonline.gameclient.generals-generalsonline-60hz", false)]
    [InlineData("1.213262.generalsonline.gameclient.zerohour", "not-a-manifest-id", false)]
    public void IsGeneralsOnlineGameClientVariantMatch_MatchesExpectedPairs(string requiredId, string candidateId, bool expected)
    {
        Assert.Equal(expected, ReplayDirectoryService.IsGeneralsOnlineGameClientVariantMatch(requiredId, candidateId));
    }

    /// <summary>
    /// Verifies end-to-end client manifest matching for GeneralsOnline variants, including the
    /// version gate: version-less candidates (such as compound detector ids with a zero build
    /// segment) cannot match, while candidates with a known matching version can.
    /// </summary>
    /// <param name="candidateId">The candidate manifest ID.</param>
    /// <param name="candidateVersion">The candidate client version, if known.</param>
    /// <param name="expected">The expected match result.</param>
    [Theory]
    [InlineData("1.213262.generalsonline.gameclient.60hz", "021326_QFE2", true)]
    [InlineData("1.213262.generalsonline.gameclient.60hz", null, true)]
    [InlineData("1.0.generalsonline.gameclient.zerohour-generalsonline-60hz", "021326_QFE2", true)]
    [InlineData("1.0.generalsonline.gameclient.zerohour-generalsonline-60hz", null, false)]
    [InlineData("1.0.generalsonline.gameclient.generals-generalsonline-60hz", "021326_QFE2", false)]
    public void IsClientManifestMatch_GeneralsOnlineVariants_RespectsVersionGate(string candidateId, string? candidateVersion, bool expected)
    {
        Assert.Equal(
            expected,
            ReplayDirectoryService.IsClientManifestMatch("1.213262.generalsonline.gameclient.zerohour", candidateId, "021326_QFE2", candidateVersion));
    }

    /// <summary>
    /// Verifies that profile creation from the client selection dialog resolves an unpooled
    /// GeneralsOnline catalog id (zerohour content name) to the pooled 60Hz variant manifest.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_WhenCustomGeneralsOnlineCatalogIdProvided_ResolvesPooled60HzVariantAsync()
    {
        var replay = CreateCrcReplayFile("i-fly.rep", 0x88BEB180, 0xFEAAE3F3, CreateGeneralsOnlineMappingEntry());

        // Mirrors the top-priority matched-client card produced by the selection dialog.
        var customClient = new GameClient
        {
            Id = "1.213262.generalsonline.gameclient.zerohour",
            Name = "GeneralsOnline 021326_QFE2",
            Version = "021326_QFE2",
            PublisherType = "generalsonline",
            GameType = GameType.ZeroHour,
            ExecutablePath = string.Empty,
        };

        var installation = new GameInstallation("/games/ZeroHour", GameInstallationType.Steam)
        {
            HasZeroHour = true,
            ZeroHourPath = "/games/ZeroHour",
        };

        _mockInstallationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        _mockProfileManager
            .Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        _mockDependencyResolver
            .Setup(r => r.ResolveDependenciesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) => new HashSet<string>(ids));

        var pooledGameClient = new ContentManifest
        {
            Id = ManifestId.Create("1.213262.generalsonline.gameclient.60hz"),
            Name = "GeneralsOnline 60Hz",
            ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Version = "021326_QFE2",
            Publisher = new PublisherInfo { PublisherType = "generalsonline", Name = "GeneralsOnline" },
        };

        _mockManifestPool
            .Setup(m => m.GetManifestAsync(It.Is<ManifestId>(id => id.Value == "1.213262.generalsonline.gameclient.60hz"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(pooledGameClient));

        _mockManifestPool
            .Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([pooledGameClient]));

        CreateProfileRequest? capturedRequest = null;
        _mockProfileManager
            .Setup(p => p.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync((CreateProfileRequest req, CancellationToken _) =>
                ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "go-profile-1", Name = req.Name }));

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.CreateProfileForReplayAsync(
            replay,
            customGameClient: customClient,
            customClientManifestId: "1.213262.generalsonline.gameclient.zerohour");

        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        Assert.Equal("1.213262.generalsonline.gameclient.60hz", capturedRequest.GameClientId);
        Assert.NotNull(capturedRequest.GameClient);
        Assert.Equal("1.213262.generalsonline.gameclient.60hz", capturedRequest.GameClient.Id);
    }

    /// <summary>
    /// Verifies that GetReplayDirectory uses IGamePathProvider when supplied.
    /// </summary>
    /// <param name="gameType">The game type to test.</param>
    /// <param name="expectedFolder">The expected directory name.</param>
    [Theory]
    [InlineData(GameType.Generals, "Command and Conquer Generals Data")]
    [InlineData(GameType.ZeroHour, "Command and Conquer Generals Zero Hour Data")]
    public void GetReplayDirectory_WithPathProvider_UsesProvidedOptionsDirectory(GameType gameType, string expectedFolder)
    {
        var mockPathProvider = new Mock<IGamePathProvider>();
        var fakeBasePath = Path.Combine(Path.GetTempPath(), expectedFolder);
        mockPathProvider
            .Setup(p => p.GetOptionsDirectory(gameType))
            .Returns(fakeBasePath);

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance,
            pathProvider: mockPathProvider.Object);

        var result = service.GetReplayDirectory(gameType);

        var expectedPath = Path.Combine(fakeBasePath, GameSettingsConstants.FolderNames.Replays);
        Assert.Equal(expectedPath, result);
        mockPathProvider.Verify(p => p.GetOptionsDirectory(gameType), Times.Once);
    }

    /// <summary>
    /// Verifies that GetReplayDirectory falls back to SpecialFolder.MyDocuments when pathProvider is null.
    /// </summary>
    /// <param name="gameType">The game type to test.</param>
    /// <param name="folderName">The expected fallback directory name.</param>
    [Theory]
    [InlineData(GameType.Generals, GameSettingsConstants.FolderNames.Generals)]
    [InlineData(GameType.ZeroHour, GameSettingsConstants.FolderNames.ZeroHour)]
    public void GetReplayDirectory_WithoutPathProvider_FallsBackToMyDocuments(GameType gameType, string folderName)
    {
        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance,
            pathProvider: null);

        var result = service.GetReplayDirectory(gameType);

        var expectedPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            folderName,
            GameSettingsConstants.FolderNames.Replays);

        Assert.Equal(expectedPath, result);
    }

    /// <summary>
    /// Verifies that GetReplayDirectory throws ArgumentException for unsupported game types.
    /// </summary>
    /// <param name="invalidGameType">The invalid game type to test.</param>
    [Theory]
    [InlineData(GameType.Unknown)]
    [InlineData((GameType)999)]
    public void GetReplayDirectory_WithUnsupportedGameType_ThrowsArgumentException(GameType invalidGameType)
    {
        var mockPathProvider = new Mock<IGamePathProvider>();
        var serviceWithPathProvider = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance,
            pathProvider: mockPathProvider.Object);

        var serviceWithoutPathProvider = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance,
            pathProvider: null);

        Assert.Throws<ArgumentException>(() => serviceWithPathProvider.GetReplayDirectory(invalidGameType));
        Assert.Throws<ArgumentException>(() => serviceWithoutPathProvider.GetReplayDirectory(invalidGameType));
    }

    private static ReplayFile CreateCrcReplayFile(string fileName, uint exeCrc, uint iniCrc, CrcMappingEntry? matchedClient = null) => new()
    {
        FileName = fileName,
        FullPath = $"/replays/{fileName}",
        SizeInBytes = 2048,
        LastModified = DateTime.UtcNow,
        GameVersion = GameType.ZeroHour,
        Metadata = new ReplayMetadata
        {
            ExeCrc = exeCrc,
            IniCrc = iniCrc,
        },
        MatchedClient = matchedClient,
    };

    private static CrcMappingEntry CreateGeneralsOnlineMappingEntry() => new()
    {
        ExeCrc = "0x88BEB180",
        IniCrc = "0xFEAAE3F3",
        ManifestId = "1.213262.generalsonline.gameclient.zerohour",
        Publisher = "generalsonline",
        GameType = "ZeroHour",
        Version = "021326_QFE2",
        Description = "GeneralsOnline 021326_QFE2",
        CdnUrl = "https://cdn.playgenerals.online/GeneralsOnline_portable_021326_QFE2.zip",
    };

    private static GameProfile CreateGeneralsOnlineProfile(string profileName, params string[] enabledContentIds) => new()
    {
        Id = "go-profile-1",
        Name = profileName,
        GameClient = new GameClient
        {
            Id = "1.213262.generalsonline.gameclient.60hz",
            Name = "GeneralsOnline 021326_QFE2",
            Version = "021326_QFE2",
            GameType = GameType.ZeroHour,
            PublisherType = "generalsonline",
        },
        EnabledContentIds = [.. enabledContentIds],
    };

    private static CrcMappingEntry CreateRetailMappingEntry() => new()
    {
        ExeCrc = "0xDA2B4B18",
        IniCrc = "0xFEAAE3F3",
        ManifestId = "1.104.retail.gameclient.zerohour",
        Publisher = "retail",
        GameType = "ZeroHour",
        Version = "1.04",
        Description = "Zero Hour 1.04 (Retail)",
    };

    private static async Task<IReadOnlyList<GameProfile>> FindRecoveryProfilesWithCachedExeCrcAsync(string mockedExeCrc)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "genhub_test_recovery_crc_" + Guid.NewGuid().ToString("N"));
        var fakeExePath = Path.Combine(tempDir, "generals.exe");

        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(fakeExePath, "fake-recovery-binary-content");

            var mockCrcCalc = new Mock<IGameCrcCalculatorService>();
            mockCrcCalc
                .Setup(c => c.CalculateExeCrcAsync(fakeExePath, It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess(mockedExeCrc));

            await ReplayCrcMatchingHelper.GetOrCalculateProfileExeCrcAsync(fakeExePath, mockCrcCalc.Object);

            var replay = new ReplayFile
            {
                FileName = "Match104.rep",
                FullPath = "/replays/Match104.rep",
                SizeInBytes = 2048,
                LastModified = DateTime.UtcNow,
                GameVersion = GameType.ZeroHour,
                MatchedClient = CreateRetailMappingEntry(),
            };

            var recoveryProfile = new GameProfile
            {
                Id = "recovery-profile-1",
                Name = "Zero Hour 1.04 (Recovery)",
                GameClient = new GameClient
                {
                    Id = "1.0.local.gameclient.generalszh-mp-recovery",
                    Name = "Zero Hour 1.04 Recovery",
                    GameType = GameType.ZeroHour,
                    PublisherType = "custom",
                    Capabilities = GameClientCapabilities.AllRecoveryFeatures,
                    ExecutablePath = fakeExePath,
                    WorkingDirectory = tempDir,
                },
            };

            var service = new ReplayDirectoryService(
                new Mock<IReplayHeaderParser>().Object,
                new Mock<ICrcMappingRegistry>().Object,
                new Mock<IServiceScopeFactory>().Object,
                NullLogger<ReplayDirectoryService>.Instance);

            return service.FindRecoveryProfiles(replay, [recoveryProfile]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private static ReplayFile CreateTestReplayForPathResolution(string publisher) => new()
    {
        FileName = "Test.rep",
        FullPath = "/replays/Test.rep",
        SizeInBytes = 2048,
        LastModified = DateTime.UtcNow,
        GameVersion = GameType.ZeroHour,
        MatchedClient = new CrcMappingEntry
        {
            Publisher = publisher,
        },
    };
}
