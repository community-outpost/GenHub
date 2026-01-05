using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Models.Enums;
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
            new Mock<IManifestGenerationService>().Object,
            new Mock<IContentManifestPool>().Object,
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
            new Mock<IManifestGenerationService>().Object,
            new Mock<IContentManifestPool>().Object,
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
            new Mock<IManifestGenerationService>().Object,
            new Mock<IContentManifestPool>().Object,
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
            new Mock<IManifestGenerationService>().Object,
            new Mock<IContentManifestPool>().Object,
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
            new Mock<IManifestGenerationService>().Object,
            new Mock<IContentManifestPool>().Object,
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
            new Mock<IManifestGenerationService>().Object,
            new Mock<IContentManifestPool>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance);

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        // Service returns failure, so we should get a scan failed message
        Assert.Contains("Scan failed", vm.StatusMessage);
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
}
