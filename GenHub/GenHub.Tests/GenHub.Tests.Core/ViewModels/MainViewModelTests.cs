using System.Threading.Tasks;
using GenHub.Common.ViewModels;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Settings.ViewModels;
using Xunit;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Verifies basic behavior of <see cref="MainViewModel"/>.
/// </summary>
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

        // Act & Assert
        await vm.InitializeAsync();
    }

    /// <summary>
    /// Tests that executing <see cref="MainViewModel.SelectTabCommand"/> sets the selected tab index.
    /// </summary>
    /// <param name="index">The tab index to select.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SelectTabCommand_SetsSelectedTabIndex(int index)
    {
        var vm = new MainViewModel(
            new GameProfileLauncherViewModel(),
            new DownloadsViewModel(),
            new SettingsViewModel());
        vm.SelectTabCommand.Execute(index);
        Assert.Equal(index, vm.SelectedTabIndex);
    }
}
