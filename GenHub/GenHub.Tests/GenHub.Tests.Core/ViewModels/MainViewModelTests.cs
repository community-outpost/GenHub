using System.Threading.Tasks;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Settings.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Contains unit tests for the <see cref="MainViewModel"/> class.
/// </summary>
public class MainViewModelTests
{
    /// <summary>
    /// Tests that <see cref="MainViewModel"/> can be instantiated successfully.
    /// </summary>
    [Fact]
    public void Constructor_CreatesValidInstance()
    {
        // Arrange
        var mockOrchestrator = new Mock<IGameInstallationDetectionOrchestrator>();
        var logger = NullLogger<MainViewModel>.Instance;

        var vm = new MainViewModel(
            new GameProfileLauncherViewModel(),
            new DownloadsViewModel(),
            new SettingsViewModel(),
            mockOrchestrator.Object,
            logger);

        // Assert
        Assert.NotNull(vm);
        Assert.IsType<MainViewModel>(vm);
    }

    /// <summary>
    /// Tests that executing <see cref="MainViewModel.SelectTabCommand"/> sets the <see cref="MainViewModel.SelectedTab"/> property.
    /// </summary>
    /// <param name="tab">The tab to select.</param>
    [Theory]
    [InlineData(NavigationTab.GameProfiles)]
    [InlineData(NavigationTab.Downloads)]
    [InlineData(NavigationTab.Settings)]
    public void SelectTabCommand_SetsSelectedTab(NavigationTab tab)
    {
        var mockOrchestrator = new Mock<IGameInstallationDetectionOrchestrator>();
        var logger = NullLogger<MainViewModel>.Instance;
        var vm = new MainViewModel(
            new GameProfileLauncherViewModel(),
            new DownloadsViewModel(),
            new SettingsViewModel(),
            mockOrchestrator.Object,
            logger);
        vm.SelectTabCommand.Execute(tab);
        Assert.Equal(tab, vm.SelectedTab);
    }

    /// <summary>
    /// Verifies ScanForGamesAsync can be called.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanForGamesAsync_CanBeCalled()
    {
        // Arrange
        var mockOrchestrator = new Mock<IGameInstallationDetectionOrchestrator>();
        var logger = NullLogger<MainViewModel>.Instance;
        var viewModel = new MainViewModel(
            new GameProfileLauncherViewModel(),
            new DownloadsViewModel(),
            new SettingsViewModel(),
            mockOrchestrator.Object,
            logger);

        // Act & Assert
        await viewModel.ScanForGamesAsync();
        Assert.True(true); // Test passes if no exception is thrown
    }

    /// <summary>
    /// Tests that multiple calls to <see cref="MainViewModel.InitializeAsync"/> are safe.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_MultipleCallsAreSafe()
    {
        // Arrange
        var mockOrchestrator = new Mock<IGameInstallationDetectionOrchestrator>();
        var logger = NullLogger<MainViewModel>.Instance;
        var vm = new MainViewModel(
            new GameProfileLauncherViewModel(),
            new DownloadsViewModel(),
            new SettingsViewModel(),
            mockOrchestrator.Object,
            logger);

        // Act & Assert
        await vm.InitializeAsync();
        await vm.InitializeAsync(); // Should not throw
        Assert.True(true);
    }

    /// <summary>
    /// Tests that CurrentTabViewModel returns the correct ViewModel based on SelectedTab.
    /// </summary>
    /// <param name="tab">The tab to select.</param>
    [Theory]
    [InlineData(NavigationTab.GameProfiles)]
    [InlineData(NavigationTab.Downloads)]
    [InlineData(NavigationTab.Settings)]
    public void CurrentTabViewModel_ReturnsCorrectViewModel(NavigationTab tab)
    {
        var mockOrchestrator = new Mock<IGameInstallationDetectionOrchestrator>();
        var logger = NullLogger<MainViewModel>.Instance;
        var vm = new MainViewModel(
            new GameProfileLauncherViewModel(),
            new DownloadsViewModel(),
            new SettingsViewModel(),
            mockOrchestrator.Object,
            logger);

        vm.SelectTabCommand.Execute(tab);

        var currentViewModel = vm.CurrentTabViewModel;
        Assert.NotNull(currentViewModel);

        switch (tab)
        {
            case NavigationTab.GameProfiles:
                Assert.IsType<GameProfileLauncherViewModel>(currentViewModel);
                break;
            case NavigationTab.Downloads:
                Assert.IsType<DownloadsViewModel>(currentViewModel);
                break;
            case NavigationTab.Settings:
                Assert.IsType<SettingsViewModel>(currentViewModel);
                break;
        }
    }
}
