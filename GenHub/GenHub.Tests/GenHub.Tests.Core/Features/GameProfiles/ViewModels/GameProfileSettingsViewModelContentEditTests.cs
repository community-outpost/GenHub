using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using CoreContentDisplayItem = GenHub.Core.Models.Content.ContentDisplayItem;
using GameInstallationType = GenHub.Core.Models.Enums.GameInstallationType;
using GameType = GenHub.Core.Models.Enums.GameType;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Verifies that content edits in <see cref="GameProfileSettingsViewModel"/> keep the profile name and game client.
/// </summary>
public class GameProfileSettingsViewModelContentEditTests
{
    private const string ProfileId = "profile-tsh";
    private const string ProfileName = "TheSuperHackers - Zero Hour";
    private const string InstallId = "1.104.steam.gameinstallation.zerohour";
    private const string InstallSourceId = "steam_zh";
    private const string TshClientId = "1.20260101.thesuperhackers.gameclient.zerohour";
    private const string TshExecutablePath = "/games/zh/generalszh";
    private const string MapId = "1.0.local.map.tournamentdesert";

    private readonly Mock<IGameProfileManager> _gameProfileManagerMock = new();
    private readonly Mock<IGameSettingsService> _gameSettingsServiceMock = new();
    private readonly Mock<IConfigurationProviderService> _configProviderMock = new();
    private readonly Mock<IProfileContentLoader> _contentLoaderMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<IProfileContentLinker> _profileContentLinkerMock = new();
    private readonly Mock<ILaunchRegistry> _launchRegistryMock = new();
    private readonly List<UpdateProfileRequest> _updateRequests = [];
    private readonly GameProfileSettingsViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileSettingsViewModelContentEditTests"/> class.
    /// </summary>
    public GameProfileSettingsViewModelContentEditTests()
    {
        _viewModel = new GameProfileSettingsViewModel(
            _gameProfileManagerMock.Object,
            _gameSettingsServiceMock.Object,
            _configProviderMock.Object,
            _contentLoaderMock.Object,
            null,
            null,
            _manifestPoolMock.Object,
            null,
            null,
            null,
            null,
            NullLogger<GameProfileSettingsViewModel>.Instance,
            NullLogger<GameSettingsViewModel>.Instance,
            profileContentLinker: _profileContentLinkerMock.Object,
            launchRegistry: _launchRegistryMock.Object);
    }

    /// <summary>
    /// Verifies that enabling a map on a profile named after its client keeps the name and the stored client.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveAsync_AfterEnablingMapOnProfileNamedAfterClient_KeepsNameAndGameClientAsync()
    {
        SetupProfile(isRunning: false);
        await _viewModel.InitializeForProfileAsync(ProfileId);

        await EnableMapAsync();
        Assert.Equal(ProfileName, _viewModel.Name);

        await _viewModel.SaveCommand.ExecuteAsync(null);

        var request = Assert.Single(_updateRequests);
        Assert.Equal(ProfileName, request.Name);
        Assert.Contains(MapId, request.EnabledContentIds!);
        Assert.NotNull(request.GameClient);
        Assert.Equal(TshClientId, request.GameClient!.Id);
        Assert.Equal(TshExecutablePath, request.GameClient.ExecutablePath);
    }

    /// <summary>
    /// Verifies that enabling a map during hot-swap keeps the profile name and the stored client.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveAsync_AfterEnablingMapDuringHotswap_KeepsNameAndGameClientAsync()
    {
        SetupProfile(isRunning: true);
        await _viewModel.InitializeForProfileAsync(ProfileId);
        Assert.True(_viewModel.IsHotswapMode);

        await EnableMapAsync();
        Assert.Equal(ProfileName, _viewModel.Name);

        await _viewModel.SaveCommand.ExecuteAsync(null);

        var request = Assert.Single(_updateRequests);
        Assert.Equal(ProfileName, request.Name);
        Assert.Contains(MapId, request.EnabledContentIds!);
        Assert.Equal(TshClientId, request.GameClient?.Id);
        Assert.Equal(TshExecutablePath, request.GameClient?.ExecutablePath);
    }

    /// <summary>
    /// Verifies that a content edit on a custom-named profile does not replace the stored client with the installation client.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveAsync_AfterEnablingMapOnCustomNamedProfile_KeepsGameClientAsync()
    {
        SetupProfile(isRunning: false, profileName: "My Tournament Setup");
        await _viewModel.InitializeForProfileAsync(ProfileId);

        await EnableMapAsync();
        await _viewModel.SaveCommand.ExecuteAsync(null);

        var request = Assert.Single(_updateRequests);
        Assert.Equal("My Tournament Setup", request.Name);
        Assert.Equal(TshClientId, request.GameClient?.Id);
        Assert.Equal(TshExecutablePath, request.GameClient?.ExecutablePath);
    }

    /// <summary>
    /// Verifies that explicitly switching the game client still renames an auto-named profile and saves the new client.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveAsync_AfterSwitchingGameClient_UpdatesAutoNameAndGameClientAsync()
    {
        const string newClientId = "1.105.community.gameclient.zerohour";
        const string newClientName = "Zero Hour Community Client";

        SetupProfile(isRunning: false);
        await _viewModel.InitializeForProfileAsync(ProfileId);

        var newClientItem = new ContentDisplayItem
        {
            ManifestId = ManifestId.Create(newClientId),
            DisplayName = newClientName,
            ContentType = ContentType.GameClient,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            SourceId = InstallSourceId,
            GameClient = new GameClient
            {
                Id = newClientId,
                Name = newClientName,
                GameType = GameType.ZeroHour,
                ExecutablePath = "/games/community/generals",
            },
            CanToggle = true,
        };
        _viewModel.AvailableContent.Add(newClientItem);

        await _viewModel.EnableContentCommand.ExecuteAsync(newClientItem);
        Assert.Equal(newClientName, _viewModel.Name);

        await _viewModel.SaveCommand.ExecuteAsync(null);

        var request = Assert.Single(_updateRequests);
        Assert.Equal(newClientName, request.Name);
        Assert.Equal(newClientId, request.GameClient?.Id);
        Assert.Equal("/games/community/generals", request.GameClient?.ExecutablePath);
    }

    /// <summary>Editing client data without changing its identity saves the edited client.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task SaveAsync_AfterEditingSelectedClientData_PreservesEditsAsync()
    {
        SetupProfile(isRunning: false, includeClient: true);
        await _viewModel.InitializeForProfileAsync(ProfileId);
        var client = Assert.Single(_viewModel.EnabledContent, c => c.ContentType == ContentType.GameClient);
        client.GameClient!.ExecutablePath = "/games/edited/generalszh";
        client.GameClient.WorkingDirectory = "/games/edited";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        var request = Assert.Single(_updateRequests);
        Assert.Equal(TshClientId, request.GameClient?.Id);
        Assert.Equal("/games/edited/generalszh", request.GameClient?.ExecutablePath);
        Assert.Equal("/games/edited", request.GameClient?.WorkingDirectory);
    }

    /// <summary>Manifest refreshes do not turn a subsequent map edit into a client selection change.</summary>
    /// <param name="replaceClient">Whether to replace the client instead of the installation.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveAsync_AfterManifestReplacementAndMapEdit_PreservesNameAsync(bool replaceClient)
    {
        SetupProfile(isRunning: false, includeClient: replaceClient);
        await _viewModel.InitializeForProfileAsync(ProfileId);
        var oldId = replaceClient ? TshClientId : InstallId;
        var newId = replaceClient
            ? "1.20260102.thesuperhackers.gameclient.zerohour"
            : "1.105.steam.gameinstallation.zerohour";
        var replacement = new CoreContentDisplayItem
        {
            Id = newId,
            ManifestId = newId,
            DisplayName = "Updated manifest name",
            ContentType = replaceClient ? ContentType.GameClient : ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            SourceId = InstallSourceId,
            GameClient = new GameClient
            {
                Id = newId,
                Name = "Updated manifest name",
                GameType = GameType.ZeroHour,
                ExecutablePath = "/games/updated/generalszh",
            },
        };
        _contentLoaderMock.Setup(c => c.CreateManifestDisplayItem(
            It.Is<ContentManifest>(m => m.Id.Value == newId),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<bool>()))
            .Returns(replacement);

        await _viewModel.HandleManifestReplacementAsync(oldId, newId);
        await EnableMapAsync();
        await _viewModel.SaveCommand.ExecuteAsync(null);

        var request = Assert.Single(_updateRequests);
        Assert.Equal(ProfileName, request.Name);
        Assert.Contains(newId, request.EnabledContentIds!);
        Assert.Equal(replaceClient ? newId : TshClientId, request.GameClient?.Id);
        Assert.Equal(replaceClient ? "/games/updated/generalszh" : TshExecutablePath, request.GameClient?.ExecutablePath);
    }

    private static GameLaunchInfo CreateActiveLaunch(string profileId) => new()
    {
        LaunchId = "launch-1",
        ProfileId = profileId,
        WorkspaceId = "ws-1",
        ProcessInfo = new GameProcessInfo
        {
            ProcessId = 1234,
            ProcessName = "generalszh",
            StartTime = DateTime.UtcNow,
        },
    };

    private async Task EnableMapAsync()
    {
        var mapItem = new ContentDisplayItem
        {
            ManifestId = ManifestId.Create(MapId),
            DisplayName = "Tournament Desert",
            ContentType = ContentType.Map,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            CanToggle = true,
        };
        _viewModel.AvailableContent.Add(mapItem);

        await _viewModel.EnableContentCommand.ExecuteAsync(mapItem);
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == MapId && c.IsEnabled);
    }

    private void SetupProfile(bool isRunning, string profileName = ProfileName, bool includeClient = false)
    {
        var profile = new GameProfile
        {
            Id = ProfileId,
            Name = profileName,
            EnabledContentIds = [InstallId, TshClientId],
            GameInstallationId = InstallSourceId,
            GameClient = new GameClient
            {
                Id = TshClientId,
                Name = ProfileName,
                GameType = GameType.ZeroHour,
                PublisherType = "thesuperhackers",
                InstallationId = InstallSourceId,
                ExecutablePath = TshExecutablePath,
                WorkingDirectory = "/games/zh",
            },
        };

        var installItem = new CoreContentDisplayItem
        {
            Id = InstallId,
            ManifestId = InstallId,
            DisplayName = "zerohour v1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            SourceId = InstallSourceId,
            GameClient = new GameClient
            {
                Id = InstallId,
                Name = "zerohour v1.04",
                GameType = GameType.ZeroHour,
                InstallationId = InstallSourceId,
                ExecutablePath = "/games/zh/generals.exe",
                WorkingDirectory = "/games/zh",
            },
        };

        _gameProfileManagerMock.Setup(m => m.GetProfileAsync(ProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _gameProfileManagerMock.Setup(m => m.UpdateProfileAsync(ProfileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, UpdateProfileRequest, CancellationToken>((_, request, _) =>
            {
                if (request.EnabledContentIds != null)
                {
                    _updateRequests.Add(request);
                }
            })
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync())
            .ReturnsAsync(isRunning ? [CreateActiveLaunch(ProfileId)] : []);

        var enabled = new ObservableCollection<CoreContentDisplayItem> { installItem };
        if (includeClient)
        {
            enabled.Add(new CoreContentDisplayItem
            {
                Id = TshClientId,
                ManifestId = TshClientId,
                DisplayName = ProfileName,
                ContentType = ContentType.GameClient,
                GameType = GameType.ZeroHour,
                GameClient = profile.GameClient.Clone(),
            });
        }

        _contentLoaderMock.Setup(c => c.LoadEnabledContentForProfileAsync(profile))
            .ReturnsAsync(enabled);
        _contentLoaderMock.Setup(c => c.LoadAvailableGameInstallationsAsync())
            .ReturnsAsync([installItem]);
        _contentLoaderMock.Setup(c => c.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([]);

        _manifestPoolMock.Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));
        _manifestPoolMock.Setup(m => m.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManifestId id, CancellationToken _) =>
                OperationResult<ContentManifest?>.CreateSuccess(new ContentManifest { Id = id, Name = id.Value }));

        _profileContentLinkerMock.Setup(p => p.UpdateProfileUserDataAsync(
            ProfileId,
            It.IsAny<IEnumerable<ContentManifest>>(),
            It.IsAny<GameType>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
    }
}
