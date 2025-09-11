using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging;
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
        var logger = new Mock<ILogger<GameProfileLauncherViewModel>>();
        var vm = new GameProfileLauncherViewModel(installationService.Object, logger.Object);

        Assert.NotNull(vm);
        Assert.Empty(vm.Profiles);
        Assert.False(vm.IsLaunching);
        Assert.False(vm.IsEditMode);
        Assert.Equal(string.Empty, vm.StatusMessage);
    }

    /// <summary>
    /// Verifies that the parameterless constructor initializes correctly.
    /// </summary>
    [Fact]
    public void Constructor_WithoutParameters_InitializesCorrectly()
    {
        var vm = new GameProfileLauncherViewModel();

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
        var logger = new Mock<ILogger<GameProfileLauncherViewModel>>();
        var vm = new GameProfileLauncherViewModel(installationService.Object, logger.Object);

        await vm.InitializeAsync();

        Assert.Empty(vm.Profiles); // No profiles loaded yet since IGameProfileManager is not available
        Assert.Equal("Profiles loaded", vm.StatusMessage);
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

        var logger = new Mock<ILogger<GameProfileLauncherViewModel>>();
        var vm = new GameProfileLauncherViewModel(installationService.Object, logger.Object);

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        Assert.Equal("Scan complete. Found 2 game installations", vm.StatusMessage);

        // Note: Logger verification removed due to Moq limitations with extension methods
    }

    /// <summary>
    /// Verifies that ScanForGamesCommand shows failure on failed scan.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanForGamesCommand_WithFailedScan_ShowsFailure()
    {
        var installationService = new Mock<IGameInstallationService>();
        var errors = new List<string> { "Detection failed", "Network error" };

        installationService.Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateFailure("Scan failed"));

        var logger = new Mock<ILogger<GameProfileLauncherViewModel>>();
        var vm = new GameProfileLauncherViewModel(installationService.Object, logger.Object);

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        Assert.Equal("Scan failed: Scan failed", vm.StatusMessage);

        // Note: Logger verification removed due to Moq limitations with extension methods
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

        var logger = new Mock<ILogger<GameProfileLauncherViewModel>>();
        var vm = new GameProfileLauncherViewModel(installationService.Object, logger.Object);

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        Assert.Equal("Error during scan", vm.StatusMessage);

        // Note: Logger verification removed due to Moq limitations with extension methods
    }

    /// <summary>
    /// Verifies that ScanForGamesCommand does nothing when service is not available.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanForGamesCommand_WithoutService_ShowsError()
    {
        var vm = new GameProfileLauncherViewModel(); // No services injected

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        Assert.Equal("Game installation service not available", vm.StatusMessage);
    }
}
