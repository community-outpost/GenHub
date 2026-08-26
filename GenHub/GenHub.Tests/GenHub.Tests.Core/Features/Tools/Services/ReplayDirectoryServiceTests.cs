using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
                ManifestId = "1.20260821.superhackers.gameclient.zerohour",
                Publisher = "superhackers",
                GameType = "ZeroHour",
                Version = "2026-08-21",
                Description = "SuperHackers 2026-08-21",
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
    /// Verifies that profile creation fails gracefully when no matched client is known.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProfileForReplayAsync_WhenNoMatchedClient_ReturnsFailureAsync()
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

        var service = new ReplayDirectoryService(
            _mockHeaderParser.Object,
            _mockCrcRegistry.Object,
            _mockScopeFactory.Object,
            NullLogger<ReplayDirectoryService>.Instance);

        var result = await service.CreateProfileForReplayAsync(replay);

        Assert.False(result.Success);
        Assert.Contains("not mapped to any known game client", result.FirstError);
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
        var replay = new ReplayFile
        {
            FileName = "Test.rep",
            FullPath = "/path/Test.rep",
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
                ManifestId = "1.20260821.superhackers.gameclient.zerohour",
                Publisher = "superhackers",
                GameType = "ZeroHour",
                Version = "2026-08-21",
                Description = "SuperHackers 2026-08-21",
            },
        };

        // Compatible state
        replay.CompatibilityStatus = ReplayCompatibilityStatus.Compatible;
        replay.MatchingProfileName = "ZH SuperHackers";
        Assert.Equal("Ready to Play", replay.CompatibilityBadgeText);
        Assert.Contains("ZH SuperHackers", replay.CompatibilityTooltip);

        // RequiresProfile state
        replay.CompatibilityStatus = ReplayCompatibilityStatus.RequiresProfile;
        Assert.Equal("Profile Needed", replay.CompatibilityBadgeText);
        Assert.Contains("Click 'Create Profile'", replay.CompatibilityTooltip);

        // Downloadable state
        replay.CompatibilityStatus = ReplayCompatibilityStatus.Downloadable;
        Assert.Equal("Download Required", replay.CompatibilityBadgeText);
        Assert.Contains("available on CDN", replay.CompatibilityTooltip);

        // Orphaned state
        replay.CompatibilityStatus = ReplayCompatibilityStatus.Orphaned;
        Assert.Equal("Mismatch Risk", replay.CompatibilityBadgeText);
        Assert.Contains("mismatch", replay.CompatibilityTooltip, StringComparison.OrdinalIgnoreCase);

        // Unknown state
        replay.CompatibilityStatus = ReplayCompatibilityStatus.Unknown;
        Assert.Equal("Unknown", replay.CompatibilityBadgeText);
    }
}
