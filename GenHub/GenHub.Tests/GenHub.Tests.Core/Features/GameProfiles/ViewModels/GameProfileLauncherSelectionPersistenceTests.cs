using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Verifies that the launcher's profile selection survives an application restart.
/// </summary>
public sealed class GameProfileLauncherSelectionPersistenceTests : IDisposable
{
    private const string FirstProfileId = "profile-1";
    private const string SecondProfileId = "profile-2";

    private readonly string _dataRoot;
    private readonly string _settingsPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileLauncherSelectionPersistenceTests"/> class.
    /// </summary>
    public GameProfileLauncherSelectionPersistenceTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "GenHub_SelectionPersistence_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);
        _settingsPath = Path.Combine(_dataRoot, FileTypes.SettingsFileName);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    /// <summary>
    /// A launched profile is selected again by a new view model over the same data root.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InitializeAsync_AfterRestart_RestoresLaunchedProfileSelectionAsync()
    {
        var firstRun = CreateViewModel(CreateSettingsService(), out var facade);
        await firstRun.InitializeAsync();
        Assert.Null(firstRun.SelectedProfile);

        var secondProfile = firstRun.Profiles.Single(p => p.ProfileId == SecondProfileId);
        await firstRun.LaunchProfileCommand.ExecuteAsync(secondProfile);
        await firstRun.LastUsedProfileSave;
        facade.Verify(f => f.LaunchProfileAsync(SecondProfileId, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);

        var restartedSettings = CreateSettingsService();
        Assert.Equal(SecondProfileId, restartedSettings.Get().LastUsedProfileId);

        var restarted = CreateViewModel(restartedSettings, out _);
        await restarted.InitializeAsync();

        Assert.NotNull(restarted.SelectedProfile);
        Assert.Equal(SecondProfileId, restarted.SelectedProfile.ProfileId);
        Assert.Same(restarted.Profiles.Single(p => p.ProfileId == SecondProfileId), restarted.SelectedProfile);
    }

    /// <summary>
    /// A remembered profile that was deleted leaves nothing selected and does not rewrite settings.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InitializeAsync_WhenLastUsedProfileWasDeleted_SelectsNothingAsync()
    {
        var settings = CreateSettingsService();
        Assert.True(await settings.TryUpdateAndSaveAsync(s =>
        {
            s.LastUsedProfileId = "deleted-profile";
            return true;
        }));
        var writtenAt = File.GetLastWriteTimeUtc(_settingsPath);

        var viewModel = CreateViewModel(CreateSettingsService(), out _);
        await viewModel.InitializeAsync();
        await viewModel.LastUsedProfileSave;

        Assert.Null(viewModel.SelectedProfile);
        Assert.True(viewModel.HasLoadedProfilesSuccessfully);
        Assert.Equal("deleted-profile", CreateSettingsService().Get().LastUsedProfileId);
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(_settingsPath));
    }

    /// <summary>
    /// Restoring a selection and relaunching the selected profile never saves settings again.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SelectionChanges_OnlySaveWhenTheSelectedProfileChangesAsync()
    {
        var current = new UserSettings { LastUsedProfileId = FirstProfileId };
        var settings = new Mock<IUserSettingsService>();
        settings.Setup(s => s.Get()).Returns(() => (UserSettings)current.Clone());
        settings.Setup(s => s.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()))
            .Returns<Func<UserSettings, bool>>(apply => Task.FromResult(apply(current)));

        var viewModel = CreateViewModel(settings.Object, out _);
        await viewModel.InitializeAsync();
        Assert.Equal(FirstProfileId, viewModel.SelectedProfile?.ProfileId);

        await viewModel.LaunchProfileCommand.ExecuteAsync(viewModel.SelectedProfile!);
        await viewModel.LastUsedProfileSave;
        settings.Verify(s => s.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()), Times.Never);

        viewModel.SelectedProfile = viewModel.Profiles.Single(p => p.ProfileId == SecondProfileId);
        await viewModel.LastUsedProfileSave;
        viewModel.SelectedProfile = viewModel.Profiles.Single(p => p.ProfileId == SecondProfileId);
        viewModel.SelectedProfile = viewModel.Profiles.OfType<AddProfileItemViewModel>().Single();
        await viewModel.LastUsedProfileSave;

        settings.Verify(s => s.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()), Times.Once);
        Assert.Equal(SecondProfileId, current.LastUsedProfileId);
    }

    private static GameProfileLauncherViewModel CreateViewModel(IUserSettingsService settings, out Mock<IProfileLauncherFacade> facade)
    {
        var profileManager = new Mock<IGameProfileManager>();
        profileManager
            .Setup(m => m.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess(
            [
                new GameProfile { Id = FirstProfileId, Name = "First" },
                new GameProfile { Id = SecondProfileId, Name = "Second" },
            ]));

        facade = new Mock<IProfileLauncherFacade>();
        facade
            .Setup(f => f.LaunchProfileAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string profileId, bool _, CancellationToken _) => ProfileOperationResult<GameLaunchInfo>.CreateSuccess(new GameLaunchInfo
            {
                LaunchId = "launch-" + profileId,
                ProfileId = profileId,
                WorkspaceId = profileId,
                ProcessInfo = new GameProcessInfo { ProcessId = 123 },
            }));

        return new GameProfileLauncherViewModel(
            new Mock<IGameInstallationService>().Object,
            profileManager.Object,
            facade.Object,
            null!,
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            new Mock<IGameProcessManager>().Object,
            new Mock<IShortcutService>().Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            new ProfileResourceService(NullLogger<ProfileResourceService>.Instance),
            new Mock<IGameClientDetector>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            new Mock<ILocalizationService>().Object,
            userSettingsService: settings);
    }

    private FileBackedUserSettingsService CreateSettingsService() =>
        new(NullLogger<UserSettingsService>.Instance, new Mock<IAppConfiguration>().Object, _settingsPath);

    private sealed class FileBackedUserSettingsService : UserSettingsService
    {
        public FileBackedUserSettingsService(ILogger<UserSettingsService> logger, IAppConfiguration appConfig, string settingsFilePath)
            : base(logger, appConfig, initialize: false)
        {
            SetSettingsFilePath(settingsFilePath);
        }
    }
}
