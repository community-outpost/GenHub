using System.Threading.Tasks;
using GenHub.Common.ViewModels;
using GenHub.Core.Models.Enums;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Settings.ViewModels;
using Xunit;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Contains unit tests for the <see cref="MainViewModel"/> class.
/// </summary>
public class MainViewModelTests
{
    /// <summary>
    /// Tests that <see cref="MainViewModel.InitializeAsync"/> completes successfully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_CompletesSuccessfully()
    {
        // Arrange
        var vm = new MainViewModel(
            new GameProfileLauncherViewModel(),
            new DownloadsViewModel(),
            new SettingsViewModel());

        // Act
        var task = vm.InitializeAsync();
        await task;

        // Assert
        Assert.True(task.IsCompletedSuccessfully);
        Assert.NotNull(vm);
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
        var vm = new MainViewModel(
            new GameProfileLauncherViewModel(),
            new DownloadsViewModel(),
            new SettingsViewModel());
        vm.SelectTabCommand.Execute(tab);
        Assert.Equal(tab, vm.SelectedTab);
    }

    /// <summary>
    /// Tests that <see cref="MainViewModel"/> can be instantiated successfully.
    /// </summary>
    [Fact]
    public void Constructor_CreatesValidInstance()
    {
        // Act
        var vm = new MainViewModel(
            new GameProfileLauncherViewModel(),
            new DownloadsViewModel(),
            new SettingsViewModel());

        // Assert
        Assert.NotNull(vm);
        Assert.IsType<MainViewModel>(vm);
    }

    /// <summary>
    /// Tests that multiple calls to <see cref="MainViewModel.InitializeAsync"/> are safe.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_MultipleCallsAreSafe()
    {
        // Arrange
        var vm = new MainViewModel(
            new GameProfileLauncherViewModel(),
            new DownloadsViewModel(),
            new SettingsViewModel());

        // Act
        await vm.InitializeAsync();
        await vm.InitializeAsync();

        // Assert
        Assert.NotNull(vm);
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
        var vm = new MainViewModel(
            new GameProfileLauncherViewModel(),
            new DownloadsViewModel(),
            new SettingsViewModel());

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
