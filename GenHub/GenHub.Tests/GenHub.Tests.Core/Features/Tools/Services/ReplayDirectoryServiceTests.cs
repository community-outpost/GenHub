using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
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
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile
            {
                GameClient = new GameClient
                {
                    Id = "1.104.retail.gameclient.zerohour",
                    GameType = GameType.ZeroHour,
                },
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

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("profile-zh-1", replay.MatchingProfileId);
        Assert.Equal(ReplayCompatibilityStatus.Compatible, replay.CompatibilityStatus);
        Assert.Equal("Ready to Play", replay.CompatibilityBadgeText);
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
    /// Verifies that ReplayFile helper properties correctly map all compatibility enum states to badges and tooltips.
    /// </summary>
    [Fact]
    public void ReplayFile_CompatibilityBadgeAndTooltip_ReflectsStatusAccurately()
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
        Assert.Equal("Ready to Play", compatibleReplay.CompatibilityBadgeText);
        Assert.Contains("ZH SuperHackers", compatibleReplay.CompatibilityTooltip);

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
        Assert.Contains("Click 'Create Profile'", requiresProfileReplay.CompatibilityTooltip);

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
        Assert.Contains("can be downloaded", downloadableReplay.CompatibilityTooltip);

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
        Assert.Contains("official catalog", orphanedReplay.CompatibilityTooltip, StringComparison.OrdinalIgnoreCase);

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
            Name = "ZH Profile 1",
            GameClient = new GameClient { Id = "1.104.steam.gameclient.zerohour", GameType = GameType.ZeroHour },
            EnabledContentIds = ["1.04.patch.data"],
        };

        var profile2 = new GameProfile
        {
            Id = "p2",
            Name = "ZH Profile 2",
            GameClient = new GameClient { Id = "1.104.retail.gameclient.zerohour", GameType = GameType.ZeroHour },
        };

        var profiles = new List<GameProfile> { profile2, profile1 };
        var results = ReplayDirectoryService.FindCompatibleProfiles(
            profiles,
            GameType.ZeroHour,
            "1.104.steam.gameclient.zerohour",
            "1.04.patch.data",
            replay);

        Assert.Contains(profile1, results);
        Assert.Equal(profile1, results[0]);
    }

    /// <summary>
    /// Verifies that FindCompatibleProfiles strictly excludes candidate profiles whose executable binary CRC does not match the replay executable CRC.
    /// </summary>
    [Fact]
    public void FindCompatibleProfiles_WhenProfileExeCrcMismatchesReplayExeCrc_ExcludesProfile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "genhub_test_crc_mismatch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fakeExePath = Path.Combine(tempDir, "generals.exe");
        File.WriteAllText(fakeExePath, "fake-binary-content");

        try
        {
            var replay = new ReplayFile
            {
                FileName = "Oct17Replay.rep",
                FullPath = "/test/Oct17Replay.rep",
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
            var results = ReplayDirectoryService.FindCompatibleProfiles(
                profiles,
                GameType.ZeroHour,
                "1.104.retail.gameclient.zerohour",
                null,
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
    [Fact]
    public void FindCompatibleProfiles_WhenProfileExeCrcMatchesReplayExeCrc_IncludesProfile()
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
            var results = ReplayDirectoryService.FindCompatibleProfiles(
                profiles,
                GameType.ZeroHour,
                "1.104.retail.gameclient.zerohour",
                null,
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
            Assert.Equal("Ready to Play", replay.CompatibilityBadgeText);
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
    [Fact]
    public void ResolveCompatibility_WhenExistingProfileHasDifferentClientVersion_DoesNotMatchOlderProfile()
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
        service.ResolveCompatibility(replay, new HashSet<string>(), [olderProfile]);
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

        service.ResolveCompatibility(replay2, new HashSet<string>(), [matchingProfile]);
        Assert.Equal("matching-go-profile-082826", replay2.MatchingProfileId);
        Assert.Equal(ReplayCompatibilityStatus.Compatible, replay2.CompatibilityStatus);
    }

    /// <summary>
    /// Verifies that ResolveCompatibility sets Unknown status when either ExeCrc or IniCrc is missing.
    /// </summary>
    [Fact]
    public void ResolveCompatibility_WhenIniCrcOrExeCrcMissing_SetsUnknownStatus()
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

        service.ResolveCompatibility(replayNoIni, new HashSet<string>(), []);
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

        service.ResolveCompatibility(replayNoExe, new HashSet<string>(), []);
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
    /// Verifies that FindMatchingProfile does not match a profile with community outpost client to a retail replay.
    /// </summary>
    [Fact]
    public void FindMatchingProfile_WhenRetailReplay_DoesNotMatchCommunityOutpostProfile()
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

        Assert.Null(match);
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
        Assert.EndsWith("generalszh.exe", capturedRequest.GameClient.ExecutablePath, StringComparison.OrdinalIgnoreCase);
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

        Assert.Equal("generalszh.exe", result);
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

        Assert.Equal("generalszh.exe", result);
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
    [Fact]
    public void ResolveCompatibility_WhenClientInstalledButNoProfile_ResolvesToRequiresProfile()
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

        service.ResolveCompatibility(replay, acquiredIds, []);

        Assert.Equal(ReplayCompatibilityStatus.RequiresProfile, replay.CompatibilityStatus);
        Assert.Null(replay.MatchingProfileId);
        Assert.Equal("Profile Needed", replay.CompatibilityBadgeText);
    }

    /// <summary>
    /// Verifies that ResolveCompatibility resolves to Downloadable when client is not installed but has a CDN URL or third-party manifest ID.
    /// </summary>
    [Fact]
    public void ResolveCompatibility_WhenNotInstalledAndHasCdnUrlOrNonRetailManifest_ResolvesToDownloadable()
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

        service.ResolveCompatibility(replay, new HashSet<string>(), []);

        Assert.Equal(ReplayCompatibilityStatus.Downloadable, replay.CompatibilityStatus);
        Assert.Null(replay.MatchingProfileId);
        Assert.Equal("Download Required", replay.CompatibilityBadgeText);
    }

    /// <summary>
    /// Verifies that ResolveCompatibility resolves to Orphaned when a retail client is not installed on the system and has no CDN URL.
    /// </summary>
    [Fact]
    public void ResolveCompatibility_WhenRetailClientNotInstalled_ResolvesToOrphaned()
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

        service.ResolveCompatibility(replay, new HashSet<string>(), []);

        Assert.Equal(ReplayCompatibilityStatus.Orphaned, replay.CompatibilityStatus);
        Assert.Null(replay.MatchingProfileId);
        Assert.Equal("Custom / Unmapped", replay.CompatibilityBadgeText);
    }

    /// <summary>
    /// Verifies that when an exact pair is missing, but base client is known and user has a local acquired manifest matching the INI CRC, compatibility is resolved.
    /// </summary>
    [Fact]
    public void ResolveCompatibility_WhenExactPairMissing_MatchesLocalAcquiredManifest_ResolvesSuccessfully()
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

        service.ResolveCompatibility(replay, acquiredIds, []);

        Assert.Equal(ReplayCompatibilityStatus.RequiresProfile, replay.CompatibilityStatus);
        Assert.NotNull(replay.MatchedClient);
        Assert.Equal("1.828261.generalsonline.patch.gamedata", replay.MatchedClient.DataPatchManifestId);
    }

    /// <summary>
    /// Verifies that when an exact pair is missing, but base client and catalog data patch are known, resolves to Downloadable.
    /// </summary>
    [Fact]
    public void ResolveCompatibility_WhenExactPairMissing_MatchesCatalogDataPatch_ResolvesToDownloadable()
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

        service.ResolveCompatibility(replay, new HashSet<string>(), []);

        Assert.Equal(ReplayCompatibilityStatus.Downloadable, replay.CompatibilityStatus);
        Assert.NotNull(replay.MatchedClient);
        Assert.Equal("1.101.thesuperhackers.patch.gamedata", replay.MatchedClient.DataPatchManifestId);
    }

    /// <summary>
    /// Verifies that when a replay has a GeneralsOnline Exe CRC and Vanilla 1.04 INI CRC (0xFEAAE3F3),
    /// compatibility does not discard baseClient, resolving to Vanilla 1.04 INI rather than unmapped.
    /// </summary>
    [Fact]
    public void ResolveCompatibility_WhenGeneralsOnlineClientWithVanillaIni_ResolvesWithoutDroppingBaseClient()
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

        service.ResolveCompatibility(replay, acquiredIds, []);

        Assert.Equal(ReplayCompatibilityStatus.RequiresProfile, replay.CompatibilityStatus);
        Assert.NotNull(replay.MatchedClient);
        Assert.Equal("generalsonline", replay.MatchedClient.Publisher);
        Assert.Equal("032926_QFE1", replay.MatchedClient.Version);
        Assert.Equal("Vanilla 1.04 INI", replay.MatchedClient.DataPatchName);
    }

    /// <summary>
    /// Verifies that when exact CRC pair is missing, a replay matching GeneralsOnline pattern
    /// and modern build timestamp resolves via heuristic to GeneralsOnline with Vanilla 1.04 INI.
    /// </summary>
    [Fact]
    public void ResolveCompatibility_WhenGeneralsOnlineLadderReplayAndModernBuildTime_ResolvesViaHeuristic()
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

        service.ResolveCompatibility(replay, acquiredIds, []);

        Assert.Equal(ReplayCompatibilityStatus.RequiresProfile, replay.CompatibilityStatus);
        Assert.NotNull(replay.MatchedClient);
        Assert.Equal("generalsonline", replay.MatchedClient.Publisher);
        Assert.Equal("032926_QFE2", replay.MatchedClient.Version);
        Assert.Equal("Vanilla 1.04 INI", replay.MatchedClient.DataPatchName);
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
            Name = "MP Recovery Profile",
            GameClient = new GameClient
            {
                Id = "mp-recovery-client-id",
                Name = "MP Recovery",
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
        Assert.Equal("MP Recovery Profile", replay.MatchingProfileName);
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
    /// Verifies that when replay CRC is not in registry, but an existing profile game client executable has matching CRC,
    /// ResolveCompatibility resolves compatibility to that profile.
    /// </summary>
    [Fact]
    public void ResolveCompatibility_WhenCustomProfileExecutableCrcMatches_ResolvesToCompatibleProfile()
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

            var crcCalcMock = new Mock<IGameCrcCalculatorService>();
            crcCalcMock
                .Setup(c => c.CalculateExeCrcAsync(tempExe, It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess("0x88BEB180"));

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

            service.ResolveCompatibility(replay, acquiredIds, [profile]);

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
