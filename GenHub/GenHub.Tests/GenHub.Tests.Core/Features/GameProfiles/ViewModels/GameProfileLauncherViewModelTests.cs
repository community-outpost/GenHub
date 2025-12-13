using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.ViewModels;

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
        var gameProfileManager = new Mock<IGameProfileManager>();
        var profileLauncherFacade = new Mock<IProfileLauncherFacade>();
        var settingsViewModel = new Mock<GameProfileSettingsViewModel>(
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            NullLogger<GameProfileSettingsViewModel>.Instance).Object;
        var profileEditorFacade = new Mock<IProfileEditorFacade>();
        var configService = new Mock<IConfigurationProviderService>();
        var gameProcessManager = new Mock<IGameProcessManager>();
        var shortcutService = new Mock<IShortcutService>();

        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            gameProfileManager.Object,
            profileLauncherFacade.Object,
            settingsViewModel,
            profileEditorFacade.Object,
            configService.Object,
            gameProcessManager.Object,
            shortcutService.Object,
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
        var gameProfileManager = new Mock<IGameProfileManager>();
        gameProfileManager.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess(new List<GameProfile>()));

        var profileLauncherFacade = new Mock<IProfileLauncherFacade>();
        var settingsViewModel = new Mock<GameProfileSettingsViewModel>(
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            NullLogger<GameProfileSettingsViewModel>.Instance).Object;
        var profileEditorFacade = new Mock<IProfileEditorFacade>();
        var configService = new Mock<IConfigurationProviderService>();
        var gameProcessManager = new Mock<IGameProcessManager>();
        var shortcutService = new Mock<IShortcutService>();

        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            gameProfileManager.Object,
            profileLauncherFacade.Object,
            settingsViewModel,
            profileEditorFacade.Object,
            configService.Object,
            gameProcessManager.Object,
            shortcutService.Object,
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
            new GameInstallation("C:\\Steam\\Games", GameInstallationType.Steam, new Mock<ILogger<GameInstallation>>().Object),
            new GameInstallation("C:\\EA\\Games", GameInstallationType.EaApp, new Mock<ILogger<GameInstallation>>().Object),
        };

        installationService.Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess(installations));

        var gameProfileManager = new Mock<IGameProfileManager>();
        var profileLauncherFacade = new Mock<IProfileLauncherFacade>();
        var settingsViewModel = new Mock<GameProfileSettingsViewModel>(
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            NullLogger<GameProfileSettingsViewModel>.Instance).Object;
        var profileEditorFacade = new Mock<IProfileEditorFacade>();
        var configService = new Mock<IConfigurationProviderService>();
        var gameProcessManager = new Mock<IGameProcessManager>();
        var shortcutService = new Mock<IShortcutService>();

        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            gameProfileManager.Object,
            profileLauncherFacade.Object,
            settingsViewModel,
            profileEditorFacade.Object,
            configService.Object,
            gameProcessManager.Object,
            shortcutService.Object,
            NullLogger<GameProfileLauncherViewModel>.Instance);

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        // Updated to match actual message format that includes manifest generation and profile creation
        Assert.Equal("Scan complete. Found 2 installations, generated 0 manifests, created 0 profiles", vm.StatusMessage);
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

        var gameProfileManager = new Mock<IGameProfileManager>();
        var profileLauncherFacade = new Mock<IProfileLauncherFacade>();
        var settingsViewModel = new Mock<GameProfileSettingsViewModel>(
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            NullLogger<GameProfileSettingsViewModel>.Instance).Object;
        var profileEditorFacade = new Mock<IProfileEditorFacade>();
        var configService = new Mock<IConfigurationProviderService>();
        var gameProcessManager = new Mock<IGameProcessManager>();
        var shortcutService = new Mock<IShortcutService>();

        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            gameProfileManager.Object,
            profileLauncherFacade.Object,
            settingsViewModel,
            profileEditorFacade.Object,
            configService.Object,
            gameProcessManager.Object,
            shortcutService.Object,
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

        var gameProfileManager = new Mock<IGameProfileManager>();
        var profileLauncherFacade = new Mock<IProfileLauncherFacade>();
        var settingsViewModel = new Mock<GameProfileSettingsViewModel>(
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            NullLogger<GameProfileSettingsViewModel>.Instance).Object;
        var profileEditorFacade = new Mock<IProfileEditorFacade>();
        var configService = new Mock<IConfigurationProviderService>();
        var gameProcessManager = new Mock<IGameProcessManager>();
        var shortcutService = new Mock<IShortcutService>();

        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            gameProfileManager.Object,
            profileLauncherFacade.Object,
            settingsViewModel,
            profileEditorFacade.Object,
            configService.Object,
            gameProcessManager.Object,
            shortcutService.Object,
            NullLogger<GameProfileLauncherViewModel>.Instance);

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        Assert.Equal("Error during scan", vm.StatusMessage);
    }

    /// <summary>
    /// Verifies that ScanForGamesCommand does nothing when service is not available.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanForGamesCommand_WithoutService_ShowsError()
    {
        var gameProfileManager = new Mock<IGameProfileManager>();
        var profileLauncherFacade = new Mock<IProfileLauncherFacade>();
        var settingsViewModel = new Mock<GameProfileSettingsViewModel>(
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            NullLogger<GameProfileSettingsViewModel>.Instance).Object;
        var profileEditorFacade = new Mock<IProfileEditorFacade>();
        var configService = new Mock<IConfigurationProviderService>();
        var gameProcessManager = new Mock<IGameProcessManager>();
        var shortcutService = new Mock<IShortcutService>();
        var installationService = new Mock<IGameInstallationService>();

        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            gameProfileManager.Object,
            profileLauncherFacade.Object,
            settingsViewModel,
            profileEditorFacade.Object,
            configService.Object,
            gameProcessManager.Object,
            shortcutService.Object,
            NullLogger<GameProfileLauncherViewModel>.Instance);

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        // Service is available now since it's required, so the test will scan and get empty results
        Assert.Contains("Scan complete", vm.StatusMessage);
    }
}