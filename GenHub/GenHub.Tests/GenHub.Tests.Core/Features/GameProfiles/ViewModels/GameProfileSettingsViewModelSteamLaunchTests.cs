using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
/// Tests the Steam launch setting in <see cref="GameProfileSettingsViewModel"/>.
/// </summary>
public class GameProfileSettingsViewModelSteamLaunchTests
{
    private const string ProfileId = "steam-profile";
    private const string InstallId = "1.108.steam.gameinstallation.zh";

    private readonly Mock<IGameProfileManager> _gameProfileManagerMock = new();
    private readonly Mock<IProfileContentLoader> _contentLoaderMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<ILaunchRegistry> _launchRegistryMock = new();
    private readonly Mock<ISteamManifestPatcher> _patcherMock = new();
    private readonly GameProfileSettingsViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileSettingsViewModelSteamLaunchTests"/> class.
    /// </summary>
    public GameProfileSettingsViewModelSteamLaunchTests()
    {
        _viewModel = new GameProfileSettingsViewModel(
            _gameProfileManagerMock.Object,
            new Mock<IGameSettingsService>().Object,
            new Mock<IConfigurationProviderService>().Object,
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
            launchRegistry: _launchRegistryMock.Object,
            steamManifestPatcher: _patcherMock.Object);
    }

    /// <summary>
    /// Verifies that the setting is offered only for a Steam installation.
    /// </summary>
    /// <param name="installationType">The selected installation type.</param>
    /// <param name="expected">Whether the setting should be offered.</param>
    [Theory]
    [InlineData(GameInstallationType.Steam, true)]
    [InlineData(GameInstallationType.EaApp, false)]
    public void IsSteamLaunchSupported_FollowsSelectedInstallation(GameInstallationType installationType, bool expected)
    {
        var changed = false;
        _viewModel.PropertyChanged += (_, e) => changed |= e.PropertyName == nameof(GameProfileSettingsViewModel.IsSteamLaunchSupported);

        _viewModel.SelectedGameInstallation = CreateInstallation(installationType);

        Assert.True(changed);
        Assert.Equal(expected, _viewModel.IsSteamLaunchSupported);
    }

    /// <summary>
    /// Verifies that a new Steam profile records the chosen launch mode.
    /// </summary>
    /// <param name="useSteamLaunch">The user's choice.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveCommand_ForNewSteamProfile_SendsChosenLaunchModeAsync(bool useSteamLaunch)
    {
        CreateProfileRequest? captured = null;
        _gameProfileManagerMock
            .Setup(m => m.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "new-profile" }));
        _viewModel.Name = "Steam Profile";
        _viewModel.SelectedGameInstallation = CreateInstallation(GameInstallationType.Steam);
        _viewModel.UseSteamLaunch = useSteamLaunch;

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(captured);
        Assert.Equal(useSteamLaunch, captured!.UseSteamLaunch);
    }

    /// <summary>
    /// Verifies that a non-Steam profile is never saved with Steam launch enabled.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveCommand_ForNewNonSteamProfile_DisablesSteamLaunchAsync()
    {
        CreateProfileRequest? captured = null;
        _gameProfileManagerMock
            .Setup(m => m.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "new-profile" }));
        _viewModel.Name = "EA Profile";
        _viewModel.SelectedGameInstallation = CreateInstallation(GameInstallationType.EaApp);

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(captured);
        Assert.False(captured!.UseSteamLaunch);
    }

    /// <summary>
    /// Verifies that editing the launch mode persists it and patches the enabled manifests.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveCommand_WhenSteamLaunchChanged_PersistsAndPatchesManifestsAsync()
    {
        var updates = await InitializeExistingProfileAsync(useSteamLaunch: true);
        _viewModel.UseSteamLaunch = false;

        await _viewModel.SaveCommand.ExecuteAsync(null);

        var update = Assert.Single(updates);
        Assert.False(update.UseSteamLaunch);
        _patcherMock.Verify(p => p.PatchManifestAsync(InstallId, false), Times.Once);
    }

    /// <summary>
    /// Verifies that an unchanged launch mode does not rewrite manifests.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveCommand_WhenSteamLaunchUnchanged_DoesNotPatchManifestsAsync()
    {
        var updates = await InitializeExistingProfileAsync(useSteamLaunch: false);

        await _viewModel.SaveCommand.ExecuteAsync(null);

        var update = Assert.Single(updates);
        Assert.False(update.UseSteamLaunch);
        _patcherMock.Verify(p => p.PatchManifestAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a profile without a stored launch mode keeps launching directly after an unchanged save.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveCommand_WhenStoredLaunchModeMissing_KeepsDirectLaunchAsync()
    {
        var updates = await InitializeExistingProfileAsync(useSteamLaunch: null);

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.False(_viewModel.UseSteamLaunch);
        var update = Assert.Single(updates);
        Assert.False(update.UseSteamLaunch);
        _patcherMock.Verify(p => p.PatchManifestAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Verifies that manifests are patched from the saved launch mode when the setting changes mid-save.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveCommand_WhenSteamLaunchChangesDuringSave_PatchesSavedValueAsync()
    {
        var updates = await InitializeExistingProfileAsync(useSteamLaunch: true);
        _gameProfileManagerMock.Setup(m => m.UpdateProfileAsync(ProfileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, UpdateProfileRequest, CancellationToken>((_, request, _) =>
            {
                updates.Add(request);
                _viewModel.UseSteamLaunch = true;
            })
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = ProfileId }));
        _viewModel.UseSteamLaunch = false;

        await _viewModel.SaveCommand.ExecuteAsync(null);

        var update = Assert.Single(updates);
        Assert.False(update.UseSteamLaunch);
        _patcherMock.Verify(p => p.PatchManifestAsync(InstallId, false), Times.Once);
        _patcherMock.Verify(p => p.PatchManifestAsync(It.IsAny<string>(), true), Times.Never);
    }

    private static ContentDisplayItem CreateInstallation(GameInstallationType installationType) => new()
    {
        Id = InstallId,
        ManifestId = ManifestId.Create(InstallId),
        DisplayName = "Zero Hour",
        ContentType = ContentType.GameInstallation,
        GameType = GameType.ZeroHour,
        InstallationType = installationType,
        SourceId = "install-zh",
        IsEnabled = true,
        GameClient = new GameClient
        {
            Id = "client-zh",
            Name = "Zero Hour",
            GameType = GameType.ZeroHour,
            ExecutablePath = "generals.exe",
        },
    };

    private async Task<List<UpdateProfileRequest>> InitializeExistingProfileAsync(bool? useSteamLaunch)
    {
        var profile = new GameProfile
        {
            Id = ProfileId,
            Name = "Steam Profile",
            EnabledContentIds = [InstallId],
            UseSteamLaunch = useSteamLaunch,
            GameClient = new GameClient { Id = "client-zh", Name = "Zero Hour", GameType = GameType.ZeroHour },
        };
        var updates = new List<UpdateProfileRequest>();
        _gameProfileManagerMock.Setup(m => m.GetProfileAsync(ProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _gameProfileManagerMock.Setup(m => m.UpdateProfileAsync(ProfileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, UpdateProfileRequest, CancellationToken>((_, request, _) => updates.Add(request))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _launchRegistryMock.Setup(l => l.GetAllActiveLaunchesAsync()).ReturnsAsync(new List<GameLaunchInfo>());
        _contentLoaderMock.Setup(c => c.LoadEnabledContentForProfileAsync(profile))
            .ReturnsAsync(new ObservableCollection<CoreContentDisplayItem>());
        _contentLoaderMock.Setup(c => c.LoadAvailableGameInstallationsAsync()).ReturnsAsync([]);
        _contentLoaderMock.Setup(c => c.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([]);
        _manifestPoolMock.Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        await _viewModel.InitializeForProfileAsync(ProfileId);
        _viewModel.SelectedGameInstallation = CreateInstallation(GameInstallationType.Steam);
        _viewModel.EnabledContent.Add(CreateInstallation(GameInstallationType.Steam));
        Assert.Equal(useSteamLaunch ?? false, _viewModel.UseSteamLaunch);
        updates.Clear();
        return updates;
    }
}
