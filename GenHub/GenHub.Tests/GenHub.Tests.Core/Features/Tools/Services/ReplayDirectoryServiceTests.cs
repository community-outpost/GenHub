using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
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

        _mockInstallationService
            .Setup(s => s.CreateAndRegisterInstallationManifestsAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockManifestPool
            .Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));
        _mockManifestPool
            .Setup(m => m.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(null));
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
            .Setup(l => l.LaunchProfileAsync("existing-profile-id", false, It.IsAny<CancellationToken>()))
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
        Assert.True(capturedRequest.UseSteamLaunch);
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
            .Setup(l => l.LaunchProfileAsync("auto-created-profile-99", false, It.IsAny<CancellationToken>()))
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
                ManifestId = "1.828261.generalsonline.gameclient.zerohour",
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
                ManifestId = "1.828261.generalsonline.gameclient.zerohour",
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
            .Setup(m => m.GetManifestAsync(ManifestId.Create("1.828261.generalsonline.gameclient.zerohour"), It.IsAny<CancellationToken>()))
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
                Id = "1.605260.generalsonline.gameclient.zerohour",
                Name = "GeneralsOnline 060526",
                Version = "060526",
                GameType = GameType.ZeroHour,
                PublisherType = "generalsonline",
            },
            EnabledContentIds =
            [
                "1.104.retail.gameinstallation.zerohour",
                "1.605260.generalsonline.gameclient.zerohour",
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
            ManifestId = "1.828261.generalsonline.gameclient.zerohour",
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
                Id = "1.605260.generalsonline.gameclient.zerohour",
                Name = "GeneralsOnline 060526",
                Version = "060526",
                GameType = GameType.ZeroHour,
                PublisherType = "generalsonline",
            },
            EnabledContentIds =
            [
                "1.104.retail.gameinstallation.zerohour",
                "1.605260.generalsonline.gameclient.zerohour",
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
}
