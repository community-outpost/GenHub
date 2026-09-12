using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Features.Tools.ReplayManager.ViewModels;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests for <see cref="GameClientSelectionViewModel"/>.
/// </summary>
public sealed class GameClientSelectionViewModelTests
{
    /// <summary>
    /// Verifies that ManifestMatchesGame matches manifests with explicit TargetGame.
    /// </summary>
    /// <param name="manifestGame">The GameType assigned to the manifest.</param>
    /// <param name="targetGame">The target GameType being filtered for.</param>
    /// <param name="expected">The expected match result.</param>
    [Theory]
    [InlineData(GameType.ZeroHour, GameType.ZeroHour, true)]
    [InlineData(GameType.Generals, GameType.Generals, true)]
    [InlineData(GameType.ZeroHour, GameType.Generals, false)]
    [InlineData(GameType.Generals, GameType.ZeroHour, false)]
    public void ManifestMatchesGame_ExplicitTargetGame_ReturnsExpected(GameType manifestGame, GameType targetGame, bool expected)
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.gameclient.zh"),
            TargetGame = manifestGame,
        };

        var result = GameClientSelectionViewModel.ManifestMatchesGame(manifest, targetGame);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that ManifestMatchesGame matches unknown target game manifests by ID token prefix, suffix, or exact match.
    /// </summary>
    /// <param name="manifestId">The manifest ID string to evaluate.</param>
    /// <param name="targetGame">The target GameType being filtered for.</param>
    /// <param name="expected">The expected match result.</param>
    [Theory]
    [InlineData("1.0.generalsonline.gameclient.zerohour", GameType.ZeroHour, true)]
    [InlineData("1.0.generalsonline.gameclient.zh", GameType.ZeroHour, true)]
    [InlineData("1.0.generalsonline.gameclient.zerohour-generalsonline-60hz", GameType.ZeroHour, true)]
    [InlineData("1.0.generalsonline.gameclient.zh-generalsonline-60hz", GameType.ZeroHour, true)]
    [InlineData("1.0.community.gameclient.custom-zerohour", GameType.ZeroHour, true)]
    [InlineData("1.0.community.gameclient.custom-zh", GameType.ZeroHour, true)]
    [InlineData("1.0.generalsonline.gameclient.generals", GameType.Generals, true)]
    [InlineData("1.0.generalsonline.gameclient.generals-generalsonline-60hz", GameType.Generals, true)]
    [InlineData("1.0.community.gameclient.custom-generals", GameType.Generals, true)]
    [InlineData("1.0.generalsonline.gameclient.generals-generalsonline-60hz", GameType.ZeroHour, false)]
    [InlineData("1.0.generalsonline.gameclient.zerohour-generalsonline-60hz", GameType.Generals, false)]
    [InlineData("1.0.other.gameclient.unknown", GameType.ZeroHour, false)]
    [InlineData("1.0.other.gameclient.unknown", GameType.Generals, false)]
    public void ManifestMatchesGame_UnknownTargetGame_MatchesTokensCorrectly(string manifestId, GameType targetGame, bool expected)
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create(manifestId),
            TargetGame = GameType.Unknown,
        };

        var result = GameClientSelectionViewModel.ManifestMatchesGame(manifest, targetGame);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that when a replay has zero compatible clients, ShowAllClients remains false
    /// and FilteredClients is empty, rather than dumping all incompatible clients onto the user.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadClientsForReplayAsync_WhenNoCompatibleClients_DoesNotAutoShowAllClientsAsync()
    {
        var mockProfileMgr = new Moq.Mock<GenHub.Core.Interfaces.GameProfiles.IGameProfileManager>();
        var mockManifestPool = new Moq.Mock<GenHub.Core.Interfaces.Manifest.IContentManifestPool>();
        var mockCrcRegistry = new Moq.Mock<GenHub.Core.Interfaces.Tools.ReplayManager.ICrcMappingRegistry>();
        var mockLogger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<GameClientSelectionViewModel>>();

        mockProfileMgr
            .Setup(p => p.GetAllProfilesAsync(Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.ProfileOperationResult<IReadOnlyList<GenHub.Core.Models.GameProfile.GameProfile>>.CreateSuccess([]));

        mockManifestPool
            .Setup(m => m.GetAllManifestsAsync(Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var vm = new GameClientSelectionViewModel(
            mockProfileMgr.Object,
            mockManifestPool.Object,
            mockCrcRegistry.Object,
            mockLogger.Object);

        // Replay with unrecognized/unmapped CRC (e.g. i-fly.rep)
        var replay = new ReplayFile
        {
            FileName = "i-fly.rep",
            FullPath = "/replays/i-fly.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0x88BEB180,
            },
        };

        await vm.LoadClientsForReplayAsync(GameType.ZeroHour, replay);

        Assert.False(vm.ShowAllClients);
        Assert.False(vm.HasCompatibleCrcClients);
        Assert.Equal(0, vm.CompatibleCount);
        Assert.Empty(vm.FilteredClients);

        // User can explicitly toggle ShowAllClients
        vm.ToggleShowAllCommand.Execute(null);
        Assert.True(vm.ShowAllClients);
    }

    /// <summary>
    /// Verifies that profiles dedicated to another replay are ignored during game client discovery.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadClientsForReplayAsync_IgnoresProfilesDedicatedToAnotherReplayAsync()
    {
        var mockProfileMgr = new Moq.Mock<GenHub.Core.Interfaces.GameProfiles.IGameProfileManager>();
        var mockManifestPool = new Moq.Mock<GenHub.Core.Interfaces.Manifest.IContentManifestPool>();
        var mockCrcRegistry = new Moq.Mock<GenHub.Core.Interfaces.Tools.ReplayManager.ICrcMappingRegistry>();
        var mockLogger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<GameClientSelectionViewModel>>();

        var otherReplayProfile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "profile-for-other-replay",
            Name = "Zero Hour 1.04 (Oct 17 2005) (Replay: other_replay)",
            Description = "[replay:other_replay.rep] Dedicated profile for other_replay",
            GameClient = new GenHub.Core.Models.GameClients.GameClient
            {
                Id = "client-other-replay",
                Name = "Zero Hour 1.04 (Oct 17 2005)",
                GameType = GameType.ZeroHour,
                PublisherType = "Retail",
            },
        };

        mockProfileMgr
            .Setup(p => p.GetAllProfilesAsync(Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.ProfileOperationResult<IReadOnlyList<GenHub.Core.Models.GameProfile.GameProfile>>.CreateSuccess([otherReplayProfile]));

        mockManifestPool
            .Setup(m => m.GetAllManifestsAsync(Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var vm = new GameClientSelectionViewModel(
            mockProfileMgr.Object,
            mockManifestPool.Object,
            mockCrcRegistry.Object,
            mockLogger.Object);

        var replay = new ReplayFile
        {
            FileName = "my_replay.rep",
            FullPath = "/replays/my_replay.rep",
            SizeInBytes = 1024,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                ExeCrc = 0xDA2B4B18,
            },
        };

        await vm.LoadClientsForReplayAsync(GameType.ZeroHour, replay);

        vm.ToggleShowAllCommand.Execute(null);
        Assert.DoesNotContain(vm.FilteredClients, c => c.Name.Contains("other_replay") || c.Description.Contains("other_replay"));
    }
}
