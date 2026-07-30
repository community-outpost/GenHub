using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Events;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Publishers;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Contains unit tests for <see cref="GameProfileLauncherViewModel"/>.
/// </summary>
public class GameProfileLauncherViewModelTests
{
    /// <summary>
    /// Verifies that the constructor initializes properties correctly.
    /// </summary>
    [Fact]
    public void Constructor_WithValidParameters_InitializesCorrectly()
    {
        var installationService = new Mock<IGameInstallationService>();
        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<IProfileLauncherFacade>().Object,
            new GameProfileSettingsViewModel(
                new Mock<IGameProfileManager>().Object,
                new Mock<IGameSettingsService>().Object,
                new Mock<IConfigurationProviderService>().Object,
                new Mock<IProfileContentLoader>().Object,
                CreateProfileResourceService(),
                new Mock<INotificationService>().Object,
                null,
                new Mock<IContentStorageService>().Object,
                null, // ILocalContentService
                NullLogger<GameProfileSettingsViewModel>.Instance,
                NullLogger<GameSettingsViewModel>.Instance),
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            new Mock<IGameProcessManager>().Object,
            new Mock<IShortcutService>().Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance);

        Assert.NotNull(vm);
        Assert.Empty(vm.Profiles);
        Assert.False(vm.IsLaunching);
        Assert.False(vm.IsEditMode);
        Assert.Equal(string.Empty, vm.StatusMessage);
    }

    /// <summary>
    /// Verifies that InitializeAsync loads profiles successfully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_LoadsProfiles_Successfully()
    {
        var installationService = new Mock<IGameInstallationService>();
        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<IProfileLauncherFacade>().Object,
            new GameProfileSettingsViewModel(
                new Mock<IGameProfileManager>().Object,
                new Mock<IGameSettingsService>().Object,
                new Mock<IConfigurationProviderService>().Object,
                new Mock<IProfileContentLoader>().Object,
                CreateProfileResourceService(),
                new Mock<INotificationService>().Object,
                null,
                new Mock<IContentStorageService>().Object,
                null, // ILocalContentService
                NullLogger<GameProfileSettingsViewModel>.Instance,
                NullLogger<GameSettingsViewModel>.Instance),
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            new Mock<IGameProcessManager>().Object,
            new Mock<IShortcutService>().Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance);

        await vm.InitializeAsync();

        Assert.Empty(vm.Profiles); // No profiles returned by mock
    }

    /// <summary>
    /// Verifies that ScanForGamesCommand shows success on successful scan.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanForGamesCommand_WithSuccessfulScan_ShowsSuccess()
    {
        var installationService = new Mock<IGameInstallationService>();
        var installations = new List<GameInstallation>
        {
            new("C:\\Steam\\Games", GameInstallationType.Steam, new Mock<ILogger<GameInstallation>>().Object),
            new("C:\\EA\\Games", GameInstallationType.EaApp, new Mock<ILogger<GameInstallation>>().Object),
        };

        installationService.Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess(installations));

        var shortcutService = new Mock<IShortcutService>();
        var notificationService = new Mock<INotificationService>();
        var publisherOrchestrator = new Mock<IPublisherProfileOrchestrator>();
        var profileManager = new Mock<IGameProfileManager>();
        var editorFacade = new Mock<IProfileEditorFacade>();

        var setupWizardService = new Mock<ISetupWizardService>();
        setupWizardService.Setup(x => x.RunSetupWizardAsync(It.IsAny<IEnumerable<GameInstallation>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SetupWizardResult { Confirmed = true });

        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            profileManager.Object,
            null!,
            null!,
            editorFacade.Object,
            null!,
            null!,
            shortcutService.Object,
            publisherOrchestrator.Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            notificationService.Object,
            setupWizardService.Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance);

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        // Updated to match actual message format that includes manifest generation and profile creation
        Assert.Equal("Scan complete. Found 2 installations, created 0 profiles", vm.StatusMessage);
    }

    /// <summary>
    /// Verifies that ScanForGamesCommand shows failure on failed scan.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanForGamesCommand_WithFailedScan_ShowsFailure()
    {
        var installationService = new Mock<IGameInstallationService>();
        const string expectedError = "Detection service unavailable";

        installationService.Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateFailure(expectedError));

        var shortcutService = new Mock<IShortcutService>();

        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            shortcutService.Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance);

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        Assert.Equal($"Scan failed: {expectedError}", vm.StatusMessage);
    }

    /// <summary>
    /// Verifies that ScanForGamesCommand handles exceptions gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanForGamesCommand_WithException_HandlesGracefully()
    {
        var installationService = new Mock<IGameInstallationService>();
        installationService.Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        var shortcutService = new Mock<IShortcutService>();

        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<IProfileLauncherFacade>().Object,
            null!, // SettingsVM
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            new Mock<IGameProcessManager>().Object,
            shortcutService.Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance);

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        // Should handle exception gracefully by setting an error message
        Assert.Contains("Error during scan", vm.StatusMessage);
    }

    /// <summary>
    /// Verifies that ScanForGamesCommand does nothing when service is not available.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanForGamesCommand_WithoutService_ShowsError()
    {
        var installationService = new Mock<IGameInstallationService>();
        var shortcutService = new Mock<IShortcutService>();

        // Setup to return failure
        installationService.Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateFailure("Service unavailable"));

        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            shortcutService.Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance);

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        // Service returns failure, so we should get a scan failed message
        Assert.Contains("Scan failed", vm.StatusMessage);
    }

    /// <summary>
    /// Verifies that CopyProfile generates a unique name for the copied profile.
    /// </summary>
    [Fact]
    public void GenerateUniqueProfileName_CreatesUniqueName()
    {
        // Arrange
        var vm = CreateViewModelWithMockDependencies();

        // Add some existing profiles to simulate name conflicts
        var existingProfile1 = new GameProfileItemViewModel("id1", new Mock<IGameProfile>().Object, "icon.png", "cover.jpg")
        {
            Name = $"Test Profile {ProfileConstants.CopyNameSuffix}",
        };
        var existingProfile2 = new GameProfileItemViewModel("id2", new Mock<IGameProfile>().Object, "icon.png", "cover.jpg")
        {
            Name = $"Test Profile {string.Format(ProfileConstants.CopyNameNumberedFormat, 2)}",
        };

        vm.Profiles.Add(existingProfile1);
        vm.Profiles.Add(existingProfile2);

        // Act
        var uniqueName = vm.GenerateUniqueProfileName("Test Profile");

        // Assert
        Assert.Equal($"Test Profile {string.Format(ProfileConstants.CopyNameNumberedFormat, 3)}", uniqueName);
    }

    /// <summary>
    /// A process that dies after the launch was announced as running must not vanish
    /// silently: the late failure surfaces through the same status, error, and
    /// notification channel as a failed launch, naming the archive when known.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ProcessExitedWithFailure_SurfacesTheFailureToTheUser()
    {
        var gameProcessManager = new Mock<IGameProcessManager>();
        var notificationService = new Mock<INotificationService>();
        var vm = CreateViewModelWithMockDependencies(gameProcessManager, notificationService);

        // InitializeAsync is where the view model subscribes to ProcessExited.
        await vm.InitializeAsync();

        var profile = CreateProfileItem("Failing Profile");
        profile.ProcessId = 4242;
        profile.IsProcessRunning = true;
        vm.Profiles.Add(profile);

        gameProcessManager.Raise(m => m.ProcessExited += null, new GameProcessExitedEventArgs
        {
            ProcessId = 4242,
            ExitCode = 1,
            StandardErrorTail = "init abort",
            UnmountableArchives = ["TexturesZH.big"],
        });

        Assert.False(profile.IsProcessRunning);
        Assert.Equal(0, profile.ProcessId);
        Assert.Contains("exited unexpectedly", vm.StatusMessage);
        Assert.Contains("Failing Profile", vm.StatusMessage);
        Assert.Contains("TexturesZH.big", vm.ErrorMessage);
        notificationService.Verify(
            n => n.ShowError(
                "Game Exited Unexpectedly",
                It.Is<string>(s => s.Contains("TexturesZH.big") && s.Contains("Failing Profile")),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// A clean exit is the user quitting: the running state clears and nothing is
    /// reported as an error.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ProcessExitedCleanly_DoesNotReportAFailure()
    {
        var gameProcessManager = new Mock<IGameProcessManager>();
        var notificationService = new Mock<INotificationService>();
        var vm = CreateViewModelWithMockDependencies(gameProcessManager, notificationService);

        // InitializeAsync is where the view model subscribes to ProcessExited.
        await vm.InitializeAsync();

        var profile = CreateProfileItem("Quitting Profile");
        profile.ProcessId = 4243;
        profile.IsProcessRunning = true;
        vm.Profiles.Add(profile);

        gameProcessManager.Raise(m => m.ProcessExited += null, new GameProcessExitedEventArgs
        {
            ProcessId = 4243,
            ExitCode = 0,
        });

        Assert.False(profile.IsProcessRunning);
        Assert.Equal(0, profile.ProcessId);
        Assert.Equal(string.Empty, vm.ErrorMessage);
        notificationService.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// A stop the user asked for kills the process with a non-zero exit code; that must
    /// not raise the "exited unexpectedly" alarm — the stop path's own status stands.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ProcessExitedFromARequestedStop_DoesNotRaiseTheFailureAlarm()
    {
        var gameProcessManager = new Mock<IGameProcessManager>();
        var notificationService = new Mock<INotificationService>();
        var vm = CreateViewModelWithMockDependencies(gameProcessManager, notificationService);

        // InitializeAsync is where the view model subscribes to ProcessExited.
        await vm.InitializeAsync();

        var profile = CreateProfileItem("Stopped Profile");
        profile.ProcessId = 4244;
        profile.IsProcessRunning = true;
        vm.Profiles.Add(profile);

        // The status a completed stop leaves behind; the exit event must not replace it.
        vm.StatusMessage = "Stopped Profile stopped successfully";

        gameProcessManager.Raise(m => m.ProcessExited += null, new GameProcessExitedEventArgs
        {
            ProcessId = 4244,
            ExitCode = 137,
            TerminationRequested = true,
        });

        Assert.False(profile.IsProcessRunning);
        Assert.Equal(0, profile.ProcessId);
        Assert.Equal("Stopped Profile stopped successfully", vm.StatusMessage);
        Assert.Equal(string.Empty, vm.ErrorMessage);
        notificationService.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    private static ProfileResourceService CreateProfileResourceService()
    {
        return new ProfileResourceService(NullLogger<ProfileResourceService>.Instance);
    }

    private static SuperHackersProvider CreateSuperHackersProvider()
    {
        var discovererMock = new Mock<IContentDiscoverer>();
        discovererMock.Setup(x => x.SourceName).Returns("GitHubReleasesDiscoverer");

        var resolverMock = new Mock<IContentResolver>();
        resolverMock.Setup(x => x.ResolverId).Returns(GenHub.Core.Constants.SuperHackersConstants.ResolverId);

        var delivererMock = new Mock<IContentDeliverer>();
        delivererMock.Setup(x => x.SourceName).Returns(GenHub.Core.Constants.ContentSourceNames.GitHubDeliverer);

        var gitHubApiClientMock = new Mock<IGitHubApiClient>();

        var loaderMock = new Mock<IProviderDefinitionLoader>();

        return new SuperHackersProvider(
            loaderMock.Object,
            gitHubApiClientMock.Object,
            [resolverMock.Object],
            [delivererMock.Object],
            new Mock<GenHub.Core.Interfaces.Content.IContentValidator>().Object,
            NullLogger<SuperHackersProvider>.Instance);
    }

    /// <summary>
    /// Creates a GameProfileLauncherViewModel with mocked dependencies for testing.
    /// </summary>
    /// <returns>A GameProfileLauncherViewModel instance for testing.</returns>
    private static GameProfileLauncherViewModel CreateViewModelWithMockDependencies()
    {
        return CreateViewModelWithMockDependencies(
            new Mock<IGameProcessManager>(),
            new Mock<INotificationService>());
    }

    /// <summary>
    /// Creates a GameProfileLauncherViewModel wired to the given process manager and
    /// notification mocks, so tests can raise process events and observe notifications.
    /// </summary>
    /// <param name="gameProcessManager">The process manager mock the view model subscribes to.</param>
    /// <param name="notificationService">The notification service mock to observe.</param>
    /// <returns>A GameProfileLauncherViewModel instance for testing.</returns>
    private static GameProfileLauncherViewModel CreateViewModelWithMockDependencies(
        Mock<IGameProcessManager> gameProcessManager,
        Mock<INotificationService> notificationService)
    {
        var gameProfileManager = new Mock<IGameProfileManager>();

        // InitializeAsync must complete cleanly: it is what subscribes the view model to
        // ProcessExited, and a failed profile load would pollute the error state these
        // tests assert on.
        gameProfileManager
            .Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        return new GameProfileLauncherViewModel(
            new Mock<IGameInstallationService>().Object,
            gameProfileManager.Object,
            new Mock<IProfileLauncherFacade>().Object,
            new GameProfileSettingsViewModel(
                new Mock<IGameProfileManager>().Object,
                new Mock<IGameSettingsService>().Object,
                new Mock<IConfigurationProviderService>().Object,
                new Mock<IProfileContentLoader>().Object,
                CreateProfileResourceService(),
                new Mock<INotificationService>().Object,
                null,
                new Mock<IContentStorageService>().Object,
                null, // ILocalContentService
                NullLogger<GameProfileSettingsViewModel>.Instance,
                NullLogger<GameSettingsViewModel>.Instance),
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            gameProcessManager.Object,
            new Mock<IShortcutService>().Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            notificationService.Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance);
    }

    /// <summary>
    /// Creates a profile item view model backed by a mocked profile.
    /// </summary>
    /// <param name="name">The profile name.</param>
    /// <returns>A profile item for the launcher's collection.</returns>
    private static GameProfileItemViewModel CreateProfileItem(string name)
    {
        var profile = new Mock<IGameProfile>();
        profile.SetupGet(p => p.Name).Returns(name);
        profile.SetupGet(p => p.Version).Returns("1.0");
        profile.SetupGet(p => p.ExecutablePath).Returns(string.Empty);

        return new GameProfileItemViewModel("profile-1", profile.Object, string.Empty, string.Empty);
    }
}
